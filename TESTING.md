# Unity Test Workflow

This project uses Unity `6000.2.1f1`. `Packages/manifest.json` lists Unity Test Framework `1.3.11`; Unity 6 resolves the active package to `1.5.1` through the Development feature set.

## Running Tests In The Editor

1. Open the project in Unity `6000.2.1f1`.
2. Open `Window > General > Test Runner`.
3. Run all EditMode tests.
4. Run all PlayMode tests before merging gameplay, UI, scene, or prefab changes.

## Running Tests From PowerShell

Adjust the Unity path if your editor is installed elsewhere.

```powershell
New-Item -ItemType Directory -Force TestResults | Out-Null

& "C:\Program Files\Unity\Hub\Editor\6000.2.1f1\Editor\Unity.exe" `
  -batchmode `
  -runTests `
  -projectPath . `
  -testPlatform editmode `
  -testResults TestResults/EditModeResults.xml `
  -logFile TestResults/EditMode.log

& "C:\Program Files\Unity\Hub\Editor\6000.2.1f1\Editor\Unity.exe" `
  -batchmode `
  -runTests `
  -projectPath . `
  -testPlatform playmode `
  -testResults TestResults/PlayModeResults.xml `
  -logFile TestResults/PlayMode.log
```

Keep test output outside `Assets/`, `Library/`, `Logs/`, and other generated Unity folders.

Do not pass `-quit` to these commands. The Unity Test Framework command-line runner exits after completion and logs that tests will not work when `-quit` is specified.

## Running Tests From macOS/Linux Shells

Adjust `UNITY` if your editor is installed elsewhere. The macOS path shown is Unity Hub's default location. The Linux path is the common Unity Hub install location.

```bash
mkdir -p TestResults

# macOS
UNITY="/Applications/Unity/Hub/Editor/6000.2.1f1/Unity.app/Contents/MacOS/Unity"

# Linux
# UNITY="$HOME/Unity/Hub/Editor/6000.2.1f1/Editor/Unity"

"$UNITY" \
  -batchmode \
  -runTests \
  -projectPath . \
  -testPlatform editmode \
  -testResults TestResults/EditModeResults.xml \
  -logFile TestResults/EditMode.log

"$UNITY" \
  -batchmode \
  -runTests \
  -projectPath . \
  -testPlatform playmode \
  -testResults TestResults/PlayModeResults.xml \
  -logFile TestResults/PlayMode.log
```

The same output and `-quit` rules apply: keep results outside generated Unity folders, and do not pass `-quit`.

## Test Structure

- `Assets/Tests/EditMode/EditModeAssembly.asmdef`: EditMode tests for data structures and pure logic.
- `Assets/Tests/PlayMode/PlayModeAssembly.asmdef`: PlayMode tests for scene, UI, FSM, and MonoBehaviour behavior.
- Runtime code is in `Assets/MainGameAssembly.asmdef`.

## CI

`.github/workflows/unity-tests.yml` runs both EditMode and PlayMode with GameCI on pushes and pull requests targeting `main` and `pre-release`, plus manual dispatch.

Required secrets:

1. `UNITY_LICENSE`
2. `UNITY_EMAIL`
3. `UNITY_PASSWORD`

## Adding Tests

- Put pure logic and data validation tests in `Assets/Tests/EditMode`.
- Put scene, UI, prefab, lifecycle, and interaction tests in `Assets/Tests/PlayMode`.
- Use deterministic inputs. If a test touches Unity random state, save and restore it.
- Prefer focused tests around Pathfinder rules math, action economy, pathfinding, line of sight, and UI workflows.
