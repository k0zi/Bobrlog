using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Bobrlog.Core.Models;

namespace Bobrlog.Core.Rules;

/// <summary>One classification rule loaded from JSON. All specified conditions must match.</summary>
public sealed class EventRule
{
    public required string Id { get; init; }
    public EventCategory Category { get; init; }

    /// <summary>SYSLOG_IDENTIFIER or _COMM must be one of these (case-insensitive).</summary>
    public string[]? Identifiers { get; init; }
    public string? Transport { get; init; }
    public string? UnitPattern { get; init; }
    public string? MessagePattern { get; init; }

    /// <summary>Forces the severity.</summary>
    public Severity? Severity { get; init; }
    /// <summary>Raises the severity to at least this level.</summary>
    public Severity? MinSeverity { get; init; }

    /// <summary>Short cause used by the boot analysis ("why did it crash"), keyed by language code.</summary>
    [JsonPropertyName("reason")]
    public Dictionary<string, string>? ReasonTexts { get; init; }
    /// <summary>Longer explanation shown in the details pane, keyed by language code.</summary>
    [JsonPropertyName("explanation")]
    public Dictionary<string, string>? ExplanationTexts { get; init; }

    /// <summary>The reason in the current UI language (English fallback).</summary>
    [JsonIgnore] public string? Reason => Localize(ReasonTexts);
    /// <summary>The explanation in the current UI language (English fallback).</summary>
    [JsonIgnore] public string? Explanation => Localize(ExplanationTexts);

    public const string FallbackLanguage = "en";

    internal static string? Localize(Dictionary<string, string>? texts)
    {
        if (texts is null)
            return null;
        return texts.TryGetValue(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, out var text)
               || texts.TryGetValue(FallbackLanguage, out text)
            ? text
            : texts.Values.FirstOrDefault();
    }

    [JsonIgnore] internal Regex? MessageRegex { get; private set; }
    [JsonIgnore] internal Regex? UnitRegex { get; private set; }
    [JsonIgnore] internal HashSet<string>? IdentifierSet { get; private set; }

    internal void Compile()
    {
        const RegexOptions opts = RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        if (MessagePattern is not null)
            MessageRegex = new Regex(MessagePattern, opts, TimeSpan.FromMilliseconds(50));
        if (UnitPattern is not null)
            UnitRegex = new Regex(UnitPattern, opts, TimeSpan.FromMilliseconds(50));
        if (Identifiers is not null)
            IdentifierSet = new HashSet<string>(Identifiers, StringComparer.OrdinalIgnoreCase);
    }

    internal bool Matches(LogEntry entry)
    {
        if (Transport is not null && !string.Equals(entry.Transport, Transport, StringComparison.Ordinal))
            return false;
        if (IdentifierSet is not null &&
            !(entry.Identifier is { } id && IdentifierSet.Contains(id)) &&
            !(entry.Command is { } comm && IdentifierSet.Contains(comm)))
            return false;
        if (UnitRegex is not null && !(entry.AnyUnit is { } unit && SafeMatch(UnitRegex, unit)))
            return false;
        if (MessageRegex is not null && !SafeMatch(MessageRegex, entry.Message))
            return false;
        return true;
    }

    private static bool SafeMatch(Regex regex, string input)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
