; Installer for the sfl command-line tool. Independent of SecureFolderLocker.iss (different AppId),
; so the desktop app and the CLI can be installed together or separately.
#define MyAppName "Secure Folder Locker CLI"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.1"
#endif
#define MyAppPublisher "ChamathDilshanC"
#define MyAppExeName "sfl.exe"
#define PublishDir "bin\cli-publish"

[Setup]
AppId={{99F6E81C-182D-4852-8E64-0D439575BC42}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
DefaultDirName={autopf}\SecureFolderLockerCLI
DisableProgramGroupPage=yes
DisableDirPage=auto
; Default: install for all users under Program Files and add the folder to the system PATH.
; `SfLCliSetup-<version>.exe /CURRENTUSER` installs for the current user only (no administrator rights).
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
ChangesEnvironment=yes
OutputDir=installer
OutputBaseFilename=SfLCliSetup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=SecureFolderLocker.ico

[Files]
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Messages]
FinishedLabel=Setup has installed [name] on your computer.%n%nOpen a NEW terminal window (Command Prompt, PowerShell or Windows Terminal) and run:%n%n    sfl --help%n%nTerminals that were already open do not see the new PATH entry.

[Code]
const
  MachineEnvironmentKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';
  UserEnvironmentKey = 'Environment';

function EnvironmentRoot: Integer;
begin
  if IsAdminInstallMode then
    Result := HKEY_LOCAL_MACHINE
  else
    Result := HKEY_CURRENT_USER;
end;

function EnvironmentKey: String;
begin
  if IsAdminInstallMode then
    Result := MachineEnvironmentKey
  else
    Result := UserEnvironmentKey;
end;

function TrimTrailingBackslash(const Value: String): String;
begin
  Result := Value;
  while (Length(Result) > 3) and (Result[Length(Result)] = '\') do
    Delete(Result, Length(Result), 1);
end;

function SamePathEntry(const A, B: String): Boolean;
begin
  Result := CompareText(TrimTrailingBackslash(Trim(A)), TrimTrailingBackslash(Trim(B))) = 0;
end;

{ True when the semicolon-separated list already contains Dir (ignoring case and a trailing backslash). }
function ListContains(const List, Dir: String): Boolean;
var
  Remaining, Entry: String;
  Separator: Integer;
begin
  Result := False;
  Remaining := List;
  while Remaining <> '' do
  begin
    Separator := Pos(';', Remaining);
    if Separator = 0 then
    begin
      Entry := Remaining;
      Remaining := '';
    end
    else
    begin
      Entry := Copy(Remaining, 1, Separator - 1);
      Remaining := Copy(Remaining, Separator + 1, MaxInt);
    end;

    if SamePathEntry(Entry, Dir) then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

{
  Removes every segment equal to Dir and leaves all other segments exactly as they were, including
  empty ones. AddToPath always appends ';' + Dir, so removing it restores the original value byte for byte.
}
function ListWithout(const List, Dir: String): String;
var
  Remaining, Entry: String;
  Separator: Integer;
  HasMore, First: Boolean;
begin
  Result := '';
  First := True;
  Remaining := List;
  HasMore := List <> '';
  while HasMore do
  begin
    Separator := Pos(';', Remaining);
    if Separator = 0 then
    begin
      Entry := Remaining;
      Remaining := '';
      HasMore := False;
    end
    else
    begin
      Entry := Copy(Remaining, 1, Separator - 1);
      Remaining := Copy(Remaining, Separator + 1, MaxInt);
    end;

    if not SamePathEntry(Entry, Dir) then
    begin
      if not First then
        Result := Result + ';';
      Result := Result + Entry;
      First := False;
    end;
  end;
end;

procedure AddToPath(const Dir: String);
var
  Current: String;
begin
  if not RegQueryStringValue(EnvironmentRoot, EnvironmentKey, 'Path', Current) then
    Current := '';

  if ListContains(Current, Dir) then
    Exit;

  { Path must stay REG_EXPAND_SZ so entries such as %SystemRoot% keep expanding. }
  if Current = '' then
    RegWriteExpandStringValue(EnvironmentRoot, EnvironmentKey, 'Path', Dir)
  else
    RegWriteExpandStringValue(EnvironmentRoot, EnvironmentKey, 'Path', Current + ';' + Dir);
end;

procedure RemoveFromPath(const Dir: String);
var
  Current: String;
begin
  if not RegQueryStringValue(EnvironmentRoot, EnvironmentKey, 'Path', Current) then
    Exit;

  if ListContains(Current, Dir) then
    RegWriteExpandStringValue(EnvironmentRoot, EnvironmentKey, 'Path', ListWithout(Current, Dir));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    AddToPath(ExpandConstant('{app}'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    RemoveFromPath(ExpandConstant('{app}'));
end;
