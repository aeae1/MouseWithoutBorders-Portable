[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$requiredPaths = @(
    'App\MouseWithoutBorders.csproj',
    'MouseWithoutBorders.UnitTests\MouseWithoutBorders.UnitTests.csproj',
    'docs\DEVELOPMENT.md',
    'docs\UPSTREAM_SYNC.md',
    'LICENSE'
)

$forbiddenPaths = @(
    'src',
    'deps',
    'doc',
    'installer',
    'tools',
    '.pipelines',
    'PowerToys.slnx',
    'Directory.Build.targets',
    'App\Service',
    'App\MouseWithoutBorders.Standalone.csproj',
    'App\Helper\MouseWithoutBordersHelper.csproj',
    'App\Helper\MouseWithoutBordersHelper.Standalone.csproj'
)

foreach ($requiredPath in $requiredPaths) {
    if (-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot $requiredPath))) {
        throw "Required portable-project path is missing: $requiredPath"
    }
}

foreach ($forbiddenPath in $forbiddenPaths) {
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot $forbiddenPath)) {
        throw "PowerToys-only or retired comparison path returned: $forbiddenPath"
    }
}

$projectFiles = @(
    Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.csproj' -File -Recurse |
        ForEach-Object { [IO.Path]::GetRelativePath($PSScriptRoot, $_.FullName) } |
        Sort-Object
)
$expectedProjects = @(
    'App\MouseWithoutBorders.csproj',
    'MouseWithoutBorders.UnitTests\MouseWithoutBorders.UnitTests.csproj'
)

if (Compare-Object -ReferenceObject $expectedProjects -DifferenceObject $projectFiles) {
    throw "Expected only the portable app and unit-test projects. Found: $($projectFiles -join ', ')"
}

Write-Host "Clean portable repository layout verified ($($projectFiles.Count) projects)."
