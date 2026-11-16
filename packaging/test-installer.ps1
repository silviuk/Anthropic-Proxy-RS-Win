$ErrorActionPreference = 'Stop'
$version = (Select-String '^version = "([^"]+)"' Cargo.toml | Select-Object -First 1).Matches.Groups[1].Value
$setup = (Resolve-Path "dist/Anthropic-Proxy-RS-Win-$version-Setup.exe").Path
$output = (Resolve-Path dist/ui-test).Path
$preview = Start-Process dist/package/Anthropic-Proxy-RS-Win.exe -ArgumentList @('--installer-preview', "`"$setup`"", "`"$output`"") -PassThru
if (-not $preview.WaitForExit(60000)) { Stop-Process -Id $preview.Id -Force; throw 'Installer appearance test timed out.' }
if ($preview.ExitCode -ne 0) { Get-Content dist/ui-test/desktop-test-error.txt -ErrorAction SilentlyContinue; throw 'Installer appearance test failed.' }
$directory = Join-Path $env:RUNNER_TEMP 'Proxy installation test'
$install = Start-Process $setup -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/DIR=`"$directory`"", "/LOG=`"$env:RUNNER_TEMP\proxy-install.log`"") -Wait -PassThru
if ($install.ExitCode -ne 0) { throw "Silent install failed: $($install.ExitCode)" }
foreach ($name in @('Anthropic-Proxy-RS-Win.exe', 'Anthropic-Proxy-RS-Win.exe.config', 'anthropic-proxy.exe', 'unins000.exe')) {
    if (-not (Test-Path (Join-Path $directory $name))) { throw "Missing installed file: $name" }
}
& (Join-Path $directory 'anthropic-proxy.exe') --version
if ($LASTEXITCODE -ne 0) { throw 'Installed proxy did not start.' }
$arp = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{D9F06C1E-7714-4C6A-A5DE-315BD6D5AB54}_is1'
$entry = Get-ItemProperty $arp
if ($entry.DisplayName -ne 'Anthropic-Proxy-RS-Win' -or $entry.DisplayVersion -ne $version) { throw 'Installer registration mismatch.' }
$uninstall = Start-Process (Join-Path $directory 'unins000.exe') -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait -PassThru
if ($uninstall.ExitCode -ne 0) { throw 'Silent uninstall failed.' }
if (Test-Path (Join-Path $directory 'Anthropic-Proxy-RS-Win.exe')) { throw 'Uninstall left the application executable.' }
'PASS: per-user silent install, installed binary, registered version and silent uninstall.' | Set-Content dist/ui-test/installer-test-result.txt
