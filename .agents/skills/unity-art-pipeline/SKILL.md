---
name: unity-art-pipeline
description: Plan, generate, import, review, or wire art/audio assets for this Unity tactics project, including prompts, asset briefs, import settings, prefab specs, and Git LFS expectations.
---

# Unity Art Pipeline

Use this skill for art briefs, generated bitmap concepts, import checklists, prefab specs, and asset review.

## Workflow

1. Define the asset purpose, dimensions, style constraints, camera/use context, and integration target.
2. Keep generated concepts separate from final imported Unity assets until reviewed.
3. Use Unity MCP when connected for import settings, material setup, prefab wiring, thumbnails, scene previews, and console validation. Use manual Unity review when MCP cannot expose the needed visual or importer detail.
4. Track binary and large assets with Git LFS according to `.gitattributes`.
5. Verify in context: scene lighting, scale, collider bounds, UI readability, audio levels, or animation preview as relevant.
6. For work intended for a PR, use `iterative-pr-delivery` and carry the required visual evidence through every review round.

## Guardrails

- Do not import copyrighted art, protected Pathfinder setting material, or unclear-license assets without approval.
- Preserve `.meta` files for assets that are intentionally added.
- Avoid raw serialized prefab/material edits unless narrowly scoped and reviewed.
- Keep MCP asset generation and package tools approval-gated, and verify any generated/imported asset diff before committing it.
