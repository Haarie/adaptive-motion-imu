<#
  Build the Adaptive Motion IMU SimHub plugin and install it into SimHub.

    powershell -ExecutionPolicy Bypass -File install.ps1
    powershell -ExecutionPolicy Bypass -File install.ps1 -SimHubDir "D:\SimHub"

  Needs the .NET SDK (any version 6..10). SimHub must be closed while the DLL is copied.
#>
param([string]$SimHubDir = "C:\Program Files (x86)\SimHub")
$ErrorActionPreference = "Stop"
$proj = Join-Path $PSScriptRoot "SimSpares.AdaptiveMotion\SimSpares.AdaptiveMotion.csproj"

if (-not (Test-Path (Join-Path $SimHubDir "SimHub.Plugins.dll"))) {
    Write-Host "SimHub not found in $SimHubDir - pass -SimHubDir ""<your SimHub folder>""" -ForegroundColor Red; exit 1
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "dotnet not found - install the .NET SDK from https://dotnet.microsoft.com/download" -ForegroundColor Red; exit 1
}

Write-Host "Building ..." -ForegroundColor Cyan
& dotnet build $proj -c Release "-p:SimHubDir=$SimHubDir" -nologo -v:minimal
if ($LASTEXITCODE -ne 0) { Write-Host "Build failed - copy the errors above into the chat." -ForegroundColor Red; exit 1 }
$out = Join-Path $PSScriptRoot "SimSpares.AdaptiveMotion\bin\Release\net48"
$dll = Join-Path $out "SimSpares.AdaptiveMotion.dll"
if (-not (Test-Path $dll)) { Write-Host "Build output not found: $dll" -ForegroundColor Red; exit 1 }

$running = Get-Process -Name "SimHubWPF" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "SimHub is running. Close it (also from the tray icon), then press Enter." -ForegroundColor Yellow
    Read-Host | Out-Null
    if (Get-Process -Name "SimHubWPF" -ErrorAction SilentlyContinue) { Write-Host "SimHub still running - stopped." -ForegroundColor Red; exit 1 }
}

Write-Host "Copying into $SimHubDir (Windows asks for administrator permission) ..." -ForegroundColor Cyan
$files = @($dll, (Join-Path $out "SimSpares.AdaptiveMotion.pdb")) | Where-Object { Test-Path $_ }
$copy = ($files | ForEach-Object { "Copy-Item -LiteralPath '$_' -Destination '$SimHubDir' -Force" }) -join "; "
$p = Start-Process powershell -Verb RunAs -Wait -PassThru -ArgumentList "-NoProfile", "-Command", "$copy; if (-not `$?) { exit 1 }"
if ($p.ExitCode -ne 0 -and -not (Test-Path (Join-Path $SimHubDir "SimSpares.AdaptiveMotion.dll"))) {
    Write-Host "Copy failed." -ForegroundColor Red; exit 1
}
$installed = Get-Item (Join-Path $SimHubDir "SimSpares.AdaptiveMotion.dll")
Write-Host ("Installed {0} ({1:N0} bytes, {2})" -f $installed.FullName, $installed.Length, $installed.LastWriteTime) -ForegroundColor Green
Write-Host "Start SimHub: it asks to enable the new plugin 'Adaptive Motion (IMU)' - say yes." -ForegroundColor Green
