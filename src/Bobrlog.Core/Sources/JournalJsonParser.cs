using System.Text;
using System.Text.Json;
using Bobrlog.Core.Models;

namespace Bobrlog.Core.Sources;

/// <summary>Parses one line of <c>journalctl -o json</c> output.</summary>
public static class JournalJsonParser
{
    public static LogEntry? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(line);
            return Parse(doc.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static LogEntry? Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in root.EnumerateObject())
        {
            var value = ReadValue(prop.Value);
            if (value is not null)
                fields[prop.Name] = value;
        }

        if (!fields.TryGetValue("__REALTIME_TIMESTAMP", out var rt) || !long.TryParse(rt, out var micros))
            return null;

        var priority = fields.TryGetValue("PRIORITY", out var p) && int.TryParse(p, out var pv) ? pv : 6;

        var entry = new LogEntry
        {
            Timestamp = FromMicros(micros),
            Message = fields.GetValueOrDefault("MESSAGE") ?? string.Empty,
            Priority = priority,
            BootId = fields.GetValueOrDefault("_BOOT_ID"),
            Identifier = fields.GetValueOrDefault("SYSLOG_IDENTIFIER"),
            Unit = fields.GetValueOrDefault("UNIT") ?? fields.GetValueOrDefault("_SYSTEMD_UNIT"),
            UserUnit = fields.GetValueOrDefault("_SYSTEMD_USER_UNIT") ?? fields.GetValueOrDefault("USER_UNIT"),
            Pid = fields.TryGetValue("_PID", out var pid) && int.TryParse(pid, out var pidv) ? pidv : null,
            Command = fields.GetValueOrDefault("_COMM"),
            Transport = fields.GetValueOrDefault("_TRANSPORT"),
            Cursor = fields.GetValueOrDefault("__CURSOR"),
            Fields = fields,
        };
        entry.Severity = SeverityMapper.FromPriority(priority);
        return entry;
    }

    public static DateTimeOffset FromMicros(long micros) =>
        DateTimeOffset.FromUnixTimeMilliseconds(micros / 1000).AddTicks(micros % 1000 * 10).ToLocalTime();

    /// <summary>
    /// journalctl emits strings normally, byte arrays for non-UTF8/binary values,
    /// arrays of those for repeated fields and null for oversized values.
    /// </summary>
    private static string? ReadValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return value.GetString();
            case JsonValueKind.Number:
                return value.GetRawText();
            case JsonValueKind.Array:
                if (IsByteArray(value))
                    return DecodeBytes(value);
                var parts = new List<string>();
                foreach (var item in value.EnumerateArray())
                {
                    var s = ReadValue(item);
                    if (s is not null)
                        parts.Add(s);
                }
                return parts.Count == 0 ? null : string.Join('\n', parts);
            default:
                return null;
        }
    }

    private static bool IsByteArray(JsonElement array)
    {
        var any = false;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Number)
                return false;
            any = true;
        }
        return any;
    }

    private static string DecodeBytes(JsonElement array)
    {
        var bytes = new byte[array.GetArrayLength()];
        var i = 0;
        foreach (var item in array.EnumerateArray())
            bytes[i++] = item.TryGetByte(out var b) ? b : (byte)'?';
        // Kernel messages can contain control characters; keep printable text only.
        var text = Encoding.UTF8.GetString(bytes);
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            sb.Append(char.IsControl(c) && c != '\n' && c != '\t' ? ' ' : c);
        return sb.ToString();
    }
}
