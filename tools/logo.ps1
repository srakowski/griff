<#
  Draws Griff's logo, a 16 x 16 pixel-art griffin, into assets\:
    griff.png  256 x 256, for the editor's title bar and start page and the README
    griff.ico  16, 32, 48, 64, 128 and 256, for the editor's and the game players' .exe
  Every sprite pixel becomes an exact square block, so the logo stays sharp at any whole-number size.
  After changing the sprite, run it again and rebuild:
    powershell -NoProfile -ExecutionPolicy Bypass -File tools\logo.ps1
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# Top to bottom, facing right: ear tufts, brow, eye, hooked beak, feathered neck. '.' is see-through.
$sprite = @(
    '................',
    '..o.............',
    '..oo..o.........',
    '..ooo.oo........',
    '..olllllloo.....',
    '.oooooddddoo....',
    '.ooooookwoooy...',
    '.ooooookkooyyyy.',
    '.ooooooooooyyyyy',
    '.ddooooooooyy.yy',
    'ddddoooooooo...y',
    '.ddddoooooo.....',
    'ddddddoooo......',
    '.ddddddoo.......',
    '..ddddd.........',
    '................'
)

# AARRGGBB. o is the editor's accent (Color.Accent.500), d and l are it darkened and lightened.
$colors = @{}
@{
    '.' = '00000000'
    'o' = 'FFF08C2E'
    'd' = 'FFB46923'
    'l' = 'FFF5AF6D'
    'y' = 'FFFFD15C'
    'k' = 'FF16171B'
    'w' = 'FFFFFFFF'
}.GetEnumerator() | ForEach-Object { $colors[$_.Key] = [Convert]::ToInt32($_.Value, 16) }

foreach ($row in $sprite) {
    if ($row.Length -ne 16) { throw "Sprite row '$row' is $($row.Length) pixels wide, not 16." }
}
if ($sprite.Count -ne 16) { throw "The sprite is $($sprite.Count) rows tall, not 16." }

# The color of the pixel at (x, y) in a size x size picture.
function Get-Argb([int]$size, [int]$x, [int]$y) {
    $scale = $size / 16
    $ch = [string]$sprite[[math]::Floor($y / $scale)][[math]::Floor($x / $scale)]
    return [int]$colors[$ch]
}

function Get-Png([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    for ($y = 0; $y -lt $size; $y++) {
        for ($x = 0; $x -lt $size; $x++) {
            $bitmap.SetPixel($x, $y, [System.Drawing.Color]::FromArgb((Get-Argb $size $x $y)))
        }
    }
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    return ,$stream.ToArray()
}

# An icon image the classic way: a 32-bit bottom-up DIB followed by its 1-bit transparency mask.
function Get-Dib([int]$size) {
    $maskStride = [math]::Ceiling($size / 32) * 4
    $stream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($stream)
    $writer.Write([int]40); $writer.Write([int]$size); $writer.Write([int]($size * 2))
    $writer.Write([int16]1); $writer.Write([int16]32); $writer.Write([int]0)
    $writer.Write([int]($size * $size * 4 + $maskStride * $size))
    $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0); $writer.Write([int]0)
    for ($y = $size - 1; $y -ge 0; $y--) {
        for ($x = 0; $x -lt $size; $x++) { $writer.Write([int](Get-Argb $size $x $y)) }
    }
    for ($y = $size - 1; $y -ge 0; $y--) {
        $mask = New-Object byte[] $maskStride
        for ($x = 0; $x -lt $size; $x++) {
            if ((((Get-Argb $size $x $y) -shr 24) -band 0xFF) -eq 0) { $mask[[math]::Floor($x / 8)] = $mask[[math]::Floor($x / 8)] -bor (0x80 -shr ($x % 8)) }
        }
        $writer.Write($mask)
    }
    $writer.Flush()
    return ,$stream.ToArray()
}

$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'assets'
New-Item -ItemType Directory -Force $assets | Out-Null

$png = Get-Png 256
[System.IO.File]::WriteAllBytes((Join-Path $assets 'griff.png'), $png)

# 256 is stored as a PNG, as Windows expects; the smaller sizes as DIBs, which every tool reads.
$sizes = 16, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) { if ($size -eq 256) { ,$png } else { ,(Get-Dib $size) } }

$stream = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($stream)
$writer.Write([int16]0); $writer.Write([int16]1); $writer.Write([int16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $side = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$side); $writer.Write([byte]$side); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([int16]1); $writer.Write([int16]32)
    $writer.Write([int]$images[$i].Length); $writer.Write([int]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write([byte[]]$image) }
$writer.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $assets 'griff.ico'), $stream.ToArray())

Write-Host "Wrote $assets\griff.png and $assets\griff.ico"
