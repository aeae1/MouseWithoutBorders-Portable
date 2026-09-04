[CmdletBinding()]
param(
    [string] $Path = (Join-Path $PSScriptRoot 'App\ClassicGreen.ico')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$expectedSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$bytes = [IO.File]::ReadAllBytes([IO.Path]::GetFullPath($Path))

if ($bytes.Length -lt 6) {
    throw "Portable icon '$Path' is too small to be a valid ICO file."
}

$reserved = [BitConverter]::ToUInt16($bytes, 0)
$imageType = [BitConverter]::ToUInt16($bytes, 2)
$imageCount = [BitConverter]::ToUInt16($bytes, 4)
if ($reserved -ne 0 -or $imageType -ne 1) {
    throw "Portable icon '$Path' does not have a valid ICO header."
}

if ($imageCount -ne $expectedSizes.Count -or $bytes.Length -lt (6 + (16 * $imageCount))) {
    throw "Portable icon '$Path' must contain $($expectedSizes.Count) complete image entries."
}

$actualSizes = @()
for ($index = 0; $index -lt $imageCount; $index++) {
    $entryOffset = 6 + (16 * $index)
    $width = if ($bytes[$entryOffset] -eq 0) { 256 } else { [int]$bytes[$entryOffset] }
    $height = if ($bytes[$entryOffset + 1] -eq 0) { 256 } else { [int]$bytes[$entryOffset + 1] }
    $bitsPerPixel = [BitConverter]::ToUInt16($bytes, $entryOffset + 6)
    $imageSize = [BitConverter]::ToUInt32($bytes, $entryOffset + 8)
    $imageOffset = [BitConverter]::ToUInt32($bytes, $entryOffset + 12)

    if ($width -ne $height -or $bitsPerPixel -ne 32) {
        throw "Portable icon entry $index must be a square 32-bit image."
    }

    if ($imageSize -eq 0 -or ([uint64]$imageOffset + [uint64]$imageSize) -gt [uint64]$bytes.Length) {
        throw "Portable icon entry $index is truncated or points outside the ICO file."
    }

    $actualSizes += $width
}

$sizeDifferences = @(Compare-Object -ReferenceObject $expectedSizes -DifferenceObject $actualSizes)
if ($sizeDifferences.Count -ne 0) {
    throw "Portable icon sizes must be: $($expectedSizes -join ', '). Found: $($actualSizes -join ', ')."
}

Write-Host "Portable icon validated: $($actualSizes -join ', ') pixels ($($bytes.Length) bytes)."
