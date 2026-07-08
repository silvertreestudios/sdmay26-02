using System.Collections.Generic;

public enum CombatLogEntryKind
{
    System,
    Attack,
    Movement,
    Turn,
    Damage
}

public enum CombatLogOutcome
{
    None,
    System,
    Success,
    CriticalSuccess,
    Failure,
    CriticalFailure,
    Damage
}

public sealed class CombatLogEntry
{
    public CombatLogEntryKind Kind { get; set; } = CombatLogEntryKind.System;
    public CombatLogOutcome Outcome { get; set; } = CombatLogOutcome.System;
    public string Actor { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public CombatLogRoll Roll { get; set; }
    public CombatLogDamage Damage { get; set; }
    public List<CombatLogDetail> Details { get; } = new();
    public HashSet<string> Tags { get; } = new();
    public bool Expanded { get; set; }

    public static CombatLogEntry FromMessage(string message, IEnumerable<string> tags = null, CombatLogEntryKind kind = CombatLogEntryKind.System)
    {
        CombatLogEntry entry = new CombatLogEntry
        {
            Kind = kind,
            Outcome = kind == CombatLogEntryKind.Damage ? CombatLogOutcome.Damage : CombatLogOutcome.System,
            Message = message ?? string.Empty
        };
        if (tags != null)
        {
            foreach (string tag in tags)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                    entry.Tags.Add(tag.ToLowerInvariant());
            }
        }
        return entry;
    }
}

