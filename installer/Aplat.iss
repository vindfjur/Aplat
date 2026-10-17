#if VER < 0x06030000
  #error Inno Setup 6.3 or newer is required
#endif
#ifndef AppVersion
  #error AppVersion must be supplied by build-installer.ps1
#endif
#ifndef PayloadDir
  #error PayloadDir must be supplied by build-installer.ps1
#endif
#ifndef InstallerOutput
  #error InstallerOutput must be supplied by build-installer.ps1
#endif

[Setup]
AppId={{71420755-9CFA-4BDD-BFF2-89117F59F81C}
AppName=Aplat
AppVersion={#AppVersion}
AppPublisher=vindfjur
AppPublisherURL=https://github.com/vindfjur/Aplat
AppSupportURL=https://github.com/vindfjur/Aplat/issues
DefaultDirName={localappdata}\Programs\vindfjur\Aplat
DefaultGroupName=Aplat
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ChangesAssociations=yes
MinVersion=10.0
WizardStyle=modern
SetupIconFile=..\icon.ico
UninstallDisplayIcon={app}\Aplat.exe
LicenseFile=..\LICENSE.txt
OutputDir={#InstallerOutput}
OutputBaseFilename=Aplat-{#AppVersion}-Setup
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
VersionInfoVersion={#AppVersion}
VersionInfoCompany=vindfjur
VersionInfoDescription=Aplat Installer
VersionInfoCopyright=Copyright (c) 2026 vindfjur

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

; Explicit allowlist. Never use wildcards or recursesubdirs here.
[Files]
Source: "{#PayloadDir}\Aplat.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\Aplat.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\OpenTK.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\OpenTK.GLControl.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD_PARTY_NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "OpenTK-LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Registry]
; Per-user file type: double click on a .aplat opens it in Aplat (current user only, no administrator rights).
Root: HKCU; Subkey: "Software\Classes\.aplat"; ValueType: string; ValueName: ""; ValueData: "Aplat.Project"; Flags: uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\Aplat.Project"; ValueType: string; ValueName: ""; ValueData: "Aplat project"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Aplat.Project\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\Aplat.exe,0"
Root: HKCU; Subkey: "Software\Classes\Aplat.Project\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Aplat.exe"" ""%1"""

[Icons]
Name: "{autoprograms}\Aplat"; Filename: "{app}\Aplat.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Aplat"; Filename: "{app}\Aplat.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Aplat.exe"; Description: "{cm:LaunchProgram,Aplat}"; Flags: nowait postinstall skipifsilent unchecked

[CustomMessages]
russian.DotNetRequired=Для Aplat необходим .NET Framework 4.7.2 или новее. Установите его с сайта Microsoft https://dotnet.microsoft.com/download/dotnet-framework и повторите установку. Файлы программы не установлены.
english.DotNetRequired=Aplat requires .NET Framework 4.7.2 or newer. Install it from Microsoft at https://dotnet.microsoft.com/download/dotnet-framework and run Setup again. No application files have been installed.
russian.NewerVersion=Уже установлена более новая версия Aplat. Установка старой версии отменена.
english.NewerVersion=A newer version of Aplat is already installed. Downgrade cancelled.

[Code]
function InitializeSetup: Boolean;
begin
  Result := IsDotNetInstalled(net472, 0);
  if not Result then
    SuppressibleMsgBox(CustomMessage('DotNetRequired'), mbError, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExistingMS, ExistingLS, SetupMS, SetupLS: Cardinal;
begin
  Result := '';
  if GetVersionNumbers(ExpandConstant('{app}\Aplat.exe'), ExistingMS, ExistingLS) and
     GetVersionNumbers(ExpandConstant('{srcexe}'), SetupMS, SetupLS) then
    if (ExistingMS > SetupMS) or ((ExistingMS = SetupMS) and (ExistingLS > SetupLS)) then
      Result := CustomMessage('NewerVersion');
end;
