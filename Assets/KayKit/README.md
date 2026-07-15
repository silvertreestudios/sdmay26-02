# KayKit asset setup

This directory contains project-owned materials, wrapper prefabs, catalogs, and
Editor tooling for the immutable vendor payload in Assets/ThirdParty/KayKit.
Do not customize files inside the vendor directories.

## Sources and license

The four free-tier archives were downloaded from the official Kay Lousberg
itch.io pages on 2026-07-14. Each archive includes its original license file and
is released under Creative Commons Zero 1.0 (CC0-1.0).

| Pack | Version | FBX | PNG | Download SHA-256 | Official source |
| --- | --- | ---: | ---: | --- | --- |
| Dungeon Remastered | Free 1.1 | 211 | 1 | `53A38667E062217334807A35C8F916978DEA64224CB66D3799134D9FF7B0E365` | <https://kaylousberg.itch.io/kaykit-dungeon-remastered> |
| Adventurers | Free 2.0 | 37 | 5 | `ABE48F4763FBA0896BAB486EE9E6D08CA6B5B3884B9601F235C8847AE94DC479` | <https://kaylousberg.itch.io/kaykit-adventurers> |
| Skeletons | Free 1.1 | 17 | 1 | `21FBAD59EE6CC1D7BED12D0E425ACAB8EBE564B8620BBC1D017AEDB29DD8A3D2` | <https://kaylousberg.itch.io/kaykit-skeletons> |
| Character Animations | Free 1.1 | 8 | 0 | `65882F31F905AD2E953819648A59287CDEAB8F623908D5EF701971D3758BE20F` | <https://kaylousberg.itch.io/kaykit-character-animations> |

The curated payload contains 273 FBXs, seven PNG atlases, and one retained
`License.txt` from each pack. Dungeon models come from `Assets/fbx(unity)`;
character accessories come from each pack's Unity FBX folder; character models
come from each pack's character FBX folder; and animation sources are the eight
`Rig_Medium` FBXs from the Character Animations pack.

Only FBX models, PNG atlases, and the license documents are retained. The
Dungeon pack uses its Unity-oriented FBX export. GLTF, GLB, OBJ, BIN, Blend,
archive, marketing, sample-scene, URL-shortcut, and paid-tier files are
excluded. The General and MovementBasic FBXs bundled with character packs are
also excluded because their authoritative copies come from Character
Animations.

## Import policy

KayKitAssetPostprocessor applies only below Assets/ThirdParty/KayKit:

- models use scale factor 1, no generated colliders, no imported materials,
  cameras, lights, or Read/Write;
- characters and the eight Rig_Medium animation sources use generated
  Humanoid avatars;
- animation sources bake root transform motion into their clips;
- environment and accessory FBXs disable animation import;
- textures use sRGB, mipmaps, a maximum size of 1024, and no Read/Write.

The project uses the Built-in Render Pipeline. Tools > KayKit > Regenerate
Project Assets creates Standard atlas materials, deterministic catalogs, the
source manifest, and six representative wrapper prefabs. Generation stops on
duplicate IDs, missing references, missing animation sets, or ambiguous clip
loop semantics. The T-pose remains embedded for retargeting but is excluded
from playable catalog entries.

Use Tools > KayKit > Reimport Vendor Tree for a forced path-scoped reimport,
then regenerate and run Tools > KayKit > Validate Setup. Repeating those steps
must not create materials, duplicate entries, or unrelated serialized changes.

## Animated creature presentation

Tools > KayKit > Regenerate Animated Creatures creates the shared Animator
Controller, `CreatureVisualCatalog`, `EquipmentVisualCatalog`, and the eight
project-owned wrappers under `Assets/KayKit/Prefabs/Animated`. It also wires
the existing creature and character-preview prefabs to the optional animated
path. Lena, Torgrim, both undead types, and the five character-creation classes
resolve through the catalog. Goblins, kobolds, and unknown keys continue to use
their existing static token meshes.

Each wrapper keeps root motion disabled, uses a generated Humanoid avatar and a
project atlas material, and owns its animation/equipment components. Hand props
resolve through Humanoid hand bones. Back and quiver sockets are project-owned
children of the Humanoid torso. Two-handed props use the right-hand socket and
the authored two-handed pose; no procedural IK is used.

Animated wrappers use a shared 0.75 presentation scale. Their legacy token mesh
and base plate are hidden at runtime, while unmapped legacy creatures keep both.
Animated Stride movement stays level and uses the walk clip instead of the old
token hop. Strikes rotate the creature root toward the selected target before
the attack clip begins. Animated models and equipment inherit the creature
rendering layer so the existing initiative portrait cameras include them.

The character-creation `VisualRoot` applies a preview-only scale so complete
models and their longest default props remain inside the existing portrait
camera framing. Combat instances retain their authored wrapper scale.

`CreatureAnimationController.PlayClip` accepts every stable ID in
`KayKitAnimationLibrary`. Looping clips continue until `StopAction`; one-shots
return to locomotion after their catalog duration. The generator validates all
default locomotion, action, hit, and death IDs against the pinned imported
library and fails with the missing IDs when the package or catalog is stale.

Equipment resolution prefers an exact species/item mapping, then an exact
generic item mapping, then species and generic weapon-style fallbacks. Unknown
items and animation IDs degrade safely and emit one development warning per ID.
Armor remains gameplay-only because each KayKit character has a fixed outfit.

Runtime movement, strikes, spells, damage, and defeat only notify the optional
root `CreaturePresentation` component. They never wait for animation events.
Defeat removes combat/grid interaction and disables colliders immediately,
then allows the visual-only death clip to finish for at most five seconds.

## Showcase scene

Open `Assets/KayKit/Scenes/KayKitShowcase.unity` and press Play to inspect the
representative adventurer, skeleton, ranger bow, dungeon floor, wall, and prop.
The on-screen panel previews every clip in `KayKitAnimationLibrary` on both
Humanoid rigs. It supports category and text filtering, previous/next selection,
play/pause, restart, timeline scrubbing, playback speed, visibility toggles, and
camera orbit/zoom controls. The scene is intentionally excluded from player
build settings and is a project-only art and animation review tool.

## Git LFS

Issue #107 is the scoped-new-assets exception to the repository-wide LFS
migration rejected by #60. Active .gitattributes rules match only FBX and PNG
files under Assets/ThirdParty/KayKit/**. Unity metadata, project-owned
materials, prefabs, catalogs, controllers, JSON, Markdown, and existing binary
assets remain ordinary Git content.

Before review:

1. Run git check-attr -a on representative KayKit and existing assets.
2. Run git lfs status and git lfs ls-files.
3. Inspect staged KayKit FBX and PNG blobs for the LFS pointer header.
4. Verify a fresh LFS-enabled checkout is clean and can retrieve every object.
5. Run the targeted KayKit EditMode tests and the full EditMode suite with
   Unity 6000.2.1f1, without -quit.
