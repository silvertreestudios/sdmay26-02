---
name: unity-mcp-orchestrator
description: Orchestrate Unity Editor via MCP (Model Context Protocol) tools and resources. Use when working with Unity projects through MCP for Unity - creating/modifying GameObjects, editing scripts, managing scenes, running tests, or any Unity Editor automation. Provides best practices, tool schemas, and workflow patterns for effective Unity-MCP integration.
---

# Unity-MCP Operator Guide

This skill helps you effectively use the Unity Editor with MCP tools and resources.

## Template Notice

Examples in `references/workflows.md` and `references/tools-reference.md` are reusable templates. They may be inaccurate across Unity versions, package setups (UGUI/TMP/Input System), and project-specific conventions. Please check console, compilation errors, or use screenshot after implementation.

Before applying a template:
- Validate targets/components first via resources and `find_gameobjects`.
- Treat names, enum values, and property payloads as placeholders to adapt.

## Quick Start: Resource-First Workflow

**Always read relevant resources before using tools.** This prevents errors and provides the necessary context.

```
1. Check editor state     → mcpforunity://editor/state
2. Understand the scene   → mcpforunity://scene/gameobject-api
3. Find what you need     → find_gameobjects or resources
4. Take action            → tools (manage_gameobject, create_script, script_apply_edits, apply_text_edits, validate_script, delete_script, get_sha, etc.)
5. Verify results         → read_console, manage_camera(action="screenshot"), resources
```

### Dynamic Tool Visibility

After activating a tool group with `manage_tools`, call `tool_search` again to refresh the available tool schemas. If a Unity-registered tool appears in `mcpforunity://custom-tools` but is not exposed as a direct callable schema, invoke it with `execute_custom_tool`.

Example for docs/reflection smoke checks:

```python
manage_tools(action="activate", group="docs")
execute_custom_tool(
    tool_name="unity_reflect",
    parameters={"action": "get_type", "class_name": "UnityEngine.GameObject"}
)
manage_tools(action="deactivate", group="docs")
```

## Critical Best Practices

### 1. After Writing/Editing Scripts: Wait for Compilation and Check Console

```python
# After create_script or script_apply_edits:
# Both tools already trigger AssetDatabase.ImportAsset + RequestScriptCompilation automatically.
# No need to call refresh_unity — just wait for compilation to finish, then check console.

# 1. Poll editor state until compilation completes
# Read mcpforunity://editor/state → wait until is_compiling == false

# 2. Check for compilation errors
read_console(types=["error"], count=10, include_stacktrace=True)
```

**Why:** Unity must compile scripts before they're usable. `create_script` and `script_apply_edits` already trigger import and compilation automatically — calling `refresh_unity` afterward is redundant.

### 2. Use `batch_execute` for Multiple Operations

```python
# 10-100x faster than sequential calls
batch_execute(
    commands=[
        {"tool": "manage_gameobject", "params": {"action": "create", "name": "Cube1", "primitive_type": "Cube"}},
        {"tool": "manage_gameobject", "params": {"action": "create", "name": "Cube2", "primitive_type": "Cube"}},
        {"tool": "manage_gameobject", "params": {"action": "create", "name": "Cube3", "primitive_type": "Cube"}}
    ],
    parallel=True  # Hint only: Unity may still execute sequentially
)
```

**Max 25 commands per batch by default (configurable in Unity MCP Tools window, max 100).** Use `fail_fast=True` for dependent operations.

**Tip:** Also use `batch_execute` for discovery — batch multiple `find_gameobjects` calls instead of calling them one at a time:
```python
batch_execute(commands=[
    {"tool": "find_gameobjects", "params": {"search_term": "Camera", "search_method": "by_component"}},
    {"tool": "find_gameobjects", "params": {"search_term": "Player", "search_method": "by_tag"}},
    {"tool": "find_gameobjects", "params": {"search_term": "GameManager", "search_method": "by_name"}}
])
```

### 3. Use Screenshots to Verify Visual Results

