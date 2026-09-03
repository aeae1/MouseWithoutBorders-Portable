[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string] $InstallDirectory = (Join-Path $env:ProgramFiles 'Mouse Without Borders'),

    [string] $UserSid,

    [string] $UserLocalAppData,

    [string] $UserRoamingAppData
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$serviceName = 'MouseWithoutBorders.Service'
$serviceDisplayName = 'Mouse Without Borders Service'
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

function Invoke-Sc {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)

    $scPath = Join-Path $env:SystemRoot 'System32\sc.exe'
    & $scPath @Arguments | Write-Host
    if ($LASTEXITCODE -ne 0) {
        throw "sc.exe failed with exit code $LASTEXITCODE."
    }
}

function Stop-MwbProcesses {
    foreach ($processName in @('MouseWithoutBorders', 'MouseWithoutBordersHelper', 'MouseWithoutBordersService')) {
        Get-Process -Name $processName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction Stop
    }
}

if ([string]::IsNullOrWhiteSpace($UserSid)) {
    $UserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
}

if ($UserSid -notmatch '^S-\d(?:-\d+)+$') {
    throw "The Windows user SID '$UserSid' is invalid."
}

if ([string]::IsNullOrWhiteSpace($UserLocalAppData)) {
    $UserLocalAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
}

if ([string]::IsNullOrWhiteSpace($UserRoamingAppData)) {
    $UserRoamingAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
}

$InstallDirectory = Assert-SafeInstallDirectory $InstallDirectory
$UserLocalAppData = [IO.Path]::GetFullPath($UserLocalAppData).TrimEnd('\')
$UserRoamingAppData = [IO.Path]::GetFullPath($UserRoamingAppData).TrimEnd('\')
$sourceDirectory = [IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')

foreach ($requiredFile in @('MouseWithoutBorders.exe', 'MouseWithoutBordersHelper.exe', 'MouseWithoutBordersService.exe', 'Uninstall.ps1')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceDirectory $requiredFile) -PathType Leaf)) {
        throw "The installer package is incomplete. Missing $requiredFile."
    }
}

if ($WhatIfPreference) {
    Write-Host "What if: install the manual-start build from '$sourceDirectory' to '$InstallDirectory'."
    Write-Host 'What if: register a demand-start service and add the inbound TCP firewall rule.'
    Write-Host 'What if: create Start menu shortcuts. No automatic-start entry will be created.'
    return
}

if (-not (Test-IsAdministrator)) {
    $powerShellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $elevationArguments = @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        (ConvertTo-QuotedArgument $PSCommandPath),
        '-InstallDirectory',
        (ConvertTo-QuotedArgument $InstallDirectory),
        '-UserSid',
        (ConvertTo-QuotedArgument $UserSid),
        '-UserLocalAppData',
        (ConvertTo-QuotedArgument $UserLocalAppData),
        '-UserRoamingAppData',
        (ConvertTo-QuotedArgument $UserRoamingAppData),
        '-Confirm:$false'
    )

    $elevatedProcess = Start-Process -FilePath $powerShellPath -ArgumentList ($elevationArguments -join ' ') -Verb RunAs -Wait -PassThru
    exit $elevatedProcess.ExitCode
}

if (-not $PSCmdlet.ShouldProcess($InstallDirectory, 'Install Mouse Without Borders in manual-start mode')) {
    return
}

$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existingService -and $existingService.Status -ne [System.ServiceProcess.ServiceControllerStatus]::Stopped) {
    Stop-Service -Name $serviceName -Force -ErrorAction Stop
    $existingService.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(20))
}

Stop-MwbProcesses

if ($sourceDirectory -ne $InstallDirectory) {
    New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
    Get-ChildItem -LiteralPath $sourceDirectory -Force | Copy-Item -Destination $InstallDirectory -Recurse -Force
}

$appPath = Join-Path $InstallDirectory 'MouseWithoutBorders.exe'
$servicePath = Join-Path $InstallDirectory 'MouseWithoutBordersService.exe'
$uninstallScriptPath = Join-Path $InstallDirectory 'Uninstall.ps1'

foreach ($installedFile in @($appPath, $servicePath, $uninstallScriptPath)) {
    if (-not (Test-Path -LiteralPath $installedFile -PathType Leaf)) {
        throw "Installation copy failed. Missing '$installedFile'."
    }
}

