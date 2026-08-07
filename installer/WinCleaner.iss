; WinCleaner Inno Setup script
; Installs the app and downloads missing runtimes during setup:
;   - .NET 8 Desktop Runtime (x64)
;   - Visual C++ 2015-2022 Redistributable (x64)
;
; Build:  powershell -File installer\build-installer.ps1
; Or:     ISCC.exe installer\WinCleaner.iss

#define MyAppName "WinCleaner"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "emirttac"
#define MyAppURL "https://github.com/emirttac/WinCleaner"
#define MyAppExeName "WinCleaner.exe"
#define MyAppId "{{B7E3C9A1-4F2D-4A8E-9C11-8D6F2E5A1B0C}"

#ifndef PublishDir
  #define PublishDir "..\publish\installer"
#endif

#ifndef OutputDir
  #define OutputDir "..\publish\setup"
#endif

[Setup]
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=WinCleaner-Setup-{#MyAppVersion}
SetupIconFile=app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoCopyright=Copyright (c) {#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
; No Authenticode company certificate available.
; Publisher metadata uses GitHub identity (emirttac).
; When you obtain a code-signing cert, enable:
;   SignTool=signtool
;   SignedUninstaller=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent shellexec runascurrentuser

[Code]
var
  DownloadPage: TDownloadWizardPage;
  NeedDotNet: Boolean;
  NeedVcRedist: Boolean;

function HasDotNet8Desktop: Boolean;
var
  FindRec: TFindRec;
  Base: String;
begin
  Result := False;
  Base := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App');
  if not DirExists(Base) then
    Exit;
  if FindFirst(Base + '\8.*', FindRec) then
  begin
    try
      repeat
        if ((FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0) and
           (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          Result := True;
          Break;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function IsVcRedistInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64') or
            RegKeyExists(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64') or
            RegKeyExists(HKLM64, 'SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64');
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if Progress = ProgressMax then
    Log(Format('Downloaded %s (%d bytes)', [FileName, Progress]))
  else if ProgressMax <> 0 then
    Log(Format('Downloading %s: %d of %d', [FileName, Progress, ProgressMax]));
  Result := True;
end;

function InitializeSetup: Boolean;
begin
  NeedDotNet := not HasDotNet8Desktop;
  NeedVcRedist := not IsVcRedistInstalled;
  Result := True;
end;

procedure InitializeWizard;
begin
  DownloadPage := CreateDownloadPage(
    SetupMessage(msgWizardPreparing),
    SetupMessage(msgPreparingDesc),
    @OnDownloadProgress);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  ResultCode: Integer;
  DotNetPath: String;
  VcPath: String;
begin
  Result := True;
  if CurPageID <> wpReady then
    Exit;

  if (not NeedDotNet) and (not NeedVcRedist) then
    Exit;

  DownloadPage.Clear;
  if NeedDotNet then
    DownloadPage.Add(
      'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe',
      'windowsdesktop-runtime-8.0-win-x64.exe', '');
  if NeedVcRedist then
    DownloadPage.Add(
      'https://aka.ms/vs/17/release/vc_redist.x64.exe',
      'vc_redist.x64.exe', '');

  DownloadPage.Show;
  try
    try
      DownloadPage.Download;
      DotNetPath := ExpandConstant('{tmp}\windowsdesktop-runtime-8.0-win-x64.exe');
      VcPath := ExpandConstant('{tmp}\vc_redist.x64.exe');

      if NeedDotNet then
      begin
        if not FileExists(DotNetPath) then
          RaiseException('Downloaded .NET Desktop Runtime installer was not found.');
        if not Exec(DotNetPath, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
          RaiseException('Failed to start .NET Desktop Runtime installer.');
        if (ResultCode <> 0) and (ResultCode <> 1638) and (ResultCode <> 3010) then
          RaiseException(Format('.NET Desktop Runtime install failed (exit code %d).', [ResultCode]));
      end;

      if NeedVcRedist then
      begin
        if not FileExists(VcPath) then
          RaiseException('Downloaded VC++ Redistributable installer was not found.');
        if not Exec(VcPath, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
          RaiseException('Failed to start VC++ Redistributable installer.');
        if (ResultCode <> 0) and (ResultCode <> 1638) and (ResultCode <> 3010) then
          RaiseException(Format('VC++ Redistributable install failed (exit code %d).', [ResultCode]));
      end;
    except
      SuppressibleMsgBox(GetExceptionMessage, mbCriticalError, MB_OK, IDOK);
      Result := False;
    end;
  finally
    DownloadPage.Hide;
  end;
end;
