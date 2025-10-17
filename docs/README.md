# Pathfinder 2e Combat System Documentation

This directory contains comprehensive design documentation for the creature representation system used in the Pathfinder 2e combat tactics game.

## Documentation Structure

### Core Documents
- **[creature-representation-overview.md](creature-representation-overview.md)** - High-level overview of the creature system architecture
- **[core-attributes.md](core-attributes.md)** - Detailed specification of creature attributes and ability scores
- **[combat-statistics.md](combat-statistics.md)** - Combat-related attributes including AC, HP, saves, and speeds
- **[actions-and-abilities.md](actions-and-abilities.md)** - Action economy, abilities, and special actions
- **[skills-and-proficiencies.md](skills-and-proficiencies.md)** - Skills, proficiencies, and training levels
- **[equipment-and-items.md](equipment-and-items.md)** - Weapons, armor, and item management
- **[classes-and-progression.md](classes-and-progression.md)** - Character classes and level progression
- **[feats-and-features.md](feats-and-features.md)** - Feats, class features, and ancestry features
- **[spells-and-magic.md](spells-and-magic.md)** - Spellcasting system and magical abilities
- **[conditions-and-effects.md](conditions-and-effects.md)** - Status conditions and ongoing effects
- **[extensibility-guide.md](extensibility-guide.md)** - Guidelines for extending the system with new content

### Supporting Documents
- **[data-structures.md](data-structures.md)** - Technical data structure specifications
- **[relationships-and-dependencies.md](relationships-and-dependencies.md)** - Component relationships and dependencies
- **[examples.md](examples.md)** - Concrete examples of creature representations

### Additional Documents (Planned)
- **classes-and-progression.md** - Class system and level advancement
- **feats-and-features.md** - Feat system and class features
- **equipment-and-items.md** - Equipment, weapons, armor, and magical items
- **spells-and-magic.md** - Spellcasting system implementation

## Design Principles

The creature representation system is designed with the following principles:

1. **Modularity** - Each component (abilities, actions, items) is self-contained and composable
2. **Extensibility** - New content can be added without modifying existing structures
3. **Data-Driven** - Game rules and content are data, not code
4. **Type Safety** - Clear typing and validation for all game data
5. **PF2e Compliance** - Faithful implementation of Pathfinder 2e rules
6. **Performance** - Efficient for real-time combat calculations

## Quick Start

For a quick understanding of the system:
1. Start with [creature-representation-overview.md](creature-representation-overview.md)
2. Review [core-attributes.md](core-attributes.md) and [combat-statistics.md](combat-statistics.md)
3. Examine [examples.md](examples.md) for concrete implementations
4. Consult specific documents as needed for detailed specifications

## Reference Implementation

This design is validated against the [FoundryVTT Pathfinder 2e](https://github.com/foundryvtt/pf2e) implementation, which provides comprehensive JSON data for nearly all Pathfinder 2e content.