```python
# Basic screenshot (saves to Assets/, returns file path only)
manage_camera(action="screenshot")

# Inline screenshot (returns base64 PNG directly to the AI)
manage_camera(action="screenshot", include_image=True)

# Use a specific camera and cap resolution for smaller payloads
manage_camera(action="screenshot", camera="MainCamera", include_image=True, max_resolution=512)

# Batch surround: captures front/back/left/right/top/bird_eye around the scene
manage_camera(action="screenshot", batch="surround", max_resolution=256)

# Batch surround centered on a specific object
manage_camera(action="screenshot", batch="surround", view_target="Player", max_resolution=256)

# Positioned screenshot: place a temp camera and capture in one call
manage_camera(action="screenshot", view_target="Player", view_position=[0, 10, -10], max_resolution=512)

# Scene View screenshot: capture what the developer sees in the editor
manage_camera(action="screenshot", capture_source="scene_view", include_image=True)

# Scene View framed on a specific object
manage_camera(action="screenshot", capture_source="scene_view", view_target="Canvas", include_image=True)
```

**Best practices for AI scene understanding:**
- Use `include_image=True` when you need to *see* the scene, not just save a file.
- Use `batch="surround"` for a comprehensive overview (6 angles, one command).
- Use `view_target`/`view_position` to capture from a specific viewpoint without needing a scene camera.
- Use `capture_source="scene_view"` to see the editor viewport (gizmos, wireframes, grid).
- Keep `max_resolution` at 256–512 to balance quality vs. token cost.

```python
# Agentic camera loop: point, shoot, analyze
manage_gameobject(action="look_at", target="MainCamera", look_at_target="Player")
manage_camera(action="screenshot", camera="MainCamera", include_image=True, max_resolution=512)
# → Analyze image, decide next action

# Multi-view screenshot (6-angle contact sheet)
manage_camera(action="screenshot_multiview", max_resolution=480)

# Scene View for editor-level inspection (shows gizmos, debug overlays, etc.)
manage_camera(action="screenshot", capture_source="scene_view", view_target="Player", include_image=True)
```

### 4. Check Console After Major Changes

```python
read_console(
    action="get",
    types=["error", "warning"],  # Focus on problems
    count=10,
    format="detailed"
)
```

### 5. Always Check `editor_state` Before Complex Operations

```python
# Read mcpforunity://editor/state to check:
# - is_compiling: Wait if true
# - is_domain_reload_pending: Wait if true  
# - ready_for_tools: Only proceed if true
# - blocking_reasons: Why tools might fail
```

### 6. Avoid Scene Save/Close Modals

Unity modal dialogs block MCP and can make the editor unusable for remote agentic work. Avoid operations that trigger "Save Scene" or "Reload changed scene" prompts.

For temporary scenes, use this exact lifecycle:

```python
# 1. Create a unique temporary folder. Do not reuse temp folder names within the same Editor session.
manage_asset(action="create_folder", path="Assets/__McpTemp_<purpose>")

# 2. Create the scene directly at its final path.
manage_scene(action="create", name="TempScene", path="Assets/__McpTemp_<purpose>", template="3d_basic")

# 3. Mutate scene objects.
manage_gameobject(action="create", name="TempCube", primitive_type="Cube")

# 4. Save the active temp scene with no path argument.
# Do not use manage_scene(save, path=...) as Save As; it may save the active scene or trigger a modal.
manage_scene(action="save")

# 5. Load a real existing scene additively and set it active.
manage_scene(action="load", path="Assets/Scenes/UnitTestingScene.unity", additive=True)
manage_scene(action="set_active_scene", scene_path="Assets/Scenes/UnitTestingScene.unity")

# 6. Close the saved, clean temp scene before deleting files.
manage_scene(action="close_scene", scene_path="Assets/__McpTemp_<purpose>/TempScene.unity", remove_scene=True)

# 7. Delete the temp folder only after the temp scene is closed.
manage_asset(action="delete", path="Assets/__McpTemp_<purpose>")
```

Do not delete a scene asset or its containing folder while that scene is loaded. If a deleted scene remains loaded and dirty, `close_scene` may refuse to close it and `save` may open a modal prompt.

Do not save tracked project scenes merely to clear dirty state; saving a tracked scene can serialize unrelated Unity YAML changes. If a tracked scene is accidentally saved and then restored from Git while open, Unity may show a "changed on disk" reload prompt that blocks MCP until a human responds.

If a Unity modal appears, stop MCP operations and ask the user to dismiss it. On unattended machines, treat modal-triggering flows as failures and restart from a clean Editor state.

`manage_asset(action="delete")` can leave a stale deleted-folder handle in Unity's AssetDatabase for the remainder of the Editor session. The folder may be gone from disk and Git, but `manage_asset(get_info)` may report `assetType: Unknown` and `isFolder: false`, and recreating the same path may fail. Use unique temp folder names per Editor session. Restarting Unity clears these stale handles when path reuse is required.

