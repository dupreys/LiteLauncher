<#
  Builds LiteLauncher into .\publish (self-contained: the PC needs no .NET installed)
  and, when Inno Setup 6 is installed, also builds the installer into .\dist.

  Run from the repository root:   powershell -ExecutionPolicy Bypass -File build\publish.ps1
  Needs: the .NET 8 SDK. Optional: Inno Setup 6 (https://jrsoftware.org/isinfo.php)
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

Write-Host '== Tests ==' -ForegroundColor Cyan
dotnet test --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }

Write-Host '== Publish ==' -ForegroundColor Cyan
if (Test-Path publish) { Remove-Item publish -Recurse -Force }
dotnet publish src\LiteLauncher.App -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o publish
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Write-Host "Done: $root\publish\LiteLauncher.exe" -ForegroundColor Green

$iscc = @(
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if ($iscc) {
    Write-Host '== Installer ==' -ForegroundColor Cyan
    & $iscc installer\LiteLauncher.iss
    if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
    Write-Host "Done: $root\dist\LiteLauncher-Setup.exe" -ForegroundColor Green
} else {
    Write-Host 'Inno Setup 6 not found: skipped the installer. Install it and run this script again.' -ForegroundColor Yellow
}
