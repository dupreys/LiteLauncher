<#
  Builds dist\LiteLauncher.msix, signed with a local developer certificate (dist\LiteLauncher-dev.cer).

  Needs: the Windows 11 SDK (MakeAppx.exe and SignTool.exe) and a finished build\publish.ps1 run.
  Run from the repository root:   powershell -ExecutionPolicy Bypass -File packaging\build-msix.ps1
#>
param(
    [string]$Version = '1.0.0.0',
    [string]$Subject = 'CN=LiteLauncher Dev'   # must equal the Publisher in the package; also the certificate subject
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not (Test-Path 'publish\LiteLauncher.exe')) {
    throw 'publish\LiteLauncher.exe not found. Run build\publish.ps1 first.'
}

function Find-SdkTool([string]$name) {
    $base = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $tool = Get-ChildItem $base -Recurse -Filter $name -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $tool) { throw "$name not found. Install the Windows 11 SDK (the 'Windows SDK Signing Tools' and 'MSIX Packaging' parts)." }
    $tool.FullName
}
$makeappx = Find-SdkTool 'makeappx.exe'
$signtool = Find-SdkTool 'signtool.exe'

# --- stage the package contents ---
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'msix-stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force "$stage\Assets", "$stage\Public" | Out-Null

Copy-Item 'publish\*' $stage -Recurse
Copy-Item 'packaging\Assets\*.png' "$stage\Assets"
Copy-Item 'packaging\CustomCapability.SCCD' $stage
Set-Content "$stage\Public\README.txt" 'Folder for the windows.gamingApp extension.'
(Get-Content 'packaging\AppxManifest.xml' -Raw).Replace('__VERSION__', $Version).Replace('__PUBLISHER__', $Subject) |
    Set-Content "$stage\AppxManifest.xml" -Encoding UTF8

# --- developer certificate (reused while it is valid) ---
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Subject -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
    Sort-Object NotAfter -Descending | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creating developer certificate $Subject ..." -ForegroundColor Cyan
    $cert = New-SelfSignedCertificate -Type Custom -Subject $Subject -KeyUsage DigitalSignature `
        -FriendlyName 'LiteLauncher developer certificate' -CertStoreLocation 'Cert:\CurrentUser\My' `
        -NotAfter (Get-Date).AddYears(3) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}
Export-Certificate -Cert $cert -FilePath (Join-Path $dist 'LiteLauncher-dev.cer') -Type CERT | Out-Null

# --- pack and sign ---
$msix = Join-Path $dist 'LiteLauncher.msix'
& $makeappx pack /d $stage /p $msix /o
if ($LASTEXITCODE -ne 0) { throw 'MakeAppx failed (read its message above: usually a manifest or missing-file problem).' }

& $signtool sign /fd SHA256 /sha1 $cert.Thumbprint $msix
if ($LASTEXITCODE -ne 0) { throw 'SignTool failed.' }

Write-Host "Done: $msix" -ForegroundColor Green
Write-Host 'Next: run packaging\install-fse.ps1 as administrator.'
