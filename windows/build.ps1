param([string]$OutputDirectory = 'dist/package')
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$sources = @((Join-Path $PSScriptRoot 'ProxyDesktop.cs'), (Join-Path $PSScriptRoot 'DesktopTests.cs'))
$destination = Join-Path $OutputDirectory 'anthropic-proxy-ui.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ "/out:$destination" `
    "/win32manifest:$manifest" /reference:System.dll /reference:System.Core.dll `
    /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Security.dll `
    /reference:System.Web.Extensions.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Windows settings UI compilation failed.' }
Copy-Item (Join-Path $PSScriptRoot 'anthropic-proxy-ui.exe.config') $OutputDirectory
