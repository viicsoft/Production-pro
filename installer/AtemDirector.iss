; AtemDirector Installer Script for Inno Setup
; Creates a Windows installer with all dependencies bundled

#define MyAppName "AtemDirector"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "VIICSOFT"
#define MyAppURL "https://viicsoft.com"
#define MyAppExeName "desktop\desktop.exe"

[Setup]
; Basic Information
AppId={{8F9A2B3C-4D5E-6F7A-8B9C-0D1E2F3A4B5C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Installation Directories
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Output
OutputDir=..\installer-output
OutputBaseFilename=AtemDirector-Setup-{#MyAppVersion}
Compression=lzma2/fast
SolidCompression=yes

; Privileges
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog

; Process management during install
CloseApplications=no
RestartApplications=no

; UI
WizardStyle=modern

; Architecture
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Registration scripts
Source: "RegisterATEM.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "UnregisterATEM.bat"; DestDir: "{app}"; Flags: ignoreversion

; Caddy
Source: "..\publish\caddy\*"; DestDir: "{app}\caddy"; Flags: ignoreversion recursesubdirs createallsubdirs

; Server
Source: "..\publish\server\*"; DestDir: "{app}\server"; Flags: ignoreversion recursesubdirs createallsubdirs

; Desktop
Source: "..\publish\desktop\*"; DestDir: "{app}\desktop"; Flags: ignoreversion recursesubdirs createallsubdirs

; Documentation (optional)
; Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Dirs]
Name: "{app}"; Permissions: users-modify
Name: "{app}\caddy"; Permissions: users-modify
Name: "{app}\server"; Permissions: users-modify
Name: "{app}\desktop"; Permissions: users-modify

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\desktop\desktop.exe"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\desktop\desktop.exe"; Tasks: desktopicon

[Run]
; Register ATEM SDK COM DLL
Filename: "{app}\RegisterATEM.bat"; Parameters: """{app}\"""; Flags: runhidden waituntilterminated
; Firewall rules
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""AtemDirector Caddy"" dir=in action=allow program=""{app}\caddy\caddy.exe"" enable=yes"; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""AtemDirector Server"" dir=in action=allow program=""{app}\server\server.exe"" enable=yes"; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""AtemDirector (HTTP)"" dir=in action=allow protocol=TCP localport=80 profile=any"; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""AtemDirector (HTTPS)"" dir=in action=allow protocol=TCP localport=8443 profile=any"; Flags: runhidden
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Unregister ATEM SDK COM DLL
Filename: "{app}\UnregisterATEM.bat"; Parameters: """{app}\"""; Flags: runhidden waituntilterminated
; Remove firewall rules
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AtemDirector Caddy"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AtemDirector Server"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AtemDirector (HTTP)"""; Flags: runhidden
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall delete rule name=""AtemDirector (HTTPS)"""; Flags: runhidden

[Code]
function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM desktop.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
  Exec('taskkill.exe', '/F /IM AtemDirector.Desktop.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
  Exec('taskkill.exe', '/F /IM server.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
  Exec('taskkill.exe', '/F /IM AtemDirector.Server.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
  Exec('taskkill.exe', '/F /IM caddy.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
  Result := True;
end;

function InitializeUninstall(): Boolean;
var
  ErrorCode: Integer;
begin
  Exec('taskkill.exe', '/F /IM desktop.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
  Exec('taskkill.exe', '/F /IM AtemDirector.Desktop.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
  Exec('taskkill.exe', '/F /IM server.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
  Exec('taskkill.exe', '/F /IM AtemDirector.Server.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
  Exec('taskkill.exe', '/F /IM caddy.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    // Create initial config file
    SaveStringToFile(ExpandConstant('{app}\config.json'), 
                     '{"lastIP": "", "autoStart": false}', 
                     False);
  end;
end;
