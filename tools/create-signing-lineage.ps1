# One-time setup: lets phones that still have BDM 2.0.0 (signed with the Android debug key) update in place
# to BDM 2.1+ (signed with the BitProSoftTech release key), instead of "App not installed".
#
# It writes %USERPROFILE%\BDM-signing\bdm-lineage.bin, a signed proof that the release key replaces the
# debug key that built 2.0.0. build-installer.ps1 uses it automatically when it exists. Works on Android 9+.
#
# Needs: the SAME debug.keystore that signed 2.0.0 (default: %LOCALAPPDATA%\Xamarin\Mono for Android\debug.keystore
# on the PC that built it), the release key in %USERPROFILE%\BDM-signing, and Android SDK build-tools (apksigner).
# Keep bdm-lineage.bin and both keystores private and backed up; never commit them.
param(
    [string]$DebugKeystore = (Join-Path $env:LOCALAPPDATA "Xamarin\Mono for Android\debug.keystore"),
    [string]$ReleaseKeystore = (Join-Path $env:USERPROFILE "BDM-signing\bdm-release.keystore"),
    [string]$PasswordFile = (Join-Path $env:USERPROFILE "BDM-signing\keystore-password.txt")
)
$ErrorActionPreference = "Stop"
foreach ($f in $DebugKeystore, $ReleaseKeystore, $PasswordFile) { if (-not (Test-Path $f)) { throw "Not found: $f" } }

# The debug key must be the one that signed 2.0.0 (SHA-256 B7:D0:12:5D:...:06:B0).
$expected = "B7:D0:12:5D:C2:69:5E:97:42:AA:7A:C4:2C:4B:FD:5A:0A:5D:E6:38:CC:60:30:A8:16:B7:99:BF:CE:5E:06:B0"
$keytool = (Get-Command keytool -ErrorAction SilentlyContinue).Source
if (-not $keytool) { $keytool = Get-ChildItem "$env:ProgramFiles\Android\openjdk\*\bin\keytool.exe" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName }
if (-not $keytool) { throw "keytool not found (it comes with the JDK)" }
$found = ((& $keytool -list -v -keystore $DebugKeystore -alias androiddebugkey -storepass android) | Select-String 'SHA256:\s*([0-9A-F:]+)').Matches[0].Groups[1].Value
if ($found -ne $expected) { throw "This debug.keystore did not sign BDM 2.0.0 (found $found). Use the one from the PC that built 2.0.0." }

$sdk = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, "${env:ProgramFiles(x86)}\Android\android-sdk") | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
$apksigner = Get-ChildItem "$sdk\build-tools\*\apksigner.bat" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $apksigner) { throw "apksigner not found under $sdk\build-tools" }

$out = Join-Path (Split-Path $ReleaseKeystore) "bdm-lineage.bin"
$pass = (Get-Content $PasswordFile -Raw).Trim()
& $apksigner.FullName rotate --out $out `
    --old-signer --ks $DebugKeystore --ks-key-alias androiddebugkey --ks-pass pass:android `
    --new-signer --ks $ReleaseKeystore --ks-key-alias bdm --ks-pass "pass:$pass"
if ($LASTEXITCODE -ne 0) { throw "apksigner rotate failed" }
Write-Host "Created $out. Back it up next to the release key. build-installer.ps1 will now sign APKs with key rotation."
