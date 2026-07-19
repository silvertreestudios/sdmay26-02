using System.Collections.Generic;

public static class CombatLogEntryFormatter
{
    public static string ToSummary(CombatLogEntry entry)
    {
        if (entry == null)
            return string.Empty;

        if (entry.Kind != CombatLogEntryKind.Attack)
            return string.IsNullOrWhiteSpace(entry.Message)
                ? JoinNonEmpty(" | ", entry.Actor, entry.Action, entry.Target)
                : entry.Message;

        string participants = JoinNonEmpty(" -> ", entry.Actor, entry.Target);
        string damage = entry.Damage == null ? "No damage" : entry.Damage.Summary;
        return JoinNonEmpty(
            " | ",
            participants,
            entry.Action,
            entry.Roll?.Summary,
            FormatOutcome(entry.Outcome),
            damage
        );
    }

    public static string ToPlainText(CombatLogEntry entry)
    {
        if (entry == null)
            return string.Empty;

        string text = ToSummary(entry);
        if (entry.Details.Count == 0)
            return text;

        List<string> lines = new() { text };
        foreach (CombatLogDetail detail in entry.Details)
        {
            if (detail == null)
                continue;
            lines.Add("  " + detail.Label + ": " + detail.Value);
        }
        return string.Join("\n", lines);
    }

    public static string FormatOutcome(CombatLogOutcome outcome)
    {
        return outcome switch
        {
            CombatLogOutcome.CriticalSuccess => "Critical Hit",
            CombatLogOutcome.Success => "Hit",
            CombatLogOutcome.CriticalFailure => "Critical Miss",
            CombatLogOutcome.Failure => "Miss",
            CombatLogOutcome.Damage => "Damage",
            CombatLogOutcome.System => "System",
            _ => string.Empty,
        };
    }

    private static string JoinNonEmpty(string separator, params string[] parts)
    {
        List<string> filtered = new();
        foreach (string part in parts)
        {
            if (!string.IsNullOrWhiteSpace(part))
                filtered.Add(part);
        }
        return string.Join(separator, filtered);
    }
}
