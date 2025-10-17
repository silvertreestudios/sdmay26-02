# Conditions and Effects

## Overview

Conditions are persistent effects that modify a creature's capabilities, while effects are temporary changes to the game state. This document details how conditions work, how they're tracked, and how they interact with other game mechanics.

## Condition System

### Core Principles

1. **Conditions Stack with Themselves**: Multiple instances of the same condition increase its severity
2. **Conditions are Tracked Individually**: Each condition has its own source, duration, and value
3. **Removal**: Conditions can be removed through various means (time, healing, magic, etc.)
4. **Interaction**: Some conditions prevent or modify others

## Standard Conditions

Pathfinder 2e defines numerous standard conditions that can affect creatures.

### Value-Based Conditions

These conditions have numeric values indicating severity:

#### Clumsy
- **Effect**: Status penalty to Dexterity-based checks equal to value
- **Example**: Clumsy 2 = -2 status penalty to DEX

#### Drained
- **Effect**: Status penalty to Constitution-based checks equal to value, lose HP equal to level × value
- **Max HP Reduction**: Reduces maximum HP
- **Example**: Drained 2 at level 5 = -2 CON, -10 max HP

#### Enfeebled
- **Effect**: Status penalty to Strength-based checks equal to value
- **Example**: Enfeebled 1 = -1 status penalty to STR

#### Frightened
- **Effect**: Status penalty to all checks and DCs equal to value, decreases by 1 at end of turn
- **Auto-Decreasing**: Reduces by 1 each turn
- **Example**: Frightened 3 = -3 to all checks, becomes Frightened 2 next turn

#### Sickened
- **Effect**: Status penalty to all checks and DCs equal to value
- **Removal**: Can spend action to retch (Fortitude save to reduce)
- **Example**: Sickened 2 = -2 to all checks

#### Slowed
- **Effect**: Lose actions at start of turn equal to value
- **Example**: Slowed 2 = lose 2 actions (only have 1 action)

#### Stupefied
- **Effect**: Status penalty to Intelligence, Wisdom, and Charisma-based checks equal to value
- **Example**: Stupefied 1 = -1 to INT, WIS, CHA

#### Wounded
- **Effect**: Increases Dying value when knocked out
- **Gained When**: Stabilize from dying
- **Example**: Wounded 2 means you start at Dying 3 if knocked out again

#### Dying
- **Effect**: Unconscious and near death
- **Death at**: Dying 4
- **Recovery Check**: DC 10 + Dying value at start of turn
- **Example**: Dying 2 requires DC 12 recovery check

### Boolean Conditions

These conditions are either present or absent (no numeric value):

#### Blinded
- **Effect**: Can't see, automatically critically fail Perception checks requiring sight
- **Attacks**: Flat-footed to attacker, target is concealed to you

#### Confused
- **Effect**: Can't use actions with concentrate trait, attack nearest creature each turn

#### Controlled
- **Effect**: Another creature controls your actions

#### Dazzled
- **Effect**: Everything is concealed to you

#### Deafened
- **Effect**: Can't hear, automatically critically fail Perception checks requiring hearing
- **Initiative**: -2 circumstance penalty to initiative

#### Doomed
- **Effect**: Increases dying threshold (Death at Dying 4 minus Doomed value)
- **Example**: Doomed 2 means death at Dying 2

#### Fatigued
- **Effect**: -1 AC and saves, can't explore or recover while exploring

#### Fascinated
- **Effect**: -2 status penalty to Perception and skill checks, can't use actions with concentrate trait unless related to subject

#### Flat-Footed
- **Effect**: -2 circumstance penalty to AC
- **Common Causes**: Flanked, surprised, certain conditions

#### Fleeing
- **Effect**: Must spend actions to escape, can't attack or delay

#### Grabbed
- **Effect**: Immobilized, flat-footed, can't move (can attempt Escape)

#### Immobilized
- **Effect**: Can't use actions with move trait

