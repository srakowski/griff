<#
  Griff in a browser: publishes the web player (src\Griff.Web, .NET for WebAssembly), exports a game for the web with it
  and serves it at http://127.0.0.1:8091/ (this computer only) until Ctrl+C. With no -Project it's the proof game: the
  griffin, which the arrow keys or WASD move, under a label. -Check plays it in a headless Edge (or Chrome) instead, with
  no window: it checks the label's text and the griffin are drawn and that holding the right arrow moves the griffin,
  saves the two screenshots and exits 0 if so.
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\web.ps1 [-Check] [-Project <folder>] [-Port 8091] [-Out <folder>] [-NoBuild]
#>
param(
    [switch]$Check,
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
    if ($Project) { $arguments += @('--project', (Resolve-Path $Project).Path) }
    if ($Out) { $arguments += @('--out', $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)) }
    dotnet run -c Release "$repo\tools\web.cs" -- @arguments
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
