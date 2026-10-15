<#
  The editor release: builds the editor in Release (which publishes the game players), gathers it with its players, the
  free fonts, LICENSE and the third-party notices into Griff-<version>/, and zips that to Griff-<version>-win-x64.zip.
  The Release workflow (.github/workflows/release.yml) runs this, so a release is what you can make here.
  The version is the tag without its v: tools\release.ps1 -Version 0.1.0 for the tag v0.1.0. -Out is where the zip and the
  folder it was made from go (default artifacts\release). -NoBuild reuses the last Release build. Prints the zip's path.
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\release.ps1 -Version 0.1.0 [-Out <folder>] [-NoBuild]
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Out,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "The version is MAJOR.MINOR.PATCH, like 0.1.0 (the tag v0.1.0), not '$Version'." }
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

The Player folder holds the game players the Build menu packs your games with (for Windows, and for the
web in Player\Web). FreeFonts holds
the fonts Griff offers for games, each with its license. LICENSE is Griff's (MIT); THIRD-PARTY-NOTICES.txt lists
what others made that Griff carries.
"@ | Set-Content -Encoding utf8 (Join-Path $folder 'README.txt')

Compress-Archive -Path $folder -DestinationPath $zip -CompressionLevel Optimal
Write-Host "Made $zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
Write-Output $zip
