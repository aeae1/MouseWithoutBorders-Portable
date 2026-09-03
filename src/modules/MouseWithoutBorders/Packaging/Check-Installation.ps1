[CmdletBinding()]
param(
    [string] $InstallDirectory = (Join-Path $env:ProgramFiles 'Mouse Without Borders'),

    [string] $UserRoamingAppData
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$serviceName = 'MouseWithoutBorders.Service'
$firewallRuleName = 'Mouse Without Borders (Standalone)'
$uninstallRegistryPath = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\MouseWithoutBordersStandalone'
$installMarkerName = '.mwb-standalone-install.json'
$results = New-Object System.Collections.Generic.List[object]

function Add-CheckResult {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('PASS', 'WARNING', 'FAIL')]
        [string] $Status,

        [Parameter(Mandatory = $true)]
        [string] $Check,

        [Parameter(Mandatory = $true)]
        [string] $Details
    )

    $results.Add([PSCustomObject]@{
        Status = $Status
        Check = $Check
        Details = $Details
    })
}

function Test-RunRegistryPath {
    param([Parameter(Mandatory = $true)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $properties = Get-ItemProperty -LiteralPath $Path
    foreach ($property in $properties.PSObject.Properties) {
        if ($property.Name.StartsWith('PS')) {
            continue
        }

        $candidate = $property.Name + ' ' + [string]$property.Value
        if ($candidate -match '(?i)Mouse\s*Without\s*Borders|MouseWithoutBorders') {
            return $true
        }
    }

    return $false
}

if ([string]::IsNullOrWhiteSpace($UserRoamingAppData)) {
    $UserRoamingAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
}

$InstallDirectory = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
$UserRoamingAppData = [IO.Path]::GetFullPath($UserRoamingAppData).TrimEnd('\')
$appPath = Join-Path $InstallDirectory 'MouseWithoutBorders.exe'
$servicePath = Join-Path $InstallDirectory 'MouseWithoutBordersService.exe'

$requiredFiles = @(
    'MouseWithoutBorders.exe',
    'MouseWithoutBordersHelper.exe',
    'MouseWithoutBordersService.exe',
    'Uninstall.ps1',
    $installMarkerName
)
$missingFiles = @($requiredFiles | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $InstallDirectory $_) -PathType Leaf)
})
if ($missingFiles.Count -eq 0) {
    Add-CheckResult 'PASS' 'Program files' 'All required standalone files are installed.'
} else {
    Add-CheckResult 'FAIL' 'Program files' ('Missing: ' + ($missingFiles -join ', '))
}

$markerPath = Join-Path $InstallDirectory $installMarkerName
if (Test-Path -LiteralPath $markerPath -PathType Leaf) {
    try {
        $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
        if ($marker.Product -eq 'Mouse Without Borders Standalone' -and $marker.StartMode -eq 'Manual') {
            Add-CheckResult 'PASS' 'Install record' 'The standalone manual-start install record is valid.'
        } else {
            Add-CheckResult 'FAIL' 'Install record' 'The install record does not identify the expected manual-start build.'
        }
    } catch {
        Add-CheckResult 'FAIL' 'Install record' 'The install record could not be read.'
    }
} else {
    Add-CheckResult 'FAIL' 'Install record' 'The standalone install record is missing.'
}

try {
    $service = Get-CimInstance -ClassName Win32_Service -Filter "Name='$serviceName'" -ErrorAction Stop
    if ($null -eq $service) {
        Add-CheckResult 'FAIL' 'Support service' 'The optional support service is not registered.'
    } else {
        $serviceProblems = New-Object System.Collections.Generic.List[string]
        if ($service.StartMode -ne 'Manual') {
            $serviceProblems.Add("startup type is $($service.StartMode), not Manual")
        }
        if (-not $service.PathName.Contains($servicePath)) {
            $serviceProblems.Add('program path does not point to this installation')
        }
        if ($service.PathName -match '(?i)PowerToys') {
            $serviceProblems.Add('program path still mentions PowerToys')
        }

        if ($serviceProblems.Count -eq 0) {
            $serviceState = if ($service.State -eq 'Stopped') { ' It is currently stopped.' } else { " Its current state is $($service.State)." }
            Add-CheckResult 'PASS' 'Support service' ('The service is registered for manual/on-demand use.' + $serviceState)
        } else {
            Add-CheckResult 'FAIL' 'Support service' ($serviceProblems -join '; ')
        }
    }
} catch {
    Add-CheckResult 'FAIL' 'Support service' 'Windows could not read the support-service registration.'
}

