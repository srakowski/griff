<#
  Griff in a browser: publishes the web player (src\Griff.Web, .NET for WebAssembly), exports a game for the web with it
  and serves it at http://127.0.0.1:8091/ (this computer only) until Ctrl+C: -Project <folder> (a project), -Template
  <name> (a template made into a project) or, with neither, the proof game (the griffin, which the arrow keys, WASD or a
  stick move, under a label).
  -Export writes the export to -Out and stops (with -Zip, a zip for itch.io beside it): the command line's Build > Web.
  -Site <folder> exports every template for the website into <folder> (the site's playable games): .NET once in _player,
  then a folder, page and poster for each game, and games.json; -Template picks some. site\package.json's `npm run games`.
  -Check plays games in a headless Chrome (or Edge) instead, with no window, and exits 0 if all pass: the proof game, the
  Adventure template (Enter plays, taps move its talk on, a tap walks round the table, the pointer is a hand over a
  hotspot, a right click looks, a mug taken is picked from the inventory bar and filled at the kettle, the keys work the
  bar, and the items are still there after a reload), rhythm lanes (Lanes: Create Note Lanes in an empty project, notes pressed
  on the beat Web Audio plays are hits, with the keys, two fingers at once and one held) and the Platformer, Shooter,
  Match3, Isometric, Physics, Pinball, Rhythm, BunnyMark and Multiplayer templates (-Template picks some, Proof and Lanes among them; -Project
  checks that project). Each must run and draw; its sound must wait for a click or key, saying so, with its music keeping time,
  then play through Web Audio; input must reach it (keys, a tap, a controller, as each game takes them); and its saves
  must survive reloading the page. The proof game is also checked at a phone's and a 4K screen's size and going
  fullscreen, Match-3 quitting, and Multiplayer saying network play isn't there.
  -Compare checks the browser draws the same pixels as the desktop: every scene of every template, and three scenes of
  lights, shadows, screen effects and a viewport, held still after -Steps steps (90) in the headless browser and drawn by
  the desktop's OpenGL renderer after the same, compared pixel by pixel; -Template picks some (comma-separated names).
  Screenshots and pictures go in -Out's folder.
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\web.ps1 [-Export [-Zip] | -Site <folder> | -Check | -Compare [-Steps 90]] [-Project <folder> | -Template <names>] [-Port 8091] [-Out <folder>] [-NoBuild]
#>
param(
    [switch]$Export,
    [switch]$Zip,
    [switch]$Check,
    [switch]$Compare,
    [string]$Site,
    [string]$Template,
    [int]$Steps = -1,
    [string]$Project,
    [int]$Port = 8091,
    [string]$Out,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

# Under GitHub Actions a failure is also an annotation on the run (an ::error:: line), readable without signing in to see the log.
function Annotate([string]$message) {
    if ($env:GITHUB_ACTIONS -eq 'true') {
        Write-Output "::error title=tools/web.ps1::$($message.Replace('%', '%25').Replace("`r", '%0D').Replace("`n", '%0A'))"
    }
}
trap {
    Annotate "web.ps1 stopped: $_"
    break
}

if (-not $NoBuild) {
    dotnet publish "$repo\src\Griff.Web\Griff.Web.csproj" -c Release --nologo -v q
    if ($LASTEXITCODE -ne 0) {
        Annotate "Publishing the web player (src/Griff.Web) failed with exit code $LASTEXITCODE; its errors are above."
        exit $LASTEXITCODE
    }
}

Push-Location $repo
try {
    $arguments = @('--port', $Port)
    if ($Export) { $arguments += '--export' }
    if ($Zip) { $arguments += '--zip' }
    if ($Site) { $arguments += @('--site', $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Site)) }
    if ($Check) { $arguments += '--check' }
    if ($Compare) { $arguments += '--compare' }
    if ($Template) { $arguments += @('--template', $Template) }
    if ($Steps -ge 0) { $arguments += @('--steps', $Steps) }
    if ($Project) { $arguments += @('--project', (Resolve-Path $Project).Path) }
    if ($Out) { $arguments += @('--out', $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)) }
    dotnet run -c Release "$repo\tools\web.cs" -- @arguments
    # web.cs annotates what it found wrong itself; this says that it stopped, also when it couldn't start (a build error).
    if ($LASTEXITCODE -ne 0) { Annotate "tools/web.cs exited with $LASTEXITCODE." }
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
