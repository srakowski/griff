<#
  The editor release: builds the editor in Release (which publishes the game players), gathers it with its players, the
  free fonts, LICENSE and the third-party notices into Griff-<version>/, and zips that to Griff-<version>-win-x64.zip (the
  portable download). Then Velopack's vpk (a tool in dotnet-tools.json) packs the same folder as the installer and its
  updates into <Out>elopack: GriffEditor-<channel>-Setup.exe (installs for the user, no admin, and the .NET 10 Desktop
  Runtime if it's missing), the full package, a delta package from the previous release when its full package is in that
  folder, and the feed (releases.<channel>.json) the installed editor reads.
  The Release workflow (.github/workflows/release.yml) runs this, so a release is what you can make here.
  The version is the tag without its v: tools
elease.ps1 -Version 0.1.0 for the tag v0.1.0, or -Version 0.1.0-alpha.1 for
  the pre-release v0.1.0-alpha.1. A pre-release goes on the "prerelease" channel, a release on "stable".
  -Out is where it all goes (default artifacts
elease). -NoBuild reuses the last Release build. -NoInstaller makes just the
  zip. Packing again into the same folder keeps the earlier versions there, so it is a feed to update from (GRIFF_UPDATE_SOURCE).
  Prints the zip's path.
    powershell -NoProfile -ExecutionPolicy Bypass -File tools
elease.ps1 -Version 0.1.0 [-Out <folder>] [-NoBuild] [-NoInstaller]
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Out,
    [switch]$NoBuild,
    [switch]$NoInstaller
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
# MAJOR.MINOR.PATCH, and for a pre-release a dot-separated label after a hyphen (SemVer 2, as .NET and Velopack read it).
if ($Version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$') {
    throw "The version is MAJOR.MINOR.PATCH, like 0.1.0 (the tag v0.1.0), or a pre-release like 0.1.0-alpha.1, not '$Version'."
}
$channel = if ($Version.Contains('-')) { 'prerelease' } else { 'stable' }
if (-not $Out) { $Out = Join-Path $repo 'artifacts\release' }
$Out = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out)

if (-not $NoBuild) {
    dotnet build "$repo\src\Griff.Editor\Griff.Editor.csproj" -c Release --nologo -v q "-p:Version=$Version"
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$build = Join-Path $repo 'src\Griff.Editor\bin\Release\net10.0-windows'
foreach ($needed in 'Griff.Editor.exe', 'Player\Standalone\Griff.Player.exe', 'Player\Small\Griff.Player.exe', 'Player\Web\wwwroot\index.html', 'FreeFonts\JetBrainsMono-OFL.txt') {
    if (-not (Test-Path (Join-Path $build $needed))) { throw "The Release build has no $needed (in $build)." }
}

$name = "Griff-$Version"
$folder = Join-Path $Out $name
$zip = Join-Path $Out "$name-win-x64.zip"
New-Item -ItemType Directory -Force $Out | Out-Null
Remove-Item -Recurse -Force $folder, $zip -ErrorAction SilentlyContinue

# The build folder as it is, without the debug symbols.
New-Item -ItemType Directory $folder | Out-Null
robocopy $build $folder /E /XF *.pdb /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Copying the build failed (robocopy $LASTEXITCODE)." }

Copy-Item (Join-Path $repo 'LICENSE') $folder
dotnet run -c Release (Join-Path $repo 'tools\release-notices.cs') -- $folder (Join-Path $folder 'THIRD-PARTY-NOTICES.txt')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

@"
Griff $Version
=============

Griff is a game engine and editor for game jams: https://griff.games

To run it:
  1. It needs 64-bit Windows and the .NET 10 Desktop Runtime
     (https://dotnet.microsoft.com/download/dotnet/10.0, "Desktop Runtime").
  2. Unzip this folder anywhere you can write to, and run Griff.Editor.exe.
  3. File > New Project, pick a template, press Play.

This is the portable copy: it runs from wherever you unzip it and doesn't update itself. The installer on the
same release (GriffEditor-...-Setup.exe) installs Griff for you, with the .NET runtime it needs, and keeps it up to date.

The Player folder holds the game players the Build menu packs your games with (for Windows, and for the
web in Player\Web). FreeFonts holds
the fonts Griff offers for games, each with its license. LICENSE is Griff's (MIT); THIRD-PARTY-NOTICES.txt lists
what others made that Griff carries.
"@ | Set-Content -Encoding utf8 (Join-Path $folder 'README.txt')

Compress-Archive -Path $folder -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Made $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"

if (-not $NoInstaller) {
    # The installer and the updates: the same folder, less the zip's README, as the package GriffEditor. (Not "Griff": an
    # app is installed in %LOCALAPPDATA%\<id> and removing it deletes that folder, and %LOCALAPPDATA%\Griff holds the
    # editor's settings.)
    $feed = Join-Path $Out 'velopack'
    New-Item -ItemType Directory -Force $feed | Out-Null
    # Packing a version again replaces it.
    Get-ChildItem $feed -Filter "GriffEditor-$Version-*.nupkg" | Remove-Item -Force
    Push-Location $repo
    try {
        dotnet tool restore | Out-Null
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        dotnet vpk pack --packId GriffEditor --packVersion $Version --packDir $folder --mainExe Griff.Editor.exe `
            --packTitle Griff --packAuthors 'The Griff contributors' --icon (Join-Path $repo 'assets\griff.ico') `
            --channel $channel --runtime win-x64 --outputDir $feed --framework net10.0-x64-desktop --noPortable --exclude 'README\.txt' `
            --skip-updates --yes
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    finally {
        Pop-Location
    }
    Write-Host "Packed the installer and updates in $feed"
}

Write-Output $zip
