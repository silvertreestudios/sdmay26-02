# Core Attributes and Ability Scores

## Overview

In Pathfinder 2e, six ability scores define a creature's fundamental capabilities. These scores affect nearly every aspect of the game, from attack rolls and skill checks to spell DC and carrying capacity.

## The Six Abilities

### Strength (STR)
- **Measures**: Physical power and athletic prowess
- **Used For**:
  - Melee attack rolls (for most weapons)
  - Melee damage
  - Athletic activities
  - Breaking objects
  - Carrying capacity and bulk limits
- **Key Skills**: Athletics

### Dexterity (DEX)
- **Measures**: Agility, balance, and reflexes
- **Used For**:
  - Armor Class (AC)
  - Reflex saves
  - Ranged attack rolls (for most weapons)
  - Initiative (often)
  - Stealth and movement
- **Key Skills**: Acrobatics, Stealth, Thievery

### Constitution (CON)
- **Measures**: Health, stamina, and vital force
- **Used For**:
  - Hit Points per level
  - Fortitude saves
  - Enduring physical hardship
  - Resisting poison and disease
- **Key Skills**: None directly (affects all through HP)

### Intelligence (INT)
- **Measures**: Reasoning, memory, and analytical thinking
- **Used For**:
  - Additional trained skills
  - Recall Knowledge checks
  - Arcane spellcasting
  - Learning and research
- **Key Skills**: Arcana, Crafting, Lore skills, Occultism, Society

### Wisdom (WIS)
- **Measures**: Awareness, intuition, and insight
- **Used For**:
  - Perception (spotting danger)
  - Will saves
  - Divine and Primal spellcasting
  - Reading situations and people
- **Key Skills**: Medicine, Nature, Perception, Religion, Survival

### Charisma (CHA)
- **Measures**: Force of personality and presence
- **Used For**:
  - Social interactions
  - Occult spellcasting
  - Leadership and inspiration
  - Performing
- **Key Skills**: Deception, Diplomacy, Intimidation, Performance

## Ability Score Values

### Score Range
- **Typical Range**: 3-22 for most creatures
- **Average Human**: 10 in all abilities
- **Maximum Natural Score (PC)**: 18 at level 1 (rare), typically increases by ~4 every 5 levels
- **Monster Scores**: Can exceed these limits, especially for high-level or powerful creatures

### Ability Modifiers
The modifier is the derived value actually used in gameplay:

```
Modifier = (Score - 10) / 2 (rounded down)
```

**Modifier Table:**
| Score | Modifier | Score | Modifier |
|-------|----------|-------|----------|
| 1     | -5       | 18    | +4       |
| 3     | -4       | 19    | +4       |
| 5     | -3       | 20    | +5       |
| 7     | -3       | 21    | +5       |
| 8     | -1       | 22    | +6       |
| 10    | +0       | 24    | +7       |
| 12    | +1       | 26    | +8       |
| 14    | +2       | 28    | +9       |
| 16    | +3       | 30    | +10      |

## Ability Score Structure

### Base Ability Score
```
AbilityScore {
  score: integer,        // The raw ability score (1-30+)
  modifier: integer,     // Calculated: (score - 10) / 2
  baseScore: integer,    // Starting score before any modifiers
  apex: boolean,         // Whether an Apex item has been applied to this ability
}
```

### Sources of Ability Scores

#### 1. Starting Scores (Level 1 PCs)
- **Ancestry**: Usually provides one or more boosts (including free boosts)
- **Background**: Provides two boosts (usually one fixed, one free)
- **Class**: Provides one boost to key ability
- **Free Boosts**: 4 free boosts to any abilities at level 1

**Boost Rules:**
- Each boost increases a score by +2
- Cannot boost the same ability above 18 at character creation
- After 18, it takes 2 boosts to increase by +2 (partial boosts grant +1)

#### 2. Ancestry Flaws (Rare)
- Some ancestries have flaws that reduce an ability by 2
- Voluntary flaws can be taken for additional boosts (with restrictions)

#### 3. Level-Up Boosts
- **Every 5 Levels** (5, 10, 15, 20): Gain 4 ability boosts
- Same boost rules apply as character creation

#### 4. Item Bonuses
- **Apex Items**: Magical items that provide +2 to an ability score (one per character)
- Cannot increase a score above 24
- Only one apex item can affect a character at a time

#### 5. Temporary Modifiers
- **Buffs**: Spells, items, or effects that temporarily increase an ability
- **Penalties**: Effects that temporarily decrease an ability
- **Status**: Long-term conditions or curses

### Monster Ability Scores
Monsters typically have:
- Pre-calculated ability scores based on their role and CR
- No need to track boosts or progression
- Fixed values in their stat block

**Example Monster Ability Block:**
```
abilities {
  str: { score: 18, modifier: +4 },
  dex: { score: 14, modifier: +2 },
  con: { score: 16, modifier: +3 },
  int: { score: 10, modifier: +0 },
  wis: { score: 12, modifier: +1 },
  cha: { score: 8, modifier: -1 }
}
```

## Ability Score Applications

### Attack Rolls
```
Attack Roll = 1d20 + Proficiency + Ability Modifier + Item Bonus + Other Bonuses
```

- **Melee Weapons**: Usually STR (except finesse)
- **Ranged Weapons**: Usually DEX
- **Finesse Weapons**: Choose STR or DEX
- **Spell Attacks**: Spellcasting ability modifier

### Damage Rolls
```
Damage Roll = Weapon Dice + Ability Modifier + Other Bonuses
```

- **Melee**: STR modifier (except for finesse, which can use DEX)
- **Ranged**: No ability modifier unless specified
- **Thrown**: STR modifier
- **Spells**: Usually no ability modifier unless specified

