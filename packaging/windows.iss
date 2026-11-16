#ifndef AppVersion
#define AppVersion "1.5.0"
#endif
#ifndef PackageDir
#define PackageDir "..\dist\package"
#endif
[Setup]
AppId={{D9F06C1E-7714-4C6A-A5DE-315BD6D5AB54}
AppName=Anthropic-Proxy-RS-Win
AppVersion={#AppVersion}
AppPublisher=Silviu Vlasceanu
AppPublisherURL=https://github.com/silviuk/Anthropic-Proxy-RS-Win
AppSupportURL=https://github.com/silviuk/Anthropic-Proxy-RS-Win/issues
AppUpdatesURL=https://github.com/silviuk/Anthropic-Proxy-RS-Win/releases
DefaultDirName={localappdata}\Programs\Anthropic-Proxy-RS-Win
DefaultGroupName=Anthropic-Proxy-RS-Win
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
WizardStyle=modern dynamic
OutputDir=..\dist
OutputBaseFilename=Anthropic-Proxy-RS-Win-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
UninstallDisplayName=Anthropic-Proxy-RS-Win
UninstallDisplayIcon={app}\Anthropic-Proxy-RS-Win.exe
LicenseFile=..\LICENSE
CloseApplications=yes
RestartApplications=no
CloseApplicationsFilter=Anthropic-Proxy-RS-Win.exe,anthropic-proxy.exe
SetupLogging=yes
[Files]
Source: "{#PackageDir}\Anthropic-Proxy-RS-Win.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\Anthropic-Proxy-RS-Win.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\anthropic-proxy.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\WINDOWS.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PackageDir}\proxy.env.example"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\Anthropic-Proxy-RS-Win"; Filename: "{app}\Anthropic-Proxy-RS-Win.exe"
Name: "{group}\Uninstall Anthropic-Proxy-RS-Win"; Filename: "{uninstallexe}"
[Run]
Filename: "{app}\Anthropic-Proxy-RS-Win.exe"; Description: "Open Anthropic-Proxy-RS-Win"; Flags: nowait postinstall skipifsilent
[UninstallRun]
Filename: "{app}\Anthropic-Proxy-RS-Win.exe"; Parameters: "--uninstall"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: StopProxy
[Code]
function InitializeSetup(): Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    MsgBox('.NET Framework 4.8 is required. Install it from Microsoft, then run Setup again.', mbError, MB_OK);
end;
