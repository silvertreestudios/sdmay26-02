public sealed class CombatLogRoll
{
    public int NaturalRoll { get; set; }
    public int TotalModifier { get; set; }
    public int Total { get; set; }
    public int DifficultyClass { get; set; }
    public string Label { get; set; } = "Attack Roll";

    public string Summary => Total + " vs AC " + DifficultyClass;
}