#### Invisible
- **Effect**: Undetected to creatures relying on sight, not hidden to creatures with other precise senses

#### Paralyzed
- **Effect**: Can't act, flat-footed, automatically fail Strength and Dexterity saves

#### Persistent Damage
- **Effect**: Take damage at end of turn, roll flat DC 15 to end
- **Types**: Fire, bleed, acid, etc.

#### Petrified
- **Effect**: Turned to stone, can't act or sense
- **Protection**: Immune to damage but object is vulnerable

#### Prone
- **Effect**: -2 circumstance penalty to attack rolls, takes action to stand, melee attacks against you get +2, ranged get -2

#### Quickened
- **Effect**: Gain an extra action (often with restrictions on how to use it)

#### Restrained
- **Effect**: Immobilized, flat-footed, can't use actions requiring hands (can attempt Escape)

#### Stunned
- **Effect**: Can't act, has value indicating how many actions lost
- **Auto-Decreasing**: Reduces automatically

#### Unconscious
- **Effect**: Can't act, prone, flat-footed, drop held items
- **Awareness**: Unaware of surroundings

## Data Structure

### Condition Object

```typescript
interface ActiveCondition {
  // Identity
  id: string;
  name: string;
  
  // Value (if applicable)
  value: number | null;
  maxValue?: number;
  
  // Source and Duration
  source: {
    type: "spell" | "ability" | "item" | "environment" | "disease" | "poison";
    name: string;
    creature?: string;  // Who applied it
  };
  
  duration: Duration;
  turnsRemaining?: number;
  
  // Effects
  effects: ConditionEffect[];
  
  // Removal
  saveToRemove?: {
    save: "fortitude" | "reflex" | "will";
    DC: number;
    frequency: "start of turn" | "end of turn" | "once per day";
  };
  
  // Interaction
  overrides: string[];     // Conditions this replaces
  blockedBy: string[];     // Conditions that prevent this
  
  // Metadata
  applied: number;         // Turn applied
  notes: string;
}

interface ConditionEffect {
  type: "penalty" | "bonus" | "prevent" | "modify";
  target: string;          // What's affected (e.g., "ac", "all-checks", "actions")
  value: number;
  
  // Conditional application
  applyWhen?: string;
}
```

### Condition Definition

```typescript
interface ConditionDefinition {
  id: string;
  name: string;
  description: string;
  
  // Properties
  hasValue: boolean;
  maxValue?: number;
  autoDecreases: boolean;  // Like Frightened
  
  // Default Effects
  effects: ConditionEffect[];
  
  // Rules
  rules: {
    stacks: boolean;
    group?: string;        // Conditions in same group don't stack
    overrides: string[];
    overriddenBy: string[];
  };
  
  // Removal
  defaultDuration: Duration | null;
  removalMethods: string[];
}
```

## Condition Interactions

### Stacking Rules

**Same Condition:**
- Most conditions stack with themselves (values add)
- Example: Frightened 2 + Frightened 1 = Frightened 3

**Different Conditions:**
- Different conditions all apply
- Penalties of different types stack
- Example: Frightened 2 + Clumsy 1 both apply

**Penalties of Same Type:**
- Only highest applies
- Status penalties don't stack
- Circumstance penalties don't stack
- Item penalties don't stack
- Untyped penalties stack

### Overriding Conditions

Some conditions override others:

**Restrained → Grabbed**
- Restrained includes all effects of Grabbed
- Remove Grabbed when Restrained is applied

**Unconscious → Prone**
- Unconscious automatically makes you Prone
- Don't track separately

**Invisible → Hidden**
- Invisible includes benefits of Hidden
- Against creatures relying on sight

## Managing Persistent Damage

Persistent damage is a special condition type.

### Persistent Damage Types

```typescript
interface PersistentDamage extends ActiveCondition {
  name: "persistent-damage";
  damageType: DamageType;
  value: number;  // Amount of damage
  
  // Flat check to end
  flatCheckDC: number;  // Usually 15
  
  // Recovery modifiers
  modifiers: {
    type: string;  // "assisted", "favorable conditions", etc.
    dcModifier: number;
  }[];
}
```

