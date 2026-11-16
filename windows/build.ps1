param([string]$OutputDirectory = 'dist/package')
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ "/out:$OutputDirectory/anthropic-proxy-ui.exe" `
    /win32manifest:windows/app.manifest /reference:System.dll /reference:System.Core.dll `
    /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Security.dll `
    /reference:System.Web.Extensions.dll windows/ProxyDesktop.cs windows/DesktopTests.cs
if ($LASTEXITCODE -ne 0) { throw 'Windows settings UI compilation failed.' }
Copy-Item windows/anthropic-proxy-ui.exe.config $OutputDirectory/
