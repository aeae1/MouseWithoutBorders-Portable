[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $BuildRoot,

    [Parameter(Mandatory = $true)]
    [string] $Destination
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$BuildRoot = [IO.Path]::GetFullPath($BuildRoot).TrimEnd('\')
$Destination = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')

if (-not (Test-Path -LiteralPath $BuildRoot -PathType Container)) {
    throw "Build output directory '$BuildRoot' was not found."
}

if (Test-Path -LiteralPath $Destination) {
    if (Get-ChildItem -LiteralPath $Destination -Force | Select-Object -First 1) {
        throw "Package destination '$Destination' must be empty."
    }
} else {
    New-Item -ItemType Directory -Path $Destination | Out-Null
}

Get-ChildItem -LiteralPath $BuildRoot -Force | Copy-Item -Destination $Destination -Recurse -Force

foreach ($packagingFile in @('Install.cmd', 'Install.ps1', 'Uninstall.cmd', 'Uninstall.ps1')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $packagingFile) -Destination $Destination -Force
}

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $Destination 'INSTALLING.md') -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $Destination -Force
Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $Destination -Force

$requiredFiles = @(
    'MouseWithoutBorders.exe',
    'MouseWithoutBordersHelper.exe',
    'MouseWithoutBordersService.exe',
    'Install.cmd',
    'Install.ps1',
    'Uninstall.cmd',
    'Uninstall.ps1',
    'INSTALLING.md',
    'README.md',
    'LICENSE'
)

foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $Destination $requiredFile) -PathType Leaf)) {
        throw "Package staging failed. Missing $requiredFile."
    }
}

Write-Host "Manual-start package staged at '$Destination'."
