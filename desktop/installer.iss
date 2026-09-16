#define MyAppName "Vidikom"
#define MyAppVersion "1.0.20"
#define MyAppPublisher "Vidikom"
#define MyAppExeName "Desktop.exe"

[Setup]
; NOTE: The value of AppId uniquely identifies this application. Do not use the same AppId value in installers for other applications.
; (To generate a new GUID, click Tools | Generate GUID inside the IDE.)
AppId={{5A4C2E9A-8F12-4D34-9A4B-B8F12A7C2D3E}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
SetupIconFile=icon.ico
; Remove the following line to run in administrative install mode (install for all users.)
PrivilegesRequired=admin
OutputDir=output
OutputBaseFilename=VidikomSetup_v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The publish directory containing the compiled .NET self-contained app
Source: "bin\Release\net9.0-windows\win-x64\publish\lib\BMDSwitcherAPI64.dll"; DestDir: "{app}\lib"; Flags: ignoreversion regserver 64bit
Source: "bin\Release\net9.0-windows\win-x64\publish\*"; DestDir: "{app}"; Excludes: "webview2_data\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
// Add firewall rule during installation
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    // Add inbound firewall rule for the application
    Exec('netsh', 'advfirewall firewall add rule name="Vidikom PWA Server" dir=in action=allow program="' + ExpandConstant('{app}\{#MyAppExeName}') + '" enable=yes profile=any', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

// Remove firewall rule during uninstallation
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec('netsh', 'advfirewall firewall delete rule name="Vidikom PWA Server" program="' + ExpandConstant('{app}\{#MyAppExeName}') + '"', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;