### Skill Checks
```
Skill Check = 1d20 + Proficiency + Ability Modifier + Item Bonus + Other Bonuses
```

Each skill is associated with a specific ability (see skill list above).

### Saving Throws
```
Save = 1d20 + Proficiency + Ability Modifier + Item Bonus + Other Bonuses
```

- **Fortitude**: CON
- **Reflex**: DEX
- **Will**: WIS

### Spell DC
```
Spell DC = 10 + Proficiency + Spellcasting Ability Modifier + Item Bonus + Other Bonuses
```

### Armor Class
```
AC = 10 + DEX Modifier (capped by armor) + Proficiency + Item Bonus + Other Bonuses
```

## Dynamic Ability Modifications

### Temporary Changes
The system must track:

1. **Status Bonuses**: Circumstantial improvements (e.g., from spells)
2. **Status Penalties**: Circumstantial reductions (e.g., from conditions)
3. **Untyped Bonuses/Penalties**: Stack with everything
4. **Conditions**: May affect abilities (e.g., enfeebled reduces STR)

### Calculation Priority
```
Final Modifier = Base Modifier 
                 + Highest Status Bonus 
                 - Highest Status Penalty 
                 + All Untyped Bonuses/Penalties
                 + Condition Effects
```

**Stacking Rules:**
- Status bonuses don't stack (take highest)
- Status penalties don't stack (take worst)
- Untyped bonuses/penalties always stack
- Item bonuses don't stack (take highest)

## Data Structure

### Complete Ability Score Object
```
Ability {
  // Core Values
  baseScore: integer,        // Starting value
  score: integer,            // Current total score
  modifier: integer,         // Calculated modifier
  
  // Sources
  ancestryBoosts: integer,   // Boosts from ancestry
  backgroundBoosts: integer, // Boosts from background
  classBoosts: integer,      // Boosts from class
  levelBoosts: integer,      // Boosts from leveling
  apexItem: boolean,         // Has apex item applied
  
  // Temporary Modifiers
  statusBonus: integer,      // Highest status bonus
  statusPenalty: integer,    // Highest status penalty
  itemBonus: integer,        // Highest item bonus
  untypedModifiers: integer[],  // All untyped modifiers
  
  // Conditions affecting this ability
  conditions: {
    name: string,
    effect: integer,
    source: string
  }[],
  
  // Metadata
  keyAbility: boolean,       // Is this a key ability for the character/class
  apex Available: boolean,   // Can apex item be applied
}
```

### Ability Score Set
```
AbilityScores {
  strength: Ability,
  dexterity: Ability,
  constitution: Ability,
  intelligence: Ability,
  wisdom: Ability,
  charisma: Ability,
  
  // Helper methods
  getModifier(ability: string): integer,
  applyTemporaryModifier(ability: string, type: string, value: integer),
  removeTemporaryModifier(modifierId: string),
  recalculate(): void
}
```

## Integration with Other Systems

### Class Features
- Some class features grant bonuses to specific abilities
- Key ability determines primary statistics
- Example: Barbarian rage adds temporary STR bonus

### Spells and Effects
- Many spells modify ability scores temporarily
- Must track duration and source of modifications
- Example: Bull's Strength adds +2 status bonus to STR

### Equipment
- Apex items provide permanent bonuses (up to +2)
- Some items may provide conditional bonuses
- Example: Headband of Inspired Wisdom

### Conditions
- Conditions like Enfeebled, Clumsy, Stupefied reduce abilities
- Severity tracks how much the ability is reduced
- Multiple instances of the same condition stack

## Examples

### Example 1: Level 1 Human Fighter
```
Starting Array (before ancestry/class):
All abilities at 10

Ancestry (Human): +2 STR, +2 free (chose CON)
Background (Warrior): +2 STR, +2 free (chose CON)
Class (Fighter): +2 STR
Free Boosts (4): +2 DEX, +2 INT, +2 WIS, +2 CHA

Final Scores:
STR: 10 + 2 + 2 + 2 = 16 (modifier +3)
DEX: 10 + 2 = 12 (modifier +1)
CON: 10 + 2 + 2 = 14 (modifier +2)
INT: 10 + 2 = 12 (modifier +1)
WIS: 10 + 2 = 12 (modifier +1)
CHA: 10 + 2 = 12 (modifier +1)
```

### Example 2: Ancient Red Dragon (Monster)
```
Fixed Ability Scores:
STR: 28 (modifier +9)
DEX: 18 (modifier +4)
CON: 24 (modifier +7)
INT: 16 (modifier +3)
WIS: 20 (modifier +5)
CHA: 21 (modifier +5)
```

### Example 3: Ability with Temporary Modifications
```
Base STR: 18 (modifier +4)
+ Bull's Strength spell: +2 status bonus
+ Rage feature: +2 status bonus (doesn't stack, use highest)
+ Magic Weapon: +1 item bonus (to attacks, not ability)
- Enfeebled 1 condition: -1 status penalty

Effective STR: 18 + 2 - 1 = 19 (modifier +4, would be +5 at 20)
```

## Validation Rules

The system enforces:

1. ✅ Ability scores are integers ≥ 1
2. ✅ Modifiers are correctly calculated from scores
3. ✅ PC scores at creation don't exceed 18 (except via ancestry)
4. ✅ Only one apex item per character
5. ✅ Temporary modifiers have sources and durations
6. ✅ Stacking rules are enforced
7. ✅ Dependent calculations update when abilities change

## Related Documents

- [Combat Statistics](combat-statistics.md) - How abilities affect combat
- [Skills and Proficiencies](skills-and-proficiencies.md) - Ability modifiers in skills
- [Spells and Magic](spells-and-magic.md) - Spellcasting abilities
- [Conditions and Effects](conditions-and-effects.md) - Temporary ability modifications
