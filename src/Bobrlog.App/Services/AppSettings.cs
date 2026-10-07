using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bobrlog.App.Services;

public enum SourceMode
{
    Auto,
    Local,
}

public sealed class AppSettings
{
    public SourceMode SourceMode { get; set; } = SourceMode.Auto;
    public bool LowPriority { get; set; } = true;
    public int PageSize { get; set; } = 2000;
    /// <summary>UI language code (see <see cref="Localization.SupportedLanguages"/>).</summary>
    public string Language { get; set; } = Localization.DefaultLanguage;

    private static string ConfigHome =>
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } x
            ? x
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

    private static string FilePath => Path.Combine(ConfigHome, "bobrlog", "settings.json");

    /// <summary>Settings of versions released under the old name (JournalReader); read once, saved under the new name.</summary>
    private static string LegacyFilePath => Path.Combine(ConfigHome, "journalreader", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            var path = File.Exists(FilePath) ? FilePath : LegacyFilePath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.AppSettings) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, SettingsJsonContext.Default.AppSettings));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