### Persistent Damage Rules

1. **Application**: Take damage at end of your turn
2. **Recovery**: Immediately after taking damage, roll DC 15 flat check
   - **Success**: Condition ends
   - **Failure**: Persists to next turn
3. **Assistance**: Nearby allies can help (lowers DC by 5)
4. **Water**: Immersion in water ends fire damage
5. **Immunities**: If you're immune to the damage type, persistent damage doesn't apply

### Multiple Persistent Damage

- Different types all apply
- Same type: only highest applies
- Example: 2d6 persistent fire + 1d6 persistent fire = 2d6 persistent fire

## Death and Dying

Special conditions related to dying:

### Dying Condition

```typescript
interface DyingCondition extends ActiveCondition {
  name: "dying";
  value: number;  // 1-4 (4 = death)
  
  // Death threshold
  deathAt: number;  // 4 minus Doomed value
  
  // Wounded tracking
  wounded: number;  // Increases when stabilize
  
  // Recovery
  recoveryCheck: {
    DC: number;  // 10 + Dying value
    result?: "critical-success" | "success" | "failure" | "critical-failure";
  };
}
```

### Recovery Check Results

**At Start of Turn (if Dying):**
- **Critical Success**: Reduce Dying by 2 (if reduced to 0, gain Wounded and stabilize)
- **Success**: Reduce Dying by 1
- **Failure**: Increase Dying by 1
- **Critical Failure**: Increase Dying by 2

### Wounded and Doomed

**Wounded:**
- Gained when stabilize from Dying
- Value equals your Dying value when you stabilized
- When knocked out again: Dying starts at 1 + Wounded value

**Doomed:**
- Reduces death threshold
- Death at: (4 - Doomed)
- Example: Doomed 2 means death at Dying 2
- Difficult to remove, usually permanent until long rest

## Effect Management

### Temporary Effects

Effects that modify statistics temporarily:

```typescript
interface TemporaryEffect {
  id: string;
  name: string;
  source: string;
  
  // Modifications
  modifiers: Modifier[];
  
  // Duration
  duration: Duration;
  turnsRemaining?: number;
  sustained: boolean;
  sustainedBy?: string;
  
  // Application
  applied: number;  // Turn applied
  
  // Stacking
  stacksWith: string[];
  overrides: string[];
}
```

### Buff vs Debuff

**Buffs (Beneficial):**
- Bonuses to statistics
- Additional abilities
- Improved capabilities
- Usually from allies

**Debuffs (Detrimental):**
- Penalties to statistics
- Restricted actions
- Reduced capabilities
- Usually from enemies

## Condition Removal

### Methods of Removal

1. **Time**: Duration expires
2. **Save**: Make successful saving throw
3. **Action**: Spend action to attempt removal (e.g., Retch for Sickened)
4. **Magic**: Spell or ability removes condition
5. **Rest**: Some conditions end on rest
6. **Circumstance**: Environmental change (e.g., water ends fire)

### Removal Actions

**Common Removal Actions:**

**Escape** ◆
- **DC**: Grab/restrain DC or creature's Fort DC
- **Success**: No longer grabbed/restrained
- **Critical Success**: Can move 5 feet

**Retch** ◆
- **Removes**: Sickened condition
- **Check**: Fortitude save vs DC
- **Success**: Reduce sickened by 1
- **Critical Success**: Reduce sickened by 2

**Stand** ◆
- **Removes**: Prone condition

## Condition Priority

When multiple conditions affect the same thing:

1. **Immunities**: Apply first (completely negate)
2. **Condition Prevention**: Some effects prevent conditions
3. **Overriding**: Higher-level conditions override lower
4. **Stacking**: Value-based conditions stack
5. **Penalties**: Use highest of each type

## Complete Condition Tracking

