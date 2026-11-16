$ErrorActionPreference = 'Stop'
$compilerSetup = Join-Path $env:RUNNER_TEMP 'innosetup-7.1.1-x64.exe'
Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_1/innosetup-7.1.1-x64.exe' -OutFile $compilerSetup
$hash = (Get-FileHash $compilerSetup -Algorithm SHA256).Hash.ToLower()
if ($hash -ne '8a5a6711c3766a6e5fb74f280ba0e3551e719ce94538d901fdbd1f8f9920b3f0') { throw 'Installer compiler checksum mismatch.' }
$toolDirectory = Join-Path $env:RUNNER_TEMP 'inno-setup'
$process = Start-Process $compilerSetup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$toolDirectory`"") -Wait -PassThru
if ($process.ExitCode -ne 0) { throw 'Installer compiler installation failed.' }
$version = (Select-String '^version = "([^"]+)"' Cargo.toml | Select-Object -First 1).Matches.Groups[1].Value
$package = (Resolve-Path dist/package).Path
& (Join-Path $toolDirectory 'ISCC.exe') "/DAppVersion=$version" "/DPackageDir=$package" packaging/windows.iss
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
