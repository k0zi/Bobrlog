using System.Globalization;

namespace Bobrlog.App.Services;

public sealed record Language(string Code, string NativeName, string Culture)
{
    public override string ToString() => NativeName;
}

/// <summary>UI languages. The language is applied once at startup; switching requires a restart.</summary>
public static class Localization
{
    public const string DefaultLanguage = "en";

    public static IReadOnlyList<Language> SupportedLanguages { get; } =
    [
        new("en", "English", "en-US"),
        new("hu", "Magyar", "hu-HU"),
        new("fi", "Suomi", "fi-FI"),
        new("de", "Deutsch", "de-DE"),
    ];

    /// <summary>The language the running instance was started with.</summary>
    public static Language Current { get; private set; } = SupportedLanguages[0];

    public static Language Find(string? code) =>
        SupportedLanguages.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase))
        ?? SupportedLanguages[0];

    /// <summary>Sets the UI and formatting culture of the process (including thread pool threads).</summary>
    public static void Apply(string? code)
    {
        Current = Find(code);
        var culture = CultureInfo.GetCultureInfo(Current.Culture);
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}
