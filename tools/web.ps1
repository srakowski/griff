<#
  Griff in a browser: publishes the web player (src\Griff.Web, .NET for WebAssembly), exports a game for the web with it
  and serves it at http://127.0.0.1:8091/ (this computer only) until Ctrl+C. With no -Project it's the proof game: the
  griffin, which the arrow keys or WASD move, under a label. -Check plays it in a headless Edge (or Chrome) instead, with
  no window: it checks the label's text and the griffin are drawn and that holding the right arrow moves the griffin,
  saves the two screenshots and exits 0 if so. -Compare checks the browser draws the same pixels as the desktop: every
  template's start scene, and three scenes of lights, shadows, screen effects and a viewport, held still after -Steps steps (90) in
  the headless browser and drawn by the desktop's OpenGL renderer after the same, compared pixel by pixel; -Template
  picks some (comma-separated names), and the pictures of each (desktop, web and what differs) go in -Out's folder.
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\web.ps1 [-Check | -Compare [-Template <names>] [-Steps 90]] [-Project <folder>] [-Port 8091] [-Out <folder>] [-NoBuild]
#>
param(
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
