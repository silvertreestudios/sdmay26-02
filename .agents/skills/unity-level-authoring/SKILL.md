---
name: unity-level-authoring
description: Author or modify Unity scenes, levels, maps, grid layouts, prefabs, lighting, cameras, and playtest validation for this tactics project.
---

# Unity Level Authoring

Use this skill for scene, prefab, grid map, level layout, lighting, camera, and playtest work.

## Safe Workflow

1. Read `AGENTS.md` and inspect existing level assets under `Assets/Scenes`, `Assets/Prefabs`, `Assets/Textures`, and map/grid scripts.
2. Prefer bitmap layout plus generated scene validation when using the existing map-generation path.
3. Use Unity Editor automation, Unity MCP, or a batchmode editor script for scene and prefab changes.
4. Avoid raw YAML edits to `.unity`, `.prefab`, `.asset`, `.mat`, and `.meta` files unless the change is narrow and fully understood.
5. Verify level changes with PlayMode tests, an Editor screenshot, or a documented Editor validation pass.

## Review Checklist

- Grid cells, movement costs, obstacles, doors, and line of sight match the intended tactics layout.
- Spawn points and camera framing work in the target scene.
- Prefab references are intact and no unrelated serialized churn appears in diffs.
- Generated files and recovery scenes are not committed.
