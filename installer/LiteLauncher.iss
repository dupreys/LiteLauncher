; Inno Setup 6 script. Build with:  ISCC.exe installer\LiteLauncher.iss   (or run build\publish.ps1)
; Installs per user (no administrator prompt), so the registry options below apply to YOUR Windows account.

#define AppName "LiteLauncher"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe "LiteLauncher.exe"

[Setup]
AppId={{14322CF8-D840-4C08-97D8-E9E7CFF71140}
AppName={#AppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
OutputDir=..\dist
OutputBaseFilename=LiteLauncher-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
SetupIconFile=..\src\LiteLauncher.App\Assets\LiteLauncher.ico
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"
Name: "startup"; Description: "Start LiteLauncher full screen when I sign in to Windows"; GroupDescription: "Console mode:"; Flags: unchecked
Name: "xboxmode"; Description: "Turn on Windows Xbox mode (Windows 11 24H2 or later). Note: Windows will use the Xbox app as its home screen."; GroupDescription: "Console mode:"; Flags: unchecked; Check: XboxModeAvailable

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autoprograms}\{#AppName} (full screen)"; Filename: "{app}\{#AppExe}"; Parameters: "--fullscreen"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Start with Windows (current user), full screen
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; \
    ValueData: """{app}\{#AppExe}"" --fullscreen"; Tasks: startup; Flags: uninsdeletevalue
; Xbox mode on: the value Windows' own "Enable Xbox mode" switch writes (Settings > Gaming > Xbox mode).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\GamingConfiguration"; ValueType: string; \
    ValueName: "GamingHomeApp"; ValueData: "Microsoft.GamingApp_8wekyb3d8bbwe!Microsoft.Xbox.App"; Tasks: xboxmode

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
// Offer "Xbox mode" only on Windows 11 24H2 (build 26100) or later, and only when no home app is chosen yet:
// if Xbox mode is already on (the Xbox app, or LiteLauncher installed as the home app), leave the choice alone.
function XboxModeAvailable: Boolean;
var
  Version: TWindowsVersion;
  HomeApp: String;
begin
  GetWindowsVersionEx(Version);
  Result := (Version.Major = 10) and (Version.Build >= 26100);
  if Result and RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\GamingConfiguration', 'GamingHomeApp', HomeApp) then
    Result := Trim(HomeApp) = '';
end;
