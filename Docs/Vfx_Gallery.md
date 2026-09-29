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

The manifest currently contains exactly 80 reviewable entries with unique stable IDs. Those entries
reference 69 distinct production cues, each backed by its matching Resources prefab, and include all
twelve undead Heal action-count/save-degree combinations. Each entry displays its stable ID, variant,
committed outcome, production reachability, gameplay trigger, declared cue, actual target count, and
the production cues emitted by the latest timeline.
Longsword is intentionally fixture-only. The manifest at
`Assets/Resources/Vfx/vfx-coverage-manifest.json` is the source of truth for supported entries and
explicit exclusions.

The gallery does not mutate a live encounter. It builds immutable deterministic outcomes and Facts,
then sends them through the same `UnitySpellActionPresenter`, `UnityStrikeActionPresenter`, Light,
Rage, spell-effect, and Rotting Aura observers used by production encounters. Prefabs are loaded from
the same Resources catalog. Reset stops gallery-owned selected and Play All routines, disposes active
effect observers, removes every playback-owned object, and restores every fixture actor to its active
10/10-health baseline before reset, replay, or navigation stages another entry. Spell damage and
healing fixtures submit their deterministic committed amounts through the production health
projection observer, including hit reactions and terminal defeat.

## Regenerating assets

Use **Tools > VFX > Regenerate Production Gallery**. The editor generator creates the materials,
prefabs, texture, scene, and build-settings entry through Unity asset APIs. Review serialized changes
after regeneration. Do not hand-edit the generated Unity YAML.

## Performance verification

The latest retained Windows/D3D12 verification used Unity 6000.2.1f1 in batch mode. Thirty idle
gallery frames took 7.29 ms total, while ten accelerated replays across five representative
production fixtures took 30.39 ms total. Unity's coarse total-allocated-memory counter increased by
5,026,132 bytes across those replays. Observed peaks were three overlapping VFX roots, three particle
systems, 27 renderers, one point light, and two distinct shared materials; every replay reset returned
to zero VFX roots. A separate 20-cycle persistent-replacement stress run took 13.38 ms after a
2.84 ms 30-frame idle sample, recorded a 1,149,132-byte coarse allocation delta, reached three VFX
roots, and returned to zero.

These are local Unity Test Framework regression observations, not shipping-build benchmarks. The
production-fixture test shortens review holds to 0.02 seconds, sets `Time.captureDeltaTime` to 0.1,
and samples five representative entries rather than all 80. Stopwatch totals include coroutine and
test-runner work and are not per-frame CPU/GPU timings or FPS claims. The process-wide allocated-memory
snapshots include fixture construction, Resources loading, prefab instantiation, test-runner work,
and unrelated runtime noise; they are not managed-allocation profiles. Object and renderer counts are
once-per-frame samples, so they document this run rather than a universal maximum.

Materials are shared prefab assets rather than instantiated in `Update`. The per-frame motion path
uses value-type interpolation and does not construct managed collections. Allocations are expected at
the bounded spawn/despawn and deterministic-fixture boundaries. Point lights exist only on Light
instances. The point light and paired persistent orb are removed when their exact owner is disabled
or destroyed and do not reappear if that owner is later reused; a new committed restoration Fact is
required to recreate them.

The inspected 1920x1080 Game View evidence shows effects confined to a target, weapon path, or bounded
area rather than full-screen transparent layers. The representative automated run observed a peak of
27 renderers; it does not establish a bound for every gallery entry or future content. For later
content changes, repeat the Unity Profiler and Frame Debugger inspection at shipping Game resolution
during idle, windup, peak impact, and reset, and reject stacked full-screen overdraw.
