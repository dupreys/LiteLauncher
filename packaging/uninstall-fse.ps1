<#  Removes the LiteLauncher package, its certificate and, if it points at LiteLauncher, the GamingHomeApp value.  #>
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'

$package = Get-AppxPackage -Name 'LiteLauncher.Home'
if ($package) {
    $aumid = "$($package.PackageFamilyName)!App"
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\GamingConfiguration'
    if ((Get-ItemProperty $key -Name GamingHomeApp -ErrorAction SilentlyContinue).GamingHomeApp -eq $aumid) {
        Set-ItemProperty $key -Name GamingHomeApp -Value ''
        Write-Host 'Xbox mode home app cleared.'
    }
    Remove-AppxPackage -Package $package.PackageFullName
    Write-Host 'Package removed.' -ForegroundColor Green
} else {
    Write-Host 'The package is not installed.'
}

Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object { $_.Subject -eq 'CN=LiteLauncher Dev' } | Remove-Item
Write-Host 'Certificate removed from Trusted People.'
