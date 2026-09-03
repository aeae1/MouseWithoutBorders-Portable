[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string] $InstallDirectory = (Join-Path $env:ProgramFiles 'Mouse Without Borders'),

    [string] $UserRoamingAppData,

    [switch] $ElevatedStage
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$serviceName = 'MouseWithoutBorders.Service'
$firewallRuleName = 'Mouse Without Borders (Standalone)'
$uninstallRegistryPath = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MouseWithoutBordersStandalone'
$installMarkerName = '.mwb-standalone-install.json'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function ConvertTo-QuotedArgument {
    param([Parameter(Mandatory = $true)][string] $Value)

    if ($Value.Contains('"')) {
        throw 'Paths containing quote characters are not supported.'
    }

    return '"' + $Value + '"'
}

function Assert-SafeInstallDirectory {
    param([Parameter(Mandatory = $true)][string] $Path)

    $fullPath = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $pathRoot = [IO.Path]::GetPathRoot($fullPath).TrimEnd('\')
    $programFilesRoot = [IO.Path]::GetFullPath($env:ProgramFiles).TrimEnd('\')

    if ([string]::IsNullOrWhiteSpace($fullPath) -or $fullPath -eq $pathRoot -or $fullPath -eq $programFilesRoot) {
        throw "Refusing to use unsafe install directory '$fullPath'."
    }

    return $fullPath
}

if ([string]::IsNullOrWhiteSpace($UserRoamingAppData)) {
    $UserRoamingAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
}

$InstallDirectory = Assert-SafeInstallDirectory $InstallDirectory
$UserRoamingAppData = [IO.Path]::GetFullPath($UserRoamingAppData).TrimEnd('\')

if ($WhatIfPreference) {
    Write-Host "What if: uninstall the manual-start build from '$InstallDirectory'."
    Write-Host 'What if: remove the demand-start service, firewall rule, shortcuts, and program files.'
    Write-Host 'What if: preserve the user settings file.'
    return
}

if (-not $ElevatedStage) {
    $temporaryScript = Join-Path ([IO.Path]::GetTempPath()) ("MouseWithoutBorders-Uninstall-{0}.ps1" -f [Guid]::NewGuid().ToString('N'))
    Copy-Item -LiteralPath $PSCommandPath -Destination $temporaryScript -Force

    $powerShellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $elevationArguments = @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        (ConvertTo-QuotedArgument $temporaryScript),
        '-InstallDirectory',
        (ConvertTo-QuotedArgument $InstallDirectory),
        '-UserRoamingAppData',
        (ConvertTo-QuotedArgument $UserRoamingAppData),
        '-ElevatedStage',
        '-Confirm:$false'
    )

    $elevatedProcess = Start-Process -FilePath $powerShellPath -ArgumentList ($elevationArguments -join ' ') -Verb RunAs -Wait -PassThru
    Remove-Item -LiteralPath $temporaryScript -Force -ErrorAction SilentlyContinue
    exit $elevatedProcess.ExitCode
}

if (-not (Test-IsAdministrator)) {
    throw 'Administrator permission is required to uninstall Mouse Without Borders.'
}

$installMarkerPath = Join-Path $InstallDirectory $installMarkerName
if (-not (Test-Path -LiteralPath $installMarkerPath -PathType Leaf)) {
    throw "The standalone install marker was not found. Refusing to remove '$InstallDirectory'."
}

if (-not $PSCmdlet.ShouldProcess($InstallDirectory, 'Uninstall Mouse Without Borders')) {
    return
}

$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existingService) {
    if ($existingService.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
        Stop-Service -Name $serviceName -Force -ErrorAction Stop
        $existingService.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(20))
    }

    $scPath = Join-Path $env:SystemRoot 'System32\sc.exe'
    & $scPath delete $serviceName | Write-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Could not remove the Mouse Without Borders service (sc.exe exit code $LASTEXITCODE)."
    }
}

foreach ($processName in @('MouseWithoutBorders', 'MouseWithoutBordersHelper', 'MouseWithoutBordersService')) {
    Get-Process -Name $processName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction Stop
}

Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction Stop

$programsDirectory = Join-Path $UserRoamingAppData 'Microsoft\Windows\Start Menu\Programs'
foreach ($shortcutName in @('Mouse Without Borders.lnk', 'Uninstall Mouse Without Borders.lnk', 'Check Mouse Without Borders installation.lnk')) {
    $shortcutPath = Join-Path $programsDirectory $shortcutName
    if (Test-Path -LiteralPath $shortcutPath -PathType Leaf) {
        Remove-Item -LiteralPath $shortcutPath -Force
    }
}

if (Test-Path -LiteralPath $uninstallRegistryPath) {
    Remove-Item -LiteralPath $uninstallRegistryPath -Recurse -Force
}

Remove-Item -LiteralPath $InstallDirectory -Recurse -Force

Write-Host ''
Write-Host 'Mouse Without Borders was uninstalled successfully.'
Write-Host 'Your saved Mouse Without Borders settings were preserved.'
