<#
  Installs dist\LiteLauncher.msix so Windows 11 lists LiteLauncher under Settings > Gaming > Xbox mode.

  What it does (it needs an elevated PowerShell):
    1. Turns Developer Mode on for the install only. Windows requires it for the "gaming home" capability
       when the package is not signed by Microsoft. It is put back as it was afterwards.
    2. Trusts dist\LiteLauncher-dev.cer (LocalMachine\TrustedPeople) so the package can be installed.
    3. Installs the package.
  Optional:  -SetHomeApp          also writes the GamingHomeApp value for your user (unverified shortcut; the
                                  Settings page is the supported way)
             -KeepDeveloperMode   leave Developer Mode on
#>
#Requires -RunAsAdministrator
param(
    [switch]$SetHomeApp,
    [switch]$KeepDeveloperMode
)
$ErrorActionPreference = 'Stop'
$dist = Join-Path (Split-Path -Parent $PSScriptRoot) 'dist'
$msix = Join-Path $dist 'LiteLauncher.msix'
$cer = Join-Path $dist 'LiteLauncher-dev.cer'
foreach ($file in $msix, $cer) {
    if (-not (Test-Path $file)) { throw "$file not found. Run packaging\build-msix.ps1 first." }
}

$build = [Environment]::OSVersion.Version.Build
if ($build -lt 26100) { Write-Warning "This is Windows build $build. Xbox mode needs Windows 11 24H2 (26100) or later; the install will probably be refused." }

$unlock = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock'
if (-not (Test-Path $unlock)) { New-Item $unlock -Force | Out-Null }
$before = (Get-ItemProperty $unlock -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
if ($null -eq $before) { $before = 0 }

try {
    Set-ItemProperty $unlock -Name AllowDevelopmentWithoutDevLicense -Value 1 -Type DWord
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null

    Write-Host 'Installing LiteLauncher ...' -ForegroundColor Cyan
    Add-AppxPackage -Path $msix -ForceUpdateFromAnyVersion
}
finally {
    if (-not $KeepDeveloperMode) { Set-ItemProperty $unlock -Name AllowDevelopmentWithoutDevLicense -Value $before -Type DWord }
}

$package = Get-AppxPackage -Name 'LiteLauncher.Home'
if (-not $package) { throw 'The package did not install.' }
$aumid = "$($package.PackageFamilyName)!App"
Write-Host "Installed. App id: $aumid" -ForegroundColor Green

if ($SetHomeApp) {
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GamingConfiguration'
    if (-not (Test-Path $key)) { New-Item $key -Force | Out-Null }
    Set-ItemProperty $key -Name GamingHomeApp -Value $aumid
    Write-Host 'GamingHomeApp set for this user.'
}

Write-Host ''
Write-Host 'Now open Settings > Gaming > Xbox mode and choose LiteLauncher as the home app.' -ForegroundColor Yellow
Write-Host '(On a PC that is not a handheld, the "Choose home app" list may not appear; use -SetHomeApp then.)'
