using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Bobrlog.Core.Resources;
using Bobrlog.Core.Rules;

namespace Bobrlog.Core.Tests;

public partial class LocalizationTests
{
    private static readonly string[] Languages = ["hu", "fi", "de"];
    private static readonly string[] AllLanguages = ["en", .. Languages];

    public static TheoryData<string> ResxFiles => new()
    {
        "src/Bobrlog.Core/Resources/CoreStrings",
        "src/Bobrlog.App/Resources/Strings",
    };

    [GeneratedRegex(@"\{(\d+)(?:[,:][^}]*)?\}")]
    private static partial Regex Placeholder();

    private static string RepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Bobrlog.sln")))
                return dir.FullName;
        throw new InvalidOperationException("Repository root not found");
    }

    private static Dictionary<string, string> ReadResx(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string?)d.Element("value") ?? "");

    private static string Placeholders(string text) =>
        string.Join(",", Placeholder().Matches(text).Select(m => m.Groups[1].Value).Distinct().Order());

    [Theory]
    [MemberData(nameof(ResxFiles))]
    public void Every_language_has_every_key_with_the_same_placeholders(string baseName)
    {
        var root = RepoRoot();
        var neutral = ReadResx(Path.Combine(root, baseName + ".resx"));
        Assert.NotEmpty(neutral);

        var problems = new StringBuilder();
        foreach (var lang in Languages)
        {
            var translated = ReadResx(Path.Combine(root, $"{baseName}.{lang}.resx"));
            foreach (var key in neutral.Keys.Except(translated.Keys))
                problems.AppendLine($"{lang}: missing {key}");
            foreach (var key in translated.Keys.Except(neutral.Keys))
                problems.AppendLine($"{lang}: extra {key}");
            foreach (var (key, value) in translated.Where(t => neutral.ContainsKey(t.Key)))
            {
                if (string.IsNullOrWhiteSpace(value))
                    problems.AppendLine($"{lang}: empty {key}");
                if (Placeholders(value) != Placeholders(neutral[key]))
                    problems.AppendLine($"{lang}: placeholders differ in {key}");
            }
        }
        Assert.True(problems.Length == 0, problems.ToString());
    }

    [Theory]
    [MemberData(nameof(ResxFiles))]
    public void Format_strings_and_date_patterns_are_valid(string baseName)
    {
        var root = RepoRoot();
        foreach (var lang in AllLanguages)
        {
            var path = Path.Combine(root, baseName + (lang == "en" ? ".resx" : $".{lang}.resx"));
            foreach (var (key, value) in ReadResx(path))
            {
                if (key.StartsWith("Fmt_", StringComparison.Ordinal))
                    _ = new DateTimeOffset(2026, 10, 7, 13, 5, 9, TimeSpan.Zero).ToString(value, CultureInfo.InvariantCulture);
                else
                    _ = string.Format(CultureInfo.InvariantCulture, value, 1, 2, 3, 4);
            }
        }
    }

    [Fact]
    public void Every_rule_text_is_translated()
    {
        var missing = TestData.Rules.Rules
            .SelectMany(r => new[] { ("reason", r.ReasonTexts), ("explanation", r.ExplanationTexts) }
                .Where(f => f.Item2 is not null)
                .SelectMany(f => AllLanguages.Where(l => !f.Item2!.TryGetValue(l, out var t) || string.IsNullOrWhiteSpace(t))
                    .Select(l => $"{r.Id}.{f.Item1}: {l}")))
            .ToList();
        Assert.Empty(missing);
    }

    private static EventRule ParseRule() =>
        RuleEngine.Parse(new MemoryStream(Encoding.UTF8.GetBytes(
            """[{ "id": "x", "reason": { "en": "Disk error", "hu": "Lemezhiba" } }]""")))[0];

    [Fact]
    [UseCulture("hu-HU")]
    public void Rule_text_follows_the_ui_language() => Assert.Equal("Lemezhiba", ParseRule().Reason);

    [Fact]
    [UseCulture("fi-FI")]
    public void Rule_text_falls_back_to_english() => Assert.Equal("Disk error", ParseRule().Reason);

    [Fact]
    public void Satellite_assemblies_are_deployed()
    {
        Assert.Equal("Hiba", CoreStrings.ResourceManager.GetString(nameof(CoreStrings.Severity_Error), CultureInfo.GetCultureInfo("hu-HU")));
        Assert.Equal("Virhe", CoreStrings.ResourceManager.GetString(nameof(CoreStrings.Severity_Error), CultureInfo.GetCultureInfo("fi-FI")));
        Assert.Equal("Fehler", CoreStrings.ResourceManager.GetString(nameof(CoreStrings.Severity_Error), CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("Error", CoreStrings.ResourceManager.GetString(nameof(CoreStrings.Severity_Error), CultureInfo.InvariantCulture));
    }
}
