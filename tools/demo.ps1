<#
.SYNOPSIS
    Plays a Griff demo: a narrated walkthrough that drives the editor with real mouse and keyboard input.

.DESCRIPTION
    demo.ps1 demos\single-file-builds.demo      Play a demo, narrated. Move the mouse to pause and take over.
    demo.ps1 demos\single-file-builds.demo -Check   Run it silently at speed and report whether it got to the end.
    demo.ps1 -All -Check                       Check every demo in demos\ (a regression run for the demos).
    demo.ps1 -Tour                             The full guided tour that builds Ember Hollow (about 20 minutes).
    demo.ps1 demos\mute.demo -Mute             Play a demo in silence: captions, no voice and no game sound.

    Demos run in a Debug build of the editor with throwaway settings and projects under %TEMP%\griff-demos, so
    they never touch your own settings or projects. While one runs the screen is the demo's: hands off, or move the
    mouse to pause it. The bar at the top (or Ctrl+Alt+Space / Left / Right / Up / Down / X) controls it; its speaker
    mutes the narration, and that's kept for the next demo. -Check runs are silent: no voice and no game sound
    (GRIFF_MUTE=1). GRIFF_MUTE=1 set before running this mutes the game's sound in narrated playback too.
#>
param(
    [string] $Demo,
    [switch] $Check,
    [switch] $All,
    [switch] $Tour,
    [switch] $NoBuild,
    [string] $Voice,
    [switch] $Mute
)

# What the caller set (an agent sets GRIFF_MUTE=1), kept under what each run sets.
$callerMute = $env:GRIFF_MUTE
$callerMuteNarration = $env:GRIFF_MUTE_NARRATION

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$editor = Join-Path $root 'src\Griff.Editor\bin\Debug\net10.0-windows\Griff.Editor.exe'
$work = Join-Path ([IO.Path]::GetTempPath()) 'griff-demos'
$log = Join-Path $work 'demo.log'
New-Item -ItemType Directory -Force $work | Out-Null
# A failed earlier run can leave its project here (half made, or locked), which would block the next one: start clean.
Get-ChildItem $work -Directory -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

if (-not $NoBuild) {
    Write-Host 'Building the editor...'
    dotnet build (Join-Path $root 'src\Griff.Editor\Griff.Editor.csproj') -c Debug -nologo -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'The editor did not build.' }
}

# A demo owns the editor while it runs; never close one that might hold someone's unsaved work.
if (Get-Process Griff.Editor -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $editor }) {
    throw 'The Debug editor is already running. Close it (or exit the demo in it) first.'
}

function Start-Demo([string] $what, [bool] $silent, [bool] $wait) {
    $settings = Join-Path $work 'settings.json'
    @{ LastProjectLocation = $work; RecentProjects = @() } | ConvertTo-Json | Set-Content $settings -Encoding utf8
    Remove-Item $log -ErrorAction SilentlyContinue

    $env:GRIFF_SETTINGS_PATH = $settings
    $env:GRIFF_DEMO = $what
    $env:GRIFF_DEMO_LOG = $log
    $env:GRIFF_DEMO_FAST = if ($silent) { '1' } else { '0' }
    $env:GRIFF_DEMO_EXIT = if ($wait) { '1' } else { '0' }
    # Its own MCP port, so an agent driving the editor someone is working in never reaches a demo's editor instead.
    $env:GRIFF_MCP_PORT = '7659'
    if ($Voice) { $env:GRIFF_DEMO_VOICE = $Voice }
    # Checks make no sound at all; -Mute plays in silence for this run (the bar's speaker is the lasting choice).
    $env:GRIFF_MUTE = if ($silent -or $Mute) { '1' } else { $callerMute }
    $env:GRIFF_MUTE_NARRATION = if ($Mute) { '1' } else { $callerMuteNarration }
    $process = Start-Process $editor -PassThru
    if (-not $wait) { return $null }
    $process.WaitForExit()
    return $process.ExitCode
}

if ($Tour) {
    Start-Demo $(if ($Check) { 'fast' } else { '1' }) $Check.IsPresent $Check.IsPresent | Out-Null
    Write-Host "The tour is running. Log: $log"
    return
}

$demos = @(if ($All) { Get-ChildItem (Join-Path $root 'demos') -Filter *.demo | ForEach-Object FullName }
         elseif ($Demo) { (Resolve-Path $Demo).Path }
         else { throw 'Name a .demo file, or use -All -Check or -Tour.' })

if (-not $Check) {
    if ($demos.Count -ne 1) { throw 'Play one demo at a time; use -Check to run several.' }
    Start-Demo $demos[0] $false $false | Out-Null
    Write-Host "Playing $([IO.Path]::GetFileName($demos[0])). Log: $log"
    return
}

$failed = @()
foreach ($file in $demos) {
    $name = [IO.Path]::GetFileName($file)
    $started = Get-Date
    $code = Start-Demo $file $true $true
    $seconds = [int]((Get-Date) - $started).TotalSeconds
    if ($code -eq 0) {
        $fallbacks = @(Select-String -Path $log -Pattern 'FORCED' -ErrorAction SilentlyContinue).Count
        Write-Host ("PASS  {0} ({1}s{2})" -f $name, $seconds, $(if ($fallbacks) { ", $fallbacks fallbacks" } else { '' }))
    }
    else {
        $failed += $name
        Write-Host "FAIL  $name ($seconds s)"
        Get-Content $log -ErrorAction SilentlyContinue | Select-String 'failed' -Context 0, 3 | ForEach-Object { "      $_" }
    }
}

if ($failed.Count -gt 0) {
    Write-Host "$($failed.Count) of $($demos.Count) demos failed."
    exit 1
}

Write-Host "All $($demos.Count) demos passed."
