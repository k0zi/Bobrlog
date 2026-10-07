using System.Text.Json;
using System.Text.Json.Serialization;
using Bobrlog.Core.Models;

namespace Bobrlog.Core.Service;

/// <summary>
/// Wire protocol between the GUI and the privileged background service.
/// The client sends one <see cref="ServiceRequest"/> JSON line; the service answers with
/// newline-delimited JSON and closes the connection:
/// <list type="bullet">
/// <item>query: raw <c>journalctl -o json</c> lines (classified on the client)</item>
/// <item>boots: the raw <c>journalctl --list-boots -o json</c> array as one line</item>
/// <item>crashes: one JSON array of <see cref="CrashReport"/></item>
/// <item>ping: one <see cref="ServiceHello"/></item>
/// </list>
/// Errors are reported as a single <c>{"__error":"…"}</c> line.
/// </summary>
public static class ServiceProtocol
{
    public const int Version = 1;
    public const string SocketDirectory = "/run/bobrlog";
    public const string DefaultSocketPath = SocketDirectory + "/bobrlog.sock";

    /// <summary>Socket path; BOBRLOG_SOCKET overrides it (for development/testing).</summary>
    public static string SocketPath =>
        Environment.GetEnvironmentVariable("BOBRLOG_SOCKET") is { Length: > 0 } path ? path : DefaultSocketPath;
    public const string ConfigDirectory = "/etc/bobrlog";
    public const string AllowedUidsPath = ConfigDirectory + "/allowed-uids";
    public const string ErrorField = "__error";
    public const int MaxLimit = 200_000;

    public static string Serialize(ServiceRequest request) =>
        JsonSerializer.Serialize(request, ServiceJsonContext.Default.ServiceRequest);

    public static ServiceRequest? DeserializeRequest(string line) =>
        JsonSerializer.Deserialize(line, ServiceJsonContext.Default.ServiceRequest);

    public static string Serialize(ServiceHello hello) =>
        JsonSerializer.Serialize(hello, ServiceJsonContext.Default.ServiceHello);

    public static ServiceHello? DeserializeHello(string line) =>
        JsonSerializer.Deserialize(line, ServiceJsonContext.Default.ServiceHello);

    public static string Serialize(List<CrashReport> crashes) =>
        JsonSerializer.Serialize(crashes, ServiceJsonContext.Default.ListCrashReport);

    public static List<CrashReport> DeserializeCrashes(string line) =>
        JsonSerializer.Deserialize(line, ServiceJsonContext.Default.ListCrashReport) ?? [];

    public static string Error(string message) =>
        JsonSerializer.Serialize(new Dictionary<string, string> { [ErrorField] = message },
            ServiceJsonContext.Default.DictionaryStringString);

    /// <summary>Returns the error text if the line is an error message.</summary>
    public static string? TryGetError(string line)
    {
        if (!line.StartsWith("{\"" + ErrorField, StringComparison.Ordinal))
            return null;
        using var doc = JsonDocument.Parse(line);
        return doc.RootElement.GetProperty(ErrorField).GetString();
    }
}

[JsonConverter(typeof(JsonStringEnumConverter<ServiceOperation>))]
public enum ServiceOperation
{
    Ping,
    Query,
    Boots,
    Crashes,
}

public sealed record ServiceRequest(ServiceOperation Op, JournalQuery? Query = null);

public sealed record ServiceHello(int Version, uint ServiceUid, string ServiceVersion);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ServiceRequest))]
[JsonSerializable(typeof(ServiceHello))]
[JsonSerializable(typeof(List<CrashReport>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal sealed partial class ServiceJsonContext : JsonSerializerContext;
