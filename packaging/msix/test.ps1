$ErrorActionPreference = 'Stop'
$identity = Get-Content packaging/msix/identity.json | ConvertFrom-Json
$version = (Select-String '^version = "([^"]+)"' Cargo.toml | Select-Object -First 1).Matches.Groups[1].Value
$unsigned = (Resolve-Path "dist/Anthropic-Proxy-RS-Win-$version-x64.msix").Path
$test = Join-Path $env:RUNNER_TEMP 'Anthropic-Proxy-RS-Win-test.msix'
Copy-Item $unsigned $test
# The temporary certificate is only for this disposable validation machine.
# The unsigned Store package is retained separately and no private key is uploaded.
$certificate = New-SelfSignedCertificate -Type Custom -Subject $identity.Publisher -KeyUsage DigitalSignature -FriendlyName 'Anthropic Proxy CI only' -CertStoreLocation 'Cert:\CurrentUser\My' -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddDays(1) -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3','2.5.29.19={text}')
$certificateFile = Join-Path $env:RUNNER_TEMP 'anthropic-proxy-test.cer'
Export-Certificate -Cert $certificate -FilePath $certificateFile | Out-Null
Import-Certificate -FilePath $certificateFile -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null
$sdk = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits/10/bin'
$sign = Get-ChildItem $sdk -Filter signtool.exe -Recurse | Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
try {
    & $sign.FullName sign /fd SHA256 /sha1 $certificate.Thumbprint $test
    if ($LASTEXITCODE -ne 0) { throw 'Test-only package signing failed.' }
    Add-AppxPackage -Path $test
    $package = Get-AppxPackage -Name $identity.Name
    if (-not $package -or $package.PackageFamilyName -ne $identity.PackageFamilyName) { throw 'Installed package identity mismatch.' }
    if ($package.Version -ne "$version.0") { throw 'Installed MSIX version mismatch.' }
    foreach ($file in @('Anthropic-Proxy-RS-Win.exe','ProxyStartup.exe','StartupControl.dll','anthropic-proxy.exe','Assets/icon-150.png')) {
        if (-not (Test-Path (Join-Path $package.InstallLocation $file))) { throw "MSIX is missing $file" }
    }
    # Launch the installed app through the shell, as the Store and Start menu do.
    Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class StoreWindow {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern int GetClassName(IntPtr window, StringBuilder name, int capacity);
    public static bool IsSettingsWindow(IntPtr window) {
        var name = new StringBuilder(256);
        GetClassName(window, name, name.Capacity);
        return name.ToString().StartsWith("WindowsForms10.Window.", StringComparison.Ordinal);
    }
}
'@
    Start-Process explorer.exe -ArgumentList "shell:AppsFolder\$($identity.PackageFamilyName)!App"
    $deadline = (Get-Date).AddSeconds(30)
    $window = $null
    do {
        $window = Get-Process -Name 'Anthropic-Proxy-RS-Win' -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowTitle -eq 'Anthropic-Proxy-RS-Win' -and [StoreWindow]::IsSettingsWindow($_.MainWindowHandle) } |
            Select-Object -First 1
        if (-not $window) { Start-Sleep -Milliseconds 200 }
    } until ($window -or (Get-Date) -ge $deadline)
    if (-not $window) { throw 'Opening the installed app did not show the settings window.' }
    Stop-Process -Id $window.Id
    if (-not $window.WaitForExit(5000)) { throw 'The first-launch test process did not exit.' }

    # Exercise the startup API in the installed package.
    $output = (Resolve-Path dist/ui-test).Path
    Invoke-CommandInDesktopPackage -PackageFamilyName $identity.PackageFamilyName -AppId App -Command (Join-Path $package.InstallLocation 'Anthropic-Proxy-RS-Win.exe') -Args "--package-self-test `"$output`"" -PreventBreakaway
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path dist/ui-test/msix-runtime-result.txt) -and -not (Test-Path dist/ui-test/desktop-test-error.txt) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    if (-not (Test-Path dist/ui-test/msix-runtime-result.txt)) {
        Get-Content dist/ui-test/desktop-test-error.txt -ErrorAction SilentlyContinue
        throw 'Packaged runtime test did not complete.'
    }
    Get-Content dist/ui-test/msix-runtime-result.txt
    Remove-AppxPackage -Package $package.PackageFullName
    if (Get-AppxPackage -Name $identity.Name) { throw 'MSIX uninstall did not finish.' }
    'PASS: unsigned Store identity validation, test-signed install, shell first launch, packaged startup API, installed files and removal.' | Set-Content dist/ui-test/msix-install-result.txt
} finally {
    Get-AppxPackage -Name $identity.Name | Remove-AppxPackage -ErrorAction SilentlyContinue
    Remove-Item "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -ErrorAction SilentlyContinue
    Remove-Item "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)" -ErrorAction SilentlyContinue
}
