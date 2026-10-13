<#
  BunnyMark: how many bunnies an exported Griff game keeps bouncing at 60 fps on this computer.
  Builds the editor in Release (which publishes the game players), exports the BunnyMark template with the standalone
  player, plays it on screen until it has found the count, and prints it. Close other programs first; it takes a minute
  or so a run. It's a heuristic, not a test: the count varies from run to run, so -Runs prints the middle of several.
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\bunnymark.ps1 [-Runs 3] [-NoBuild]
#>
param(
    [int]$Runs = 1,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

if (-not $NoBuild) {
    dotnet build "$repo\src\Griff.Editor\Griff.Editor.csproj" -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Push-Location $repo
try {
    dotnet run -c Release "$repo\tools\bunnymark.cs" -- --runs $Runs
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
