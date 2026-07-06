param(
    [string]$ProjectPath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path,
    [string]$UnityExe = "C:\Program Files\Unity\Hub\Editor\6000.2.1f1\Editor\Unity.exe",
    [string]$PythonRoot = "C:\Users\Josh\.pyenv\pyenv-win\versions\3.12.10"
)

$ErrorActionPreference = "Stop"

$resolvedProject = (Resolve-Path -LiteralPath $ProjectPath).Path
$pythonScripts = Join-Path $PythonRoot "Scripts"

foreach ($path in @($UnityExe, (Join-Path $PythonRoot "python.exe"), (Join-Path $pythonScripts "uvx.exe"))) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Required path not found: $path"
    }
}

$openUnity = Get-CimInstance Win32_Process -Filter "name = 'Unity.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine -like "*$resolvedProject*" }

if ($openUnity) {
    $ids = ($openUnity | ForEach-Object { $_.ProcessId }) -join ", "
    throw "Unity already has this project open. Close it before launching with this script. PID(s): $ids"
}

$pathPrefix = @($PythonRoot, $pythonScripts) -join [IO.Path]::PathSeparator
$env:Path = $pathPrefix + [IO.Path]::PathSeparator + $env:Path

Start-Process -FilePath $UnityExe -ArgumentList @("-projectPath", $resolvedProject)
