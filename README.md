# sdmay26-02

Unity project for a turn-based Pathfinder-style tactics game.

## Gameplay Modes

- **Exploration** uses one-click destination travel. The selected party leader follows the previewed
  route while the rest of the living party follows, stopping for cancellation, blockage, or an
  activated encounter. Clicking a reachable closed door or stair automatically routes the party to
  its nearest accessible side before interacting; closed intermediate doors remain blockers and are
  never opened automatically.
- **Tactics** uses initiative, three-action turns, multiple-attack penalty, effects, and the normal
  action bar. It starts automatically when enemies engage, or can be entered manually with the HUD
  control. Returning to Exploration is available only when no living enrolled opposition or action
  resolution remains.

## Rules Documentation

- [Rules runtime design](Docs/Rules_Runtime_Design.md)
- [Encounter rules runtime implementation guide](Docs/Encounter_Rules_Architecture.md)

## Development

- [C# formatting and pre-commit setup](Docs/CSharp_Formatting.md)
- [Unity test workflow](TESTING.md)