```typescript
interface ConditionTracker {
  // Active Conditions
  conditions: ActiveCondition[];
  
  // Quick Access
  dyingValue: number;
  woundedValue: number;
  doomedValue: number;
  
  // Persistent Damage
  persistentDamage: PersistentDamage[];
  
  // Helper Methods
  hasCondition(name: string): boolean;
  getCondition(name: string): ActiveCondition | null;
  getConditionValue(name: string): number;
  addCondition(condition: ActiveCondition): void;
  removeCondition(id: string): void;
  updateCondition(id: string, changes: Partial<ActiveCondition>): void;
  
  // Turn Management
  processStartOfTurn(): void;  // Auto-decrease, dying checks
  processEndOfTurn(): void;    // Persistent damage, duration
  
  // Calculate Effects
  getNetModifiers(target: string): number;
  isImmobilized(): boolean;
  canAct(): boolean;
  getActionReduction(): number;
}
```

## Examples

### Example 1: Fighter Gets Hit

```typescript
// Fighter takes critical hit from poison dagger
fighter.addCondition({
  name: "enfeebled",
  value: 1,
  source: { type: "ability", name: "Poison Dagger" },
  duration: { type: "rounds", value: 3 },
  saveToRemove: {
    save: "fortitude",
    DC: 20,
    frequency: "end of turn"
  }
});

// Effect: -1 to STR-based checks for 3 rounds
// Can save at end of turn to remove
```

### Example 2: Wizard Casts Spell

```typescript
// Wizard casts Slow on enemy
enemy.addCondition({
  name: "slowed",
  value: 1,
  source: { type: "spell", name: "Slow", creature: "wizard-pc" },
  duration: { type: "rounds", value: 10, sustained: true },
  saveToRemove: {
    save: "will",
    DC: 25,
    frequency: "end of turn"
  }
});

// Effect: Enemy loses 1 action per turn
// Can save at end of turn to remove
// Spell must be sustained or ends
```

### Example 3: Rogue Sneak Attack

```typescript
// Rogue flanks enemy, makes them flat-footed
enemy.addCondition({
  name: "flat-footed",
  value: null,
  source: { type: "ability", name: "Flanked" },
  duration: { type: "until trigger", endCondition: "no longer flanked" }
});

// Effect: -2 circumstance penalty to AC
// Lasts until positioning changes
```

### Example 4: Healing Dying Character

```typescript
// Character at Dying 2 gets healed
character.removeCondition("dying");
character.addCondition({
  name: "wounded",
  value: 2,  // Equal to dying value
  source: { type: "ability", name: "Stabilized" },
  duration: { type: "until trigger", endCondition: "10-minute rest" }
});

// If knocked out again, starts at Dying 3 (1 + Wounded 2)
```

## Condition Immunity

Some creatures are immune to conditions:

```typescript
interface CreatureImmunities {
  conditions: string[];  // Condition names
  damageTypes: DamageType[];
  
  // Conditional immunities
  conditionalImmunities: {
    condition: string;
    when: string;
  }[];
}

// Example: Undead
{
  conditions: ["paralyzed", "poison", "disease", "death"],
  damageTypes: ["poison", "death", "negative"]
}
```

## Condition Icons and Display

For UI purposes, conditions should have:

```typescript
interface ConditionDisplay {
  name: string;
  icon: string;  // Path to icon
  color: string;  // For value display
  
  shortDescription: string;
  
  // Badge display
  showValue: boolean;
  valuePrefix?: string;  // "-" for penalties, "+" for bonuses
  
  // Grouping
  category: "detrimental" | "beneficial" | "neutral";
  priority: number;  // Display order
}
```

## Related Documents

- [Combat Statistics](combat-statistics.md) - How conditions affect combat
- [Actions and Abilities](actions-and-abilities.md) - Abilities that cause conditions
- [Spells and Magic](spells-and-magic.md) - Spells that apply conditions
- [Core Attributes](core-attributes.md) - Ability score conditions
