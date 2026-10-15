<#
  Griff in a browser: publishes the web player (src\Griff.Web, .NET for WebAssembly), exports a game for the web with it
  and serves it at http://127.0.0.1:8091/ (this computer only) until Ctrl+C: -Project <folder> (a project), -Template
  <name> (a template made into a project) or, with neither, the proof game (the griffin, which the arrow keys, WASD or a
  stick move, under a label).
  -Export writes the export to -Out and stops (with -Zip, a zip for itch.io beside it): the command line's Build > Web.
  -Check plays games in a headless Edge (or Chrome) instead, with no window, and exits 0 if all pass: the proof game and
  the Platformer, Shooter, Match3, Isometric, BunnyMark and Multiplayer templates (-Template picks some, -Project checks
  that project). Each must run and draw; its sound must wait for a click or key, saying so, with its music keeping time,
  then play through Web Audio; input must reach it (keys, a tap, a controller, as each game takes them); and its saves
  must survive reloading the page. The proof game is also checked at a phone's and a 4K screen's size and going
  fullscreen, Match-3 quitting, and Multiplayer saying network play isn't there.
  -Compare checks the browser draws the same pixels as the desktop: every scene of every template, and three scenes of
  lights, shadows, screen effects and a viewport, held still after -Steps steps (90) in the headless browser and drawn by
  the desktop's OpenGL renderer after the same, compared pixel by pixel; -Template picks some (comma-separated names).
  Screenshots and pictures go in -Out's folder.
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\web.ps1 [-Export [-Zip] | -Check | -Compare [-Steps 90]] [-Project <folder> | -Template <names>] [-Port 8091] [-Out <folder>] [-NoBuild]
#>
param(
    [switch]$Export,
    [switch]$Zip,
    [switch]$Check,
    [switch]$Compare,
    [string]$Template,
    [int]$Steps = -1,
    [string]$Project,
    [int]$Port = 8091,
    [string]$Out,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

if (-not $NoBuild) {
    dotnet publish "$repo\src\Griff.Web\Griff.Web.csproj" -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

Push-Location $repo
try {
    $arguments = @('--port', $Port)
    if ($Export) { $arguments += '--export' }
    if ($Zip) { $arguments += '--zip' }
    if ($Check) { $arguments += '--check' }
    if ($Compare) { $arguments += '--compare' }
    if ($Template) { $arguments += @('--template', $Template) }
    if ($Steps -ge 0) { $arguments += @('--steps', $Steps) }
    if ($Project) { $arguments += @('--project', (Resolve-Path $Project).Path) }
    if ($Out) { $arguments += @('--out', $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)) }
    dotnet run -c Release "$repo\tools\web.cs" -- @arguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
