; Jumpzys Vortex Inno Setup script
; Build publish output first with build_release.bat, then compile this script in Inno Setup.

#define MyAppName "Jumpzys Vortex"
#define MyAppVersion "2.2.0"
#define MyAppExeName "JumpzysVortex.exe"

[Setup]
AppId={{7D53AB8F-4D75-4E1A-8F57-JUMPZYSVORTEX}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=installer
OutputBaseFilename=JumpzysVortex_Setup_{#MyAppVersion}
Compression=lzma
SolidCompression=yes
PrivilegesRequired=admin

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
