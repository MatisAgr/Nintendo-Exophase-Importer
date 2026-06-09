# Builds the plugin and (optionally) installs it into Playnite for development,
# or packs a .pext for distribution.
#
#   .\build.ps1            # build only (Release)
#   .\build.ps1 -Install   # build + copy into %AppData%\Playnite\Extensions
#   .\build.ps1 -Pack      # build + produce dist\*.pext via Toolbox
param(
    [switch]$Install,
    [switch]$Pack
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "bin\Release"

dotnet build (Join-Path $root "SwitchPlaytimeExophase.csproj") -c Release
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

if ($Install) {
    $dst = Join-Path $env:AppData "Playnite\Extensions\SwitchPlaytime_Exophase"
    New-Item -ItemType Directory -Force $dst | Out-Null
    Copy-Item (Join-Path $out "*") $dst -Recurse -Force
    Write-Host "Installed to $dst -- restart Playnite." -ForegroundColor Green
}

if ($Pack) {
    $toolbox = Join-Path $env:LOCALAPPDATA "Playnite\Toolbox.exe"
    if (-not (Test-Path $toolbox)) { throw "Toolbox.exe not found at $toolbox" }
    New-Item -ItemType Directory -Force (Join-Path $root "dist") | Out-Null
    & $toolbox pack $out (Join-Path $root "dist")
}
