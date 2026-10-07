using System.Text.Json;
using Bobrlog.Core.Models;

namespace Bobrlog.Core.Rules;

/// <summary>Assigns category, severity and explanation to entries. First matching rule wins.</summary>
public sealed class RuleEngine
{
    public IReadOnlyList<EventRule> Rules { get; }

    public RuleEngine(IEnumerable<EventRule> rules)
    {
        Rules = rules.ToList();
        foreach (var r in Rules)
            r.Compile();
    }

    public static RuleEngine LoadDefault()
    {
        using var stream = typeof(RuleEngine).Assembly.GetManifestResourceStream("Bobrlog.Core.Rules.default-rules.json")
                           ?? throw new InvalidOperationException("default-rules.json resource missing");
        return new RuleEngine(Parse(stream));
    }

    public static List<EventRule> Parse(Stream json) =>
        JsonSerializer.Deserialize(json, CoreJsonContext.Default.ListEventRule) ?? [];

    public EventRule? Apply(LogEntry entry)
    {
        var rule = Match(entry);
        if (rule is not null)
        {
            entry.Category = rule.Category;
            entry.RuleId = rule.Id;
            entry.Explanation = rule.Explanation;
            if (rule.Severity is { } forced)
                entry.Severity = forced;
            else if (rule.MinSeverity is { } min && entry.Severity < min)
                entry.Severity = min;
            return rule;
        }

        entry.Category = FallbackCategory(entry);
        return null;
    }

    public EventRule? Match(LogEntry entry)
    {
        foreach (var rule in Rules)
            if (rule.Matches(entry))
                return rule;
        return null;
    }

    public EventRule? FindById(string? id) => id is null ? null : Rules.FirstOrDefault(r => r.Id == id);

    private static EventCategory FallbackCategory(LogEntry entry)
    {
        if (entry.IsKernel)
            return EventCategory.KernelHardware;
        if (entry.UserUnit is not null)
            return EventCategory.Applications;
        if (entry.Field("_UID") is { } uid && int.TryParse(uid, out var u) && u >= 1000)
            return EventCategory.Applications;
        if (entry.Identifier is "systemd" or "init")
            return EventCategory.Services;
        return EventCategory.Other;
    }
}
