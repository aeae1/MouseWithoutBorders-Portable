[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('x64', 'ARM64')]
    [string] $Platform = 'x64',

    [switch] $RunTests,

    [switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$msbuild = Get-Command 'msbuild.exe' -ErrorAction SilentlyContinue
if (-not $msbuild) {
    $msbuild = Get-Command 'msbuild' -ErrorAction SilentlyContinue
}

if (-not $msbuild) {
    throw 'MSBuild was not found. Run this script from a Visual Studio Developer PowerShell window.'
}

$projects = @(
    'App\MouseWithoutBorders.Standalone.csproj',
    'App\Helper\MouseWithoutBordersHelper.Standalone.csproj',
    'App\Service\MouseWithoutBordersService.Standalone.csproj',
    'MouseWithoutBorders.UnitTests\MouseWithoutBorders.UnitTests.csproj'
)

foreach ($project in $projects) {
    $projectPath = Join-Path $PSScriptRoot $project
    $arguments = @(
        $projectPath,
        '/m',
        "/p:Configuration=$Configuration",
        "/p:Platform=$Platform"
    )

    if (-not $NoRestore) {
        $arguments += '/restore'
    }

    Write-Host "Building $project"
    & $msbuild.Source @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for $project (exit code $LASTEXITCODE)."
    }
}

if ($RunTests) {
    $dotnet = Get-Command 'dotnet.exe' -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        $dotnet = Get-Command 'dotnet' -ErrorAction SilentlyContinue
    }

    if (-not $dotnet) {
        throw '.NET was not found. Install the .NET 10 SDK and try again.'
    }

    $testProject = Join-Path $PSScriptRoot 'MouseWithoutBorders.UnitTests\MouseWithoutBorders.UnitTests.csproj'
    Write-Host 'Running unit tests'
    & $dotnet.Source test $testProject --no-build --no-restore -c $Configuration "-p:Platform=$Platform"
    if ($LASTEXITCODE -ne 0) {
        throw "Unit tests failed (exit code $LASTEXITCODE)."
    }
}

Write-Host "Mouse Without Borders $Configuration $Platform build completed successfully."
