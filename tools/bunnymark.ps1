<#
  BunnyMark: how many bunnies an exported Griff game keeps bouncing at 60 fps on this computer.
  Builds the editor in Release (which publishes the game players), exports the BunnyMark template with the standalone
  player, plays it on screen until it has found the count and held it for ten seconds, and prints the count with the
  hold's frame rate, 1% low and garbage collections. -Count holds that many bunnies instead, to check a count. Close
  other programs first; it takes a minute or so a run. It's a heuristic, not a test: the count varies from run to run,
  so -Runs prints the middle of several.
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\bunnymark.ps1 [-Runs 3] [-Count 50000] [-NoBuild]
#>
param(
    [int]$Runs = 1,
    [int]$Count = 0,
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
    $arguments = @('--runs', $Runs)
    if ($Count -gt 0) { $arguments += @('--count', $Count) }
    dotnet run -c Release "$repo\tools\bunnymark.cs" -- @arguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
