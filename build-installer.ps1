# Builds BDM 2.x release packages:
#   dist\BDM-Setup-<version>.exe   Windows installer (Inno Setup 6: winget install JRSoftware.InnoSetup)
#   dist\BDM-<version>-android.apk  Android app (needs the .NET MAUI Android workload, Android SDK and JDK)
param([string]$Version = "2.1.0", [switch]$SkipAndroid)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force dist | Out-Null

# ---- Windows
Remove-Item publish\windows -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish src/Bdm.App -f net10.0-windows10.0.19041.0 -c Release `
    -p:ApplicationDisplayVersion=$Version -p:Version=$Version -o publish\windows
if ($LASTEXITCODE -ne 0) { throw "Windows publish failed" }

# Refuse to package a build whose pages don't open.
$testData = Join-Path $env:TEMP "bdm-selftest-$([guid]::NewGuid().ToString('N'))"
$env:BDM_DATA_DIR = $testData
$selfTest = Start-Process publish\windows\BDM.exe -ArgumentList "--self-test" -PassThru -Wait
Remove-Item Env:BDM_DATA_DIR
if ($selfTest.ExitCode -ne 0) { Get-Content "$testData\logs\*.log"; throw "UI self-test failed" }
Write-Host "UI self-test passed."

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found. Install it with: winget install JRSoftware.InnoSetup" }
& $iscc "/DAppVersion=$Version" installer\BDM.iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

# ---- Android
if (-not $SkipAndroid) {
    # Release signing key: env vars (CI) or the local private folder created for BDM.
    $localKey = Join-Path $env:USERPROFILE "BDM-signing\bdm-release.keystore"
    if (-not $env:BDM_KEYSTORE -and (Test-Path $localKey)) {
        $env:BDM_KEYSTORE = $localKey
        $env:BDM_KEYSTORE_PASSWORD = Get-Content (Join-Path $env:USERPROFILE "BDM-signing\keystore-password.txt") -Raw
    }
    if ($env:BDM_KEYSTORE) { Write-Host "Signing the APK with the BDM release key." }
    else { Write-Warning "No release key (BDM_KEYSTORE): the APK is signed with the debug key - fine for testing, NOT for publishing." }
    Remove-Item publish\android -Recurse -Force -ErrorAction SilentlyContinue
    dotnet publish src/Bdm.App -f net10.0-android -c Release -p:ApplicationDisplayVersion=$Version -o publish\android
    if ($LASTEXITCODE -ne 0) { throw "Android publish failed" }
    $apk = Get-ChildItem publish\android -Filter "*-Signed.apk" | Select-Object -First 1
    Copy-Item $apk.FullName "dist\BDM-$Version-android.apk" -Force
}

Get-ChildItem dist -File | Where-Object Name -like "BDM-*" | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
