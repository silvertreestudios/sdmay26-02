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
