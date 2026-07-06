# Unity MCP Runbook

This project uses [MCP for Unity](https://github.com/CoplayDev/unity-mcp) to let Codex inspect and operate a running Unity Editor through Model Context Protocol tools.

## Setup

1. Use Unity `6000.2.1f1`, matching `ProjectSettings/ProjectVersion.txt`.
2. Open the project in Unity and let Package Manager import `com.coplaydev.unity-mcp` from `Packages/manifest.json`.
   - On this workstation, prefer `.\scripts\Launch-UnityMcp.ps1` from the worktree. It prepends the concrete pyenv Python and `Scripts` directories for the Unity process only, so MCP for Unity can detect `python.exe` and `uvx.exe` without changing the global User PATH.
3. Open the MCP for Unity window from the Unity Editor menu.
4. Start the local HTTP MCP server on `http://localhost:8080/mcp`.
5. Start or restart Codex from a trusted checkout of this repo.
6. In Codex, use `/mcp` or the available tool list to confirm the `unity` MCP server is connected.

Codex reads `.codex/config.toml` only for trusted projects. If Unity MCP tools are missing, first confirm project trust, Unity is open to this project, and the MCP server is running.

## Recommended Use

- Use MCP first for Editor-backed work: scene or prefab inspection, console reads, compilation refreshes, Unity Test Runner jobs, package status, asset import settings, screenshots, and PlayMode/Editor state checks.
- Use `manage_tools` to list and activate only the tool groups needed for the current task.
- Keep source-code edits in normal repo files through Codex file editing unless a Unity MCP script or asset tool is specifically safer for the operation.
- After any MCP mutation, inspect `git status` and `git diff` before continuing. Unity may dirty scenes, prefabs, package lock files, settings, or generated metadata.
- Keep the existing PowerShell batchmode test commands as the CI-parity fallback and for final verification when MCP test output is incomplete.

## Conservative Approval Policy

The project config sets `default_tools_approval_mode = "prompt"` for Unity MCP. Treat these tool categories as approval-gated by default:

- build and player generation
- package install/update/remove
- scene, prefab, material, import setting, or asset mutation
- arbitrary C# execution or script generation
- deletion, bulk asset moves, or broad project rewrites
- AI asset generation or tools that may contact external services

Read-only inspection, console reads, tool listing, and targeted test runs are normally low risk, but still verify the connected Unity instance is the intended worktree.

## Evaluation Checklist

Run this checklist whenever Unity MCP is installed, upgraded, or behaving unexpectedly:

1. Confirm `manage_tools` can list tool groups.
2. Read Editor state and console output.
3. Use Unity docs/reflection tools for one Unity API question relevant to this project.
4. Validate or refresh scripts and confirm compilation results are visible.
5. Run an EditMode test job through MCP and compare the result with `TestResults/EditModeResults.xml` from batchmode.
6. Run a small PlayMode smoke test through MCP and compare with batchmode when scene/UI behavior is involved.
7. In a disposable worktree only, create and remove temporary assets under `Assets/__McpSmokeTest/`, then verify the diff is clean except for intentional files.
8. Record any limitation or surprising dirty file in this runbook or the relevant skill before relying on that workflow.

## Initial Validation

The `chore/unity-mcp-hookup` setup was validated on Unity `6000.2.1f1`:

- Unity Package Manager resolved `com.coplaydev.unity-mcp` from the pinned Git URL and updated `Packages/packages-lock.json`.
- `codex mcp list` from the trusted worktree reported `unity` enabled at `http://localhost:8080/mcp`.
- EditMode batchmode test run passed: `1` total, `1` passed, `0` failed.
- PlayMode batchmode test run passed: `22` total, `22` passed, `0` failed.
- Test logs showed MCP for Unity's TestRunner helper applying and restoring its no-throttle behavior during Unity Test Runner execution.
- No scene, prefab, material, or `.meta` files were dirtied by package import or test execution.

The current Codex session could not use the newly configured MCP tools without restarting Codex after `.codex/config.toml` changed. Complete the live tool evaluation checklist in a fresh Codex session after starting the Unity MCP HTTP server from the Editor.

## Known Limitations

- MCP tools require an open Unity Editor instance; they do not replace headless CI.
- Codex must be restarted after `.codex/config.toml` changes for the MCP server config to load.
- Multiple open Unity projects can route tools to the wrong Editor if the server or active instance is not selected carefully.
- Unity serialized files can change for reasons unrelated to the requested operation. Review scene, prefab, material, and `.meta` diffs closely.
- MCP test jobs may not expose the same logs and XML artifacts as batchmode Test Framework runs. Use batchmode commands when CI parity matters.
- Network/package resolution is still handled by Unity Package Manager and may fail independently of Codex MCP connectivity.
