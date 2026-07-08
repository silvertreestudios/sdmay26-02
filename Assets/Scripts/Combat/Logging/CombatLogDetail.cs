public sealed class CombatLogDetail
{
    public string Label { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public CombatLogDetail()
    {
    }

    public CombatLogDetail(string label, string value)
    {
        Label = label ?? string.Empty;
        Value = value ?? string.Empty;
    }
}

