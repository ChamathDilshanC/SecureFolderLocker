#define MyAppName "Secure Folder Locker"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "ChamathDilshanC"
#define MyAppExeName "SecureFolderLocker.exe"
#define PublishDir "bin\Release\net8.0-windows\publish"

[Setup]
AppId={{B7E4D9E2-6E0F-4AA7-9C98-7E2A5B5C1D40}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\SecureFolderLocker
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
OutputDir=installer
OutputBaseFilename=SecureFolderLockerSetup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=SecureFolderLocker.ico

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"

[Registry]
; Right-click a .zip -> "Extract with Secure Folder Locker" opens the terminal extractor.
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\SecureFolderLocker.Extract"; ValueType: string; ValueName: ""; ValueData: "Extract with {#MyAppName}"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\SecureFolderLocker.Extract"; ValueType: string; ValueName: "Icon"; ValueData: """{app}\{#MyAppExeName}"""
Root: HKLM; Subkey: "Software\Classes\SystemFileAssociations\.zip\shell\SecureFolderLocker.Extract\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" --extract ""%1"""

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: shellexec nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
