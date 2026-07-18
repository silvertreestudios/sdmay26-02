# PF2e encounter-building provenance

The deterministic dungeon encounter planner uses the Pathfinder Second Edition remastered
encounter budgets and creature XP table from *GM Core*, pages 75–76. The implementation keeps
only the compact numeric facts needed for calculation:

- Trivial, Low, and Moderate baselines are 40, 60, and 80 XP.
- Their per-character adjustments from a four-character party are 10, 20, and 20 XP.
- Creature XP from party level −4 through +4 is 10, 15, 20, 30, 40, 60, 80, 120, and 160.

Rules reference: [Archives of Nethys — Encounter Design](https://2e.aonprd.com/Rules.aspx?ID=2716&Redirected=1)
and [Choosing Creatures](https://2e.aonprd.com/Rules.aspx?ID=499).

The project distributes adapted ORC-licensed rules mechanics under the notices in
[`ORCLicense.md`](../../ORCLicense.md). The enemy catalog points only to existing project-owned
Monster Core conversions whose JSON files retain their own source and ORC provenance. No
descriptive rules prose or setting material is copied into the planner.

The Low-threat adjustment is intentionally 20 XP from the remastered *GM Core* table linked
above. This differs from the 15-XP value in the legacy pre-remaster *Core Rulebook* table.
