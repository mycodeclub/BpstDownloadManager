# Builds publish\VeloxDM.exe (self-contained, single file) and dist\VeloxDM-Setup-<version>.exe.
# Requires the .NET 10 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).
param([string]$Version = "0.1.0")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

dotnet publish src/VeloxDM.App -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:Version=$Version -o publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }

& $iscc "/DAppVersion=$Version" installer\VeloxDM.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }
Get-Item "dist\VeloxDM-Setup-$Version.exe"
