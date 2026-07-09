using System.Collections.Generic;

public sealed class CombatLogDamage
{
    public List<CombatLogDamagePart> Parts { get; } = new();
    public int Total { get; set; }

    public string Summary
    {
        get
        {
            if (Total <= 0)
                return "No damage";
            if (Parts.Count == 1)
                return Total + " " + Parts[0].DamageType;
            return Total + " damage";
        }
    }
}

public sealed class CombatLogDamagePart
{
    public string DamageType { get; set; } = string.Empty;
    public int Amount { get; set; }

    public CombatLogDamagePart()
    {
    }

    public CombatLogDamagePart(string damageType, int amount)
    {
        DamageType = damageType ?? string.Empty;
        Amount = amount;
    }
}

