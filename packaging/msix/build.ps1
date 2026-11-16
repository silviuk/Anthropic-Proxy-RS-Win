$ErrorActionPreference = 'Stop'
$package = 'dist/msix/staging'
./windows/build.ps1 -OutputDirectory $package -MSIX
Copy-Item target/x86_64-pc-windows-msvc/release/anthropic-proxy.exe $package/
Copy-Item LICENSE, WINDOWS.md, PRIVACY.md $package/
New-Item -ItemType Directory -Path "$package/Assets" -Force | Out-Null
Copy-Item assets/icon-*.png "$package/Assets/"
Copy-Item packaging/msix/AppxManifest.xml "$package/AppxManifest.xml"
$identity = Get-Content packaging/msix/identity.json | ConvertFrom-Json
[xml]$manifest = Get-Content "$package/AppxManifest.xml"
if ($manifest.Package.Identity.Name -ne $identity.Name -or $manifest.Package.Identity.Publisher -ne $identity.Publisher) { throw 'Store identity mismatch.' }
$version = (Select-String '^version = "([^"]+)"' Cargo.toml | Select-Object -First 1).Matches.Groups[1].Value
$manifest.Package.Identity.Version = "$version.0"
$manifest.Save([IO.Path]::GetFullPath("$package/AppxManifest.xml"))
$sdk = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Windows Kits/10/bin'
$makeappx = Get-ChildItem $sdk -Filter makeappx.exe -Recurse | Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw 'Windows SDK MakeAppx is required.' }
& $makeappx.FullName pack /d $package /p "dist/Anthropic-Proxy-RS-Win-$version-x64.msix" /o
if ($LASTEXITCODE -ne 0) { throw 'MSIX package validation failed.' }
