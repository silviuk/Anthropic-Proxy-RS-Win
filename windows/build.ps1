param([string]$OutputDirectory = 'dist/package', [switch]$MSIX)
$ErrorActionPreference = 'Stop'
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$sources = @('ProxyDesktop.cs', 'DesktopTests.cs', 'Profiles.cs', 'Dialogs.cs', 'PackagedStartup.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$destination = Join-Path $OutputDirectory 'Anthropic-Proxy-RS-Win.exe'
$icon = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../assets/icon.ico'))
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/warnaserror+', "/out:$destination", "/win32manifest:$manifest", "/win32icon:$icon",
    '/reference:System.dll', '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll',
    '/reference:System.Security.dll', '/reference:System.Web.Extensions.dll')
if ($MSIX) { $arguments += '/define:MSIX_BUILD' }
& $compiler @arguments @sources
if ($LASTEXITCODE -ne 0) { throw 'Windows settings UI compilation failed.' }
Copy-Item (Join-Path $PSScriptRoot 'Anthropic-Proxy-RS-Win.exe.config') $OutputDirectory
if ($MSIX) {
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ "/out:$OutputDirectory/ProxyStartup.exe" "/win32icon:$icon" (Join-Path $PSScriptRoot 'StoreStartup.cs')
    if ($LASTEXITCODE -ne 0) { throw 'MSIX startup launcher compilation failed.' }
}

if ($MSIX) {
    $programFiles = [Environment]::GetFolderPath('ProgramFilesX86')
    $vswhere = Join-Path $programFiles 'Microsoft Visual Studio/Installer/vswhere.exe'
    $vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $vs) { throw 'Visual C++ build tools are required for packaged startup control.' }
    $environment = Join-Path $vs 'Common7/Tools/VsDevCmd.bat'
    $source = Join-Path $PSScriptRoot 'StartupControl.cpp'
    $target = Join-Path $OutputDirectory 'StartupControl.dll'
    $object = Join-Path $OutputDirectory 'StartupControl.obj'
    $importLibrary = Join-Path $OutputDirectory 'StartupControl.lib'
    $exports = Join-Path $OutputDirectory 'StartupControl.exp'
    $buildScript = Join-Path $OutputDirectory 'build-startup.cmd'
    [IO.File]::WriteAllLines($buildScript, @(
        '@echo off',
        "call `"$environment`" -no_logo -arch=x64",
        'if errorlevel 1 exit /b %errorlevel%',
        "cl.exe /nologo /std:c++20 /EHsc /MT /O2 /LD `"$source`" /Fe:`"$target`" /Fo:`"$object`" /link /IMPLIB:`"$importLibrary`" windowsapp.lib",
        'exit /b %errorlevel%'
    ))
    try {
        & $env:ComSpec /d /c $buildScript
        if ($LASTEXITCODE -ne 0) { throw 'Packaged startup control compilation failed.' }
    } finally {
        Remove-Item $object, $importLibrary, $exports, $buildScript -ErrorAction SilentlyContinue
    }
}
