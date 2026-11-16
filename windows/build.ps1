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
if ($MSIX) {
    $programFiles = [Environment]::GetFolderPath('ProgramFilesX86')
    $metadata = Get-ChildItem (Join-Path $programFiles 'Windows Kits/10/UnionMetadata') -Filter Windows.winmd -Recurse | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $metadata) { throw 'Windows SDK metadata is required for the MSIX build.' }
    $arguments += @('/define:MSIX_BUILD', "/reference:$($metadata.FullName)")
    $framework = Join-Path $programFiles 'Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8'
    foreach ($name in @('System.Runtime.WindowsRuntime.dll', 'System.Runtime.dll', 'System.Threading.Tasks.dll', 'System.Runtime.InteropServices.WindowsRuntime.dll')) {
        $reference = Get-ChildItem $framework -Filter $name -Recurse | Select-Object -First 1
        if (-not $reference) { throw "Missing framework reference: $name" }
        $arguments += "/reference:$($reference.FullName)"
    }
}
& $compiler @arguments @sources
if ($LASTEXITCODE -ne 0) { throw 'Windows settings UI compilation failed.' }
Copy-Item (Join-Path $PSScriptRoot 'Anthropic-Proxy-RS-Win.exe.config') $OutputDirectory
if ($MSIX) {
    & $compiler /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ "/out:$OutputDirectory/ProxyStartup.exe" "/win32icon:$icon" (Join-Path $PSScriptRoot 'StoreStartup.cs')
    if ($LASTEXITCODE -ne 0) { throw 'MSIX startup launcher compilation failed.' }
}