$serviceBinaryPath = (ConvertTo-QuotedArgument $servicePath) + ' ' + (ConvertTo-QuotedArgument $UserLocalAppData)
if ($existingService) {
    Invoke-Sc @('config', $serviceName, 'binPath=', $serviceBinaryPath, 'start=', 'demand', 'DisplayName=', $serviceDisplayName)
} else {
    Invoke-Sc @('create', $serviceName, 'binPath=', $serviceBinaryPath, 'start=', 'demand', 'DisplayName=', $serviceDisplayName)
}

Invoke-Sc @('description', $serviceName, 'Supports Mouse Without Borders on secure Windows desktops when service mode is requested.')

# Preserve the upstream service permissions while granting the installing user
# permission to start, stop, pause, and interrogate the demand-start service.
$serviceSddl = 'D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)(A;;CR;;;AU)(A;;CCLCSWRPWPDTLOCRRC;;;PU)(A;;RPWPDTLO;;;' + $UserSid + ')'
Invoke-Sc @('sdset', $serviceName, $serviceSddl)

Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction Stop
New-NetFirewallRule -DisplayName $firewallRuleName -Direction Inbound -Action Allow -Program $appPath -Protocol TCP -Profile Any | Out-Null

$programsDirectory = Join-Path $UserRoamingAppData 'Microsoft\Windows\Start Menu\Programs'
New-Item -ItemType Directory -Path $programsDirectory -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell

$appShortcut = $shell.CreateShortcut((Join-Path $programsDirectory 'Mouse Without Borders.lnk'))
$appShortcut.TargetPath = $appPath
$appShortcut.WorkingDirectory = $InstallDirectory
$appShortcut.IconLocation = "$appPath,0"
$appShortcut.Description = 'Open Mouse Without Borders'
$appShortcut.Save()

$uninstallShortcut = $shell.CreateShortcut((Join-Path $programsDirectory 'Uninstall Mouse Without Borders.lnk'))
$uninstallShortcut.TargetPath = (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe')
$uninstallShortcut.Arguments = '-NoProfile -ExecutionPolicy Bypass -File ' + (ConvertTo-QuotedArgument $uninstallScriptPath)
$uninstallShortcut.WorkingDirectory = $InstallDirectory
$uninstallShortcut.IconLocation = "$appPath,0"
$uninstallShortcut.Description = 'Uninstall Mouse Without Borders'
$uninstallShortcut.Save()

$installMarker = @{
    Product = 'Mouse Without Borders Standalone'
    Version = '0.0.1'
    InstalledUtc = [DateTime]::UtcNow.ToString('o')
    UserSid = $UserSid
    StartMode = 'Manual'
}
$installMarker | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $InstallDirectory $installMarkerName) -Encoding UTF8

New-Item -Path $uninstallRegistryPath -Force | Out-Null
New-ItemProperty -Path $uninstallRegistryPath -Name 'DisplayName' -Value 'Mouse Without Borders (Standalone)' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallRegistryPath -Name 'DisplayVersion' -Value '0.0.1' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallRegistryPath -Name 'Publisher' -Value 'aeae1 standalone fork' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallRegistryPath -Name 'DisplayIcon' -Value "$appPath,0" -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallRegistryPath -Name 'InstallLocation' -Value $InstallDirectory -PropertyType String -Force | Out-Null
New-ItemProperty -Path $uninstallRegistryPath -Name 'NoModify' -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallRegistryPath -Name 'NoRepair' -Value 1 -PropertyType DWord -Force | Out-Null
$uninstallCommand = (ConvertTo-QuotedArgument (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe')) + ' -NoProfile -ExecutionPolicy Bypass -File ' + (ConvertTo-QuotedArgument $uninstallScriptPath)
New-ItemProperty -Path $uninstallRegistryPath -Name 'UninstallString' -Value $uninstallCommand -PropertyType String -Force | Out-Null

Write-Host ''
Write-Host 'Mouse Without Borders was installed successfully.'
Write-Host 'It will not start with Windows. Open it manually from the Start menu.'
Write-Host 'The optional support service is registered as demand-start and is not running.'