## Parameter Type Conventions

These are common patterns, not strict guarantees. `manage_components.set_property` payload shapes can vary by component/property; if a template fails, inspect the component resource payload and adjust.

### Vectors (position, rotation, scale, color)
```python
# Both forms accepted:
position=[1.0, 2.0, 3.0]        # List
position="[1.0, 2.0, 3.0]"     # JSON string
```

### Booleans
```python
# Both forms accepted:
include_inactive=True           # Boolean
include_inactive="true"         # String
```

### Colors
```python
# Auto-detected format:
color=[255, 0, 0, 255]         # 0-255 range
color=[1.0, 0.0, 0.0, 1.0]    # 0.0-1.0 normalized (auto-converted)
```

### Paths
```python
# Assets-relative (default):
path="Assets/Scripts/MyScript.cs"

# URI forms:
uri="mcpforunity://path/Assets/Scripts/MyScript.cs"
uri="file:///full/path/to/file.cs"
```

## Core Tool Categories

| Category | Key Tools | Use For |
|----------|-----------|---------|
| **Scene** | `manage_scene`, `find_gameobjects` | Scene operations, finding objects. Multi-scene editing (additive load, close, set active, move GO between scenes), scene templates (`3d_basic`, `2d_basic`, `empty`, `default`), scene validation with `auto_repair`. For build settings, use `manage_build(action="scenes")`. |
| **Objects** | `manage_gameobject`, `manage_components` | Creating/modifying GameObjects |
| **Scripts** | `create_script`, `script_apply_edits`, `validate_script` | C# code management (auto-refreshes on create/edit) |
| **Assets** | `manage_asset`, `manage_prefabs` | Asset operations. **Prefab instantiation** is done via `manage_gameobject(action="create", prefab_path="...")`, not `manage_prefabs`. |
| **Editor** | `manage_editor`, `execute_menu_item`, `read_console` | Editor control, package deployment (`deploy_package`/`restore_package`), undo/redo (`undo`/`redo` actions) |
| **Testing** | `run_tests`, `get_test_job` | Unity Test Framework. Prefer reading `mcpforunity://tests` first to list tests read-only; run the smallest targeted EditMode test only when listing is unavailable or execution is explicitly needed. |
| **Batch** | `batch_execute` | Parallel/bulk operations |
| **Camera** | `manage_camera` | Camera management (Unity Camera + Cinemachine). **Tier 1** (always available): create, target, lens, priority, list, screenshot. **Tier 2** (requires `com.unity.cinemachine`): brain, body/aim/noise pipeline, extensions, blending, force/release. 7 presets: follow, third_person, freelook, dolly, static, top_down, side_scroller. Resource: `mcpforunity://scene/cameras`. Use `ping` to check Cinemachine availability. See [tools-reference.md](references/tools-reference.md#camera-tools). |
| **Graphics** | `manage_graphics` | Rendering and post-processing management. 33 actions across 5 groups: **Volume** (create/configure volumes and effects, URP/HDRP), **Bake** (lightmaps, light probes, reflection probes, Edit mode only), **Stats** (draw calls, batches, memory), **Pipeline** (quality levels, pipeline settings), **Features** (URP renderer features: add, remove, toggle, reorder). Resources: `mcpforunity://scene/volumes`, `mcpforunity://rendering/stats`, `mcpforunity://pipeline/renderer-features`. Use `ping` to check pipeline status. See [tools-reference.md](references/tools-reference.md#graphics-tools). |
| **Packages** | `manage_packages` | Install, remove, search, and manage Unity packages and scoped registries. Query actions: list installed, search registry, get info, ping, poll status. Mutating actions: add/remove packages, embed for editing, add/remove scoped registries, force resolve. Validates identifiers, warns on git URLs, checks dependents before removal (`force=true` to override). See [tools-reference.md](references/tools-reference.md#package-tools). |
| **ProBuilder** | `manage_probuilder` | 3D modeling, mesh editing, complex geometry. **When `com.unity.probuilder` is installed, prefer ProBuilder shapes over primitive GameObjects** for editable geometry, multi-material faces, or complex shapes. Supports 12 shape types, face/edge/vertex editing, smoothing, and per-face materials. See [ProBuilder Guide](references/probuilder-guide.md). |
| **UI** | `manage_ui`, `batch_execute` with `manage_gameobject` + `manage_components` | **UI Toolkit**: Use `manage_ui` to create UXML/USS files, attach UIDocument, inspect visual trees. **uGUI (Canvas)**: Use `batch_execute` for Canvas, Panel, Button, Text, Slider, Toggle, Input Field. **Read `mcpforunity://project/info` first** to detect uGUI/TMP/Input System/UI Toolkit availability. (see [UI workflows](references/workflows.md#ui-creation-workflows)) |
| **Docs** | `unity_reflect`, `unity_docs` | API verification and documentation lookup. **`unity_reflect`** inspects live C# APIs via reflection (requires Unity connection): `search` types across assemblies, `get_type` for member summary, `get_member` for full signatures. **`unity_docs`** fetches official docs from docs.unity3d.com (no Unity connection needed): `get_doc` (ScriptReference), `get_manual` (Manual pages), `get_package_doc` (package docs), `lookup` (parallel search all sources + project assets). **Trust hierarchy: reflection > project assets > docs.** Workflow: `unity_reflect` search -> get_type -> get_member -> `unity_docs` lookup. See [tools-reference.md](references/tools-reference.md#docs-tools). |

## Common Workflows

### Creating a New Script and Using It

```python
# 1. Create the script (automatically triggers import + compilation)
create_script(
    path="Assets/Scripts/PlayerController.cs",
    contents="using UnityEngine;\n\npublic class PlayerController : MonoBehaviour\n{\n    void Update() { }\n}"
)

# 2. Wait for compilation to finish
# Read mcpforunity://editor/state → wait until is_compiling == false

# 3. Check for compilation errors
read_console(types=["error"], count=10)

# 4. Only then attach to GameObject
manage_gameobject(action="modify", target="Player", components_to_add=["PlayerController"])
```

### Finding and Modifying GameObjects

```python
# 1. Find by name/tag/component (returns IDs only)
result = find_gameobjects(search_term="Enemy", search_method="by_tag", page_size=50)

# 2. Get full data via resource
# mcpforunity://scene/gameobject/{instance_id}

# 3. Modify using the ID
manage_gameobject(action="modify", target=instance_id, position=[10, 0, 0])
```

### Running and Monitoring Tests

```python
# 1. Start test run (async)
result = run_tests(mode="EditMode", test_names=["MyTests.TestSomething"])
job_id = result["job_id"]

# 2. Poll for completion
result = get_test_job(job_id=job_id, wait_timeout=60, include_failed_tests=True)
```

## Pagination Pattern

Large queries return paginated results. Always follow `next_cursor`:

```python
cursor = 0
all_items = []
while True:
    result = manage_scene(action="get_hierarchy", page_size=50, cursor=cursor)
    all_items.extend(result["data"]["items"])
    if not result["data"].get("next_cursor"):
        break
    cursor = result["data"]["next_cursor"]
```

## Multi-Instance Workflow

When multiple Unity Editors are running:

```python
# 1. List instances via resource: mcpforunity://instances
# 2. Set active instance
set_active_instance(instance="MyProject@abc123")
# 3. All subsequent calls route to that instance
```

## Error Recovery

| Symptom | Cause | Solution |
|---------|-------|----------|
| Tools return "busy" | Compilation in progress | Wait, check `editor_state` |
| "stale_file" error | File changed since SHA | Re-fetch SHA with `get_sha`, retry |
| Connection lost | Domain reload | Wait ~5s, reconnect |
| Commands fail silently | Wrong instance | Check `set_active_instance` |
| Save Scene modal appears | Dirty loaded scene was closed, deleted, or saved with ambiguous path | Stop and ask user to dismiss the modal. For future attempts, create the scene at its final path, save active scene without `path`, switch to an existing scene, close the clean temp scene, then delete files. |
| Reload changed scene modal appears | A loaded tracked scene changed on disk, often after `git restore` | Stop and ask user to reload or close the scene. Avoid saving/restoring tracked scenes during temp workflows. |
| Temp folder path reports `assetType: Unknown` and `isFolder: false` | Stale AssetDatabase entry after `manage_asset(delete)` or deleting a loaded scene | Use a new unique temp folder for the rest of the Editor session. Restart Unity only when exact path reuse is required. |

## Reference Files

For detailed schemas and examples:

- **[tools-reference.md](references/tools-reference.md)**: Complete tool documentation with all parameters
- **[resources-reference.md](references/resources-reference.md)**: All available resources and their data
- **[workflows.md](references/workflows.md)**: Extended workflow examples and patterns