try {
    $firewallRules = @(Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction Stop)
    if ($firewallRules.Count -eq 0) {
        Add-CheckResult 'FAIL' 'Firewall access' 'The required inbound firewall rule is missing.'
    } else {
        $validFirewallRule = $false
        foreach ($firewallRule in $firewallRules) {
            $applicationFilter = Get-NetFirewallApplicationFilter -AssociatedNetFirewallRule $firewallRule -ErrorAction Stop
            if ($firewallRule.Enabled -eq 'True' -and
                $firewallRule.Direction -eq 'Inbound' -and
                $firewallRule.Action -eq 'Allow' -and
                $applicationFilter.Program -eq $appPath) {
                $validFirewallRule = $true
                break
            }
        }

        if ($validFirewallRule) {
            Add-CheckResult 'PASS' 'Firewall access' 'The inbound app rule is enabled and points to this installation.'
        } else {
            Add-CheckResult 'FAIL' 'Firewall access' 'The firewall rule exists but its settings or program path are incorrect.'
        }
    }
} catch {
    Add-CheckResult 'FAIL' 'Firewall access' 'Windows could not find or read the required inbound firewall rule.'
}

$programsDirectory = Join-Path $UserRoamingAppData 'Microsoft\Windows\Start Menu\Programs'
$missingShortcuts = @(
    'Mouse Without Borders.lnk',
    'Uninstall Mouse Without Borders.lnk',
    'Check Mouse Without Borders installation.lnk'
) | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $programsDirectory $_) -PathType Leaf)
}
if (@($missingShortcuts).Count -eq 0) {
    Add-CheckResult 'PASS' 'Start menu' 'The app, checker, and uninstall shortcuts are present.'
} else {
    Add-CheckResult 'FAIL' 'Start menu' ('Missing: ' + (@($missingShortcuts) -join ', '))
}

if (Test-Path -LiteralPath $uninstallRegistryPath) {
    Add-CheckResult 'PASS' 'Installed Apps' 'Windows has an uninstall entry for the standalone app.'
} else {
    Add-CheckResult 'FAIL' 'Installed Apps' 'The Windows uninstall entry is missing.'
}

$automaticStartFound = $false
foreach ($runRegistryPath in @(
    'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run',
    'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run',
    'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run'
)) {
    if (Test-RunRegistryPath $runRegistryPath) {
        $automaticStartFound = $true
    }
}

$startupDirectories = @(
    (Join-Path $UserRoamingAppData 'Microsoft\Windows\Start Menu\Programs\Startup'),
    (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\Startup')
)
foreach ($startupDirectory in $startupDirectories) {
    if (Test-Path -LiteralPath $startupDirectory -PathType Container) {
        $startupEntry = Get-ChildItem -LiteralPath $startupDirectory -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '(?i)Mouse\s*Without\s*Borders|MouseWithoutBorders' } |
            Select-Object -First 1
        if ($startupEntry) {
            $automaticStartFound = $true
        }
    }
}

if ($automaticStartFound) {
    Add-CheckResult 'FAIL' 'Manual run mode' 'A Mouse Without Borders automatic-start entry was found.'
} else {
    Add-CheckResult 'PASS' 'Manual run mode' 'No Mouse Without Borders automatic-start entry was found.'
}

$reportLines = New-Object System.Collections.Generic.List[string]
$reportLines.Add('Mouse Without Borders installation check')
$reportLines.Add(('Checked: ' + [DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss')))
$reportLines.Add('')

Write-Host ''
Write-Host 'Mouse Without Borders installation check'
Write-Host '------------------------------------------'
foreach ($result in $results) {
    $color = switch ($result.Status) {
        'PASS' { 'Green' }
        'WARNING' { 'Yellow' }
        default { 'Red' }
    }
    Write-Host ("[{0}] {1}: {2}" -f $result.Status, $result.Check, $result.Details) -ForegroundColor $color
    $reportLines.Add(("[{0}] {1}: {2}" -f $result.Status, $result.Check, $result.Details))
}

$failures = @($results | Where-Object Status -eq 'FAIL').Count
$warnings = @($results | Where-Object Status -eq 'WARNING').Count
$reportLines.Add('')
$reportLines.Add(("Summary: {0} passed, {1} warning(s), {2} failed." -f ($results.Count - $warnings - $failures), $warnings, $failures))

$desktopDirectory = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
if ([string]::IsNullOrWhiteSpace($desktopDirectory) -or -not (Test-Path -LiteralPath $desktopDirectory -PathType Container)) {
    $desktopDirectory = [IO.Path]::GetTempPath()
}
$reportPath = Join-Path $desktopDirectory 'MouseWithoutBorders-Installation-Check.txt'
$reportLines | Set-Content -LiteralPath $reportPath -Encoding UTF8

Write-Host ''
Write-Host ("Summary: {0} passed, {1} warning(s), {2} failed." -f ($results.Count - $warnings - $failures), $warnings, $failures)
if ($desktopDirectory -eq [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)) {
    Write-Host 'A copy of this report was saved on your Desktop.'
} else {
    Write-Host "The report was saved to '$reportPath'."
}

if ($failures -gt 0) {
    exit 1
}

exit 0
