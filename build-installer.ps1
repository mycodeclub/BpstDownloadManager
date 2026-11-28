# Builds BDM 2.x release packages:
#   dist\BDM-Setup-<version>.exe   Windows installer (Inno Setup 6: winget install JRSoftware.InnoSetup)
#   dist\BDM-<version>-android.apk  Android app (needs the .NET MAUI Android workload, Android SDK and JDK)
#
# Updates: both packages install over an existing BDM and keep the user's downloads and settings.
#   Windows: same Inno Setup AppId, so Setup finds the installed copy, closes it and replaces it.
#   Android: same package id (com.bitprosofttech.bdm), a higher versionCode (computed from -Version below)
#            and the same signing key. Never publish a debug-signed APK: it can't be updated by a release one.
param([string]$Version = "2.2.0", [switch]$SkipAndroid, [switch]$AllowDebugSigning)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
New-Item -ItemType Directory -Force dist | Out-Null

# Android versionCode must grow with every release, or Android refuses the update.
# 2.1.0 -> 20100, 2.1.3 -> 20103, 2.2.0 -> 20200, 3.0.0 -> 30000.
if ($Version -notmatch '^(\d+)\.(\d+)\.(\d+)$') { throw "Version must look like 2.1.0 (got '$Version')" }
$VersionCode = [int]$Matches[1] * 10000 + [int]$Matches[2] * 100 + [int]$Matches[3]
Write-Host "BDM $Version (Android versionCode $VersionCode)"

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
    elseif ($AllowDebugSigning) { Write-Warning "No release key (BDM_KEYSTORE): the APK is signed with the debug key - for testing only, it can NOT update a released BDM." }
    else { throw "No release key (BDM_KEYSTORE). A debug-signed APK can't update installed copies of BDM. Set up the key (see README, 'Updates'), or pass -AllowDebugSigning for a test build." }
    Remove-Item publish\android -Recurse -Force -ErrorAction SilentlyContinue
    dotnet publish src/Bdm.App -f net10.0-android -c Release `
        -p:ApplicationDisplayVersion=$Version -p:ApplicationVersion=$VersionCode -p:Version=$Version -o publish\android
    if ($LASTEXITCODE -ne 0) { throw "Android publish failed" }
    $apk = Get-ChildItem publish\android -Filter "*-Signed.apk" | Select-Object -First 1
    $out = "dist\BDM-$Version-android.apk"
    Copy-Item $apk.FullName $out -Force

    # Key rotation (one-time setup with tools\create-signing-lineage.ps1): BDM 2.0.0 was signed with the
    # debug key. Re-signing with the debug -> release lineage lets phones that still have 2.0.0
    # (Android 9+) update straight to this version, while 2.1+ installs keep updating as usual.
    $lineage = Join-Path $env:USERPROFILE "BDM-signing\bdm-lineage.bin"
    $debugKey = Join-Path $env:LOCALAPPDATA "Xamarin\Mono for Android\debug.keystore"
    if ($env:BDM_KEYSTORE -and (Test-Path $lineage) -and (Test-Path $debugKey)) {
        $sdk = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, "${env:ProgramFiles(x86)}\Android\android-sdk") | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
        $apksigner = Get-ChildItem "$sdk\build-tools\*\apksigner.bat" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
        if (-not $apksigner) { throw "apksigner not found under $sdk\build-tools (needed for key rotation)" }
        & $apksigner.FullName sign --ks $debugKey --ks-key-alias androiddebugkey --ks-pass pass:android `
            --next-signer --ks $env:BDM_KEYSTORE --ks-key-alias bdm --ks-pass "pass:$($env:BDM_KEYSTORE_PASSWORD.Trim())" `
            --lineage $lineage --rotation-min-sdk-version 28 $out
        if ($LASTEXITCODE -ne 0) { throw "APK key-rotation signing failed" }
        & $apksigner.FullName verify --print-certs $out | Select-String "Signer"
        Write-Host "APK signed with key rotation (updates BDM 2.0.0 and 2.1+)."
    }
}

Get-ChildItem dist -File | Where-Object Name -like "BDM-*" | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
