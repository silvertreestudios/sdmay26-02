# Spell and Strike VFX gallery

`VfxGallery` is an in-Unity review scene for the production spell, Strike, and auxiliary combat
effects. Open it from the Main Menu's **VFX Gallery** button or load
`Assets/Scenes/VfxGallery.unity` in Unity 6000.2.1f1.

## Reviewing an effect

Use the text field to filter by stable feedback ID, display name, or category. Select an entry and
choose **Play Selected**. **Replay**, **Previous**, **Next**, and **Play All** retain the same
deterministic committed fixture. **Pause** and **Slow Motion** affect the whole authored timeline;
**Close View** moves the normal Game camera nearer to contact while **Wide View** restores the full
combat framing. **Copy ID** copies the exact manifest ID and shows confirmation in the scene.

The manifest currently contains exactly 69 reviewable entries, including all twelve undead Heal
action-count/save-degree combinations. Each entry displays its stable ID, variant, committed outcome,
production reachability, gameplay trigger, declared cue, actual target count, and the production cues
emitted by the latest timeline.
Longsword is intentionally fixture-only. The manifest at
`Assets/Resources/Vfx/vfx-coverage-manifest.json` is the source of truth for supported entries and
explicit exclusions.

The gallery does not mutate a live encounter. It builds immutable deterministic outcomes and Facts,
then sends them through the same `UnitySpellActionPresenter`, `UnityStrikeActionPresenter`, Light,
Rage, spell-effect, and Rotting Aura observers used by production encounters. Prefabs are loaded from
the same Resources catalog. Reset stops gallery-owned selected and Play All routines, disposes active
effect observers, and removes every playback-owned object.

## Regenerating assets

Use **Tools > VFX > Regenerate Production Gallery**. The editor generator creates the materials,
prefabs, texture, scene, and build-settings entry through Unity asset APIs. Review serialized changes
after regeneration. Do not hand-edit the generated Unity YAML.

## Performance verification

The final Windows/D3D12 verification used Unity 6000.2.1f1 in batch mode. Thirty idle gallery frames
took 6.95 ms total, while ten accelerated production-fixture replays took 23.44 ms total. Unity's
coarse total-allocated-memory counter increased by 3,797,379 bytes across those replays, including
fixture construction, Resources loading, prefab instantiation, and test-runner allocations. Peak
resources were two overlapping VFX roots, two particle systems, 22 renderers (the Rotting Aura
boundary), one point light, and one distinct shared material; every replay reset returned to zero VFX
roots. A separate
20-cycle replacement stress measured 13.18 ms, a 1,117,836-byte coarse allocation delta, three peak
roots, and zero final roots.

The visible production encounter capture also measured cold action-to-terminal-presentation time:
Mace critical Strike 1,250.02 ms with a 15,628,201-byte coarse allocation delta, followed by Divine
Lance critical at 3,177.70 ms and 2,285,261 bytes. These timings deliberately include authored
timeline duration; the first measurement also includes cold Resources and encounter setup costs.
They are regression observations for this machine, not frame-time or universal FPS claims.

Materials are shared prefab assets rather than instantiated in `Update`. The per-frame motion path
uses value-type interpolation and does not construct managed collections. Allocations are expected at
the bounded spawn/despawn and deterministic-fixture boundaries. Point lights exist only on Light
instances and are destroyed with their exact effect owner.

The inspected 1920x1080 Game View evidence shows effects confined to a target, weapon path, or bounded
area rather than full-screen transparent layers. The largest case is Rotting Aura's 12 separated
boundary markers plus its central ring; the automated peak of 22 renderers bounds that geometry.
For later content changes, repeat the Unity Profiler and Frame Debugger inspection at shipping Game
resolution during idle, windup, peak impact, and reset, and reject stacked full-screen overdraw.
