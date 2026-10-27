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

[Run]
Filename: "{app}\{#AppExe}"; Description: "Start {#AppName}"; Flags: nowait postinstall skipifsilent
