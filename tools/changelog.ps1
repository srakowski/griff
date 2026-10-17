<#
  Turns the changelog's "Unreleased" section (site/src/data/changelog.md) into the entry of a release: its heading
  becomes "## <version> - <today>" and a new empty Unreleased starts above it. Run before tagging, then commit.
  Refuses an empty Unreleased or a version that already has an entry.
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\changelog.ps1 -Version 0.1.0-alpha.8
#>
param([Parameter(Mandatory)][string]$Version)
$ErrorActionPreference = 'Stop'
$file = Join-Path (Split-Path $PSScriptRoot -Parent) 'site/src/data/changelog.md'
$Version = $Version.TrimStart('v')
$text = [System.IO.File]::ReadAllText($file)
if ($text -match "(?m)^## $([regex]::Escape($Version))( |$)") { throw "The changelog already has an entry for $Version." }
$match = [regex]::Match($text, '(?ms)^## Unreleased[ \t]*\r?\n(.*?)(?=^## |\z)')
if (-not $match.Success) { throw 'The changelog has no "## Unreleased" section.' }
if ($match.Groups[1].Value -notmatch '(?m)^- ') { throw 'Unreleased has no bullets: write what changed first.' }
$date = Get-Date -Format 'yyyy-MM-dd'
$newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
$text = ([regex]'(?m)^## Unreleased[ \t]*\r?$').Replace($text, "## Unreleased$newline$newline## $Version - $date", 1)
[System.IO.File]::WriteAllText($file, $text)
Write-Host "Unreleased is now the entry of $Version ($date). Commit it, then tag."
