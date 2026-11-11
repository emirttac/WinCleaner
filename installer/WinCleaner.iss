; WinCleaner Inno Setup script — version 2.6.0
; During setup:
;   - Downloads and quietly installs missing requirements:
;       .NET 8 Desktop Runtime (x64)
;       Visual C++ 2015-2022 Redistributable (x64)
;   - If WinCleaner is already installed, removes its program files
;     and installs 2.6.0 into a clean folder.
;     Settings under %LocalAppData%\WinCleaner are left in place.
;
; Build:  powershell -File installer\build-installer.ps1
; Or:     ISCC.exe installer\WinCleaner.iss

#define MyAppName "WinCleaner"
#ifndef MyAppVersion
  #define MyAppVersion "2.6.0"
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
MinVersion=10.0.19044
CloseApplications=force
CloseApplicationsFilter=WinCleaner.exe
RestartApplications=no
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

[CustomMessages]
english.ReadyNote=Setup version {#MyAppVersion} will download any missing requirement before copying files: .NET 8 Desktop Runtime (x64) and Visual C++ 2015-2022 (x64). If WinCleaner is already installed, its program files are removed and {#MyAppVersion} is installed clean.
turkish.ReadyNote=Kurulum {#MyAppVersion}, dosyaları kopyalamadan önce eksik gereksinimleri indirir: .NET 8 Desktop Runtime (x64) ve Visual C++ 2015-2022 (x64). WinCleaner zaten kuruluysa program dosyaları kaldırılır ve {#MyAppVersion} temiz kurulur.
english.PrereqBoth=Downloading required runtimes (.NET 8 Desktop + VC++ Redistributable)...
turkish.PrereqBoth=Gerekli çalışma zamanları indiriliyor (.NET 8 Desktop + VC++ Redistributable)...
english.PrereqDotNet=Downloading .NET 8 Desktop Runtime...
turkish.PrereqDotNet=.NET 8 Desktop Runtime indiriliyor...
english.PrereqVc=Downloading Visual C++ Redistributable...
turkish.PrereqVc=Visual C++ Redistributable indiriliyor...
english.InstallDotNet=Installing .NET 8 Desktop Runtime...
turkish.InstallDotNet=.NET 8 Desktop Runtime kuruluyor...
english.InstallVc=Installing Visual C++ Redistributable...
turkish.InstallVc=Visual C++ Redistributable kuruluyor...
english.DotNetMissing=Downloaded .NET Desktop Runtime installer was not found.
turkish.DotNetMissing=İndirilen .NET Desktop Runtime kurulumu bulunamadı.
english.VcMissing=Downloaded VC++ Redistributable installer was not found.
turkish.VcMissing=İndirilen VC++ Redistributable kurulumu bulunamadı.
english.DotNetStartFail=Failed to start .NET Desktop Runtime installer.
turkish.DotNetStartFail=.NET Desktop Runtime kurulumu başlatılamadı.
english.VcStartFail=Failed to start VC++ Redistributable installer.
turkish.VcStartFail=VC++ Redistributable kurulumu başlatılamadı.
english.DotNetFail=.NET Desktop Runtime install failed (exit code %d).
turkish.DotNetFail=.NET Desktop Runtime kurulumu başarısız (çıkış kodu %d).
english.VcFail=VC++ Redistributable install failed (exit code %d).
turkish.VcFail=VC++ Redistributable kurulumu başarısız (çıkış kodu %d).
english.RemoveFail=The previous WinCleaner installation could not be removed. Close WinCleaner and run Setup again.
turkish.RemoveFail=Önceki WinCleaner kurulumu kaldırılamadı. WinCleaner'ı kapatıp kurulumu yeniden çalıştırın.
english.RemoveCode=The previous WinCleaner installation could not be removed (exit code %d).
turkish.RemoveCode=Önceki WinCleaner kurulumu kaldırılamadı (çıkış kodu %d).

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Only after a previous install was detected. Fresh installs do not wipe a custom folder.
Type: filesandordirs; Name: "{app}"; Check: ShouldCleanAppDir

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
  CleaningUpgrade: Boolean;

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
var
  Installed: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM64, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) then
  begin
    if Installed = 1 then
    begin
      Result := True;
      Exit;
    end;
  end;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) then
  begin
    if Installed = 1 then
    begin
      Result := True;
      Exit;
    end;
  end;
  if RegQueryDWordValue(HKLM64, 'SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64', 'Installed', Installed) then
  begin
    if Installed = 1 then
      Result := True;
  end;
end;

function OnDownloadProgress(const Url, FileName: String; const Progress, ProgressMax: Int64): Boolean;
begin
  if Progress = ProgressMax then
    Log(Format('Downloaded %s (%d bytes)', [FileName, Progress]))
  else if ProgressMax <> 0 then
    Log(Format('Downloading %s: %d of %d', [FileName, Progress, ProgressMax]));
  Result := True;
end;

function RuntimeExitOk(Code: Integer): Boolean;
begin
  { 0 success, 1638 another version already present, 3010/1641 success but reboot wanted }
  Result := (Code = 0) or (Code = 1638) or (Code = 3010) or (Code = 1641);
end;

function IsWinCleanerDir(const Dir: String): Boolean;
var
  Name: String;
begin
  Result := False;
  if (Dir = '') or (not DirExists(Dir)) then
    Exit;
  Name := ExtractFileName(RemoveBackslash(Dir));
  Result := (CompareText(Name, 'WinCleaner') = 0) or
    FileExists(AddBackslash(Dir) + '{#MyAppExeName}');
end;

function ShouldCleanAppDir: Boolean;
begin
  { Wipe only the WinCleaner folder that is about to receive the new files. }
  Result := CleaningUpgrade and IsWinCleanerDir(ExpandConstant('{app}'));
end;

function QueryUninstall(const Root: Integer; const Key, ValueName: String; var Value: String): Boolean;
begin
  Result := RegQueryStringValue(Root, Key, ValueName, Value) and (Value <> '');
end;

function FindPreviousInstall(var Uninstall, Location: String): Boolean;
var
  Key: String;
begin
  Uninstall := '';
  Location := '';
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#MyAppId}_is1';
  if not QueryUninstall(HKLM64, Key, 'UninstallString', Uninstall) then
    if not QueryUninstall(HKLM, Key, 'UninstallString', Uninstall) then
      if not QueryUninstall(HKCU, Key, 'UninstallString', Uninstall) then
      begin
        Result := False;
        Exit;
      end;
  Result := True;
  if not QueryUninstall(HKLM64, Key, 'InstallLocation', Location) then
    if not QueryUninstall(HKLM, Key, 'InstallLocation', Location) then
      QueryUninstall(HKCU, Key, 'InstallLocation', Location);
end;

function EnsureRequirements(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
  DotNetPath: String;
  VcPath: String;
  StatusMsg: String;
begin
  Result := '';
  NeedDotNet := not HasDotNet8Desktop;
  NeedVcRedist := not IsVcRedistInstalled;
  if (not NeedDotNet) and (not NeedVcRedist) then
    Exit;

  if NeedDotNet and NeedVcRedist then
    StatusMsg := CustomMessage('PrereqBoth')
  else if NeedDotNet then
    StatusMsg := CustomMessage('PrereqDotNet')
  else
    StatusMsg := CustomMessage('PrereqVc');

  DownloadPage.Clear;
  DownloadPage.Msg1Label.Caption := StatusMsg;
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
        DownloadPage.Msg1Label.Caption := CustomMessage('InstallDotNet');
        if not FileExists(DotNetPath) then
          RaiseException(CustomMessage('DotNetMissing'));
        if not Exec(DotNetPath, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
          RaiseException(CustomMessage('DotNetStartFail'));
        if not RuntimeExitOk(ResultCode) then
          RaiseException(Format(CustomMessage('DotNetFail'), [ResultCode]));
        if (ResultCode = 3010) or (ResultCode = 1641) then
          NeedsRestart := True;
      end;

      if NeedVcRedist then
      begin
        DownloadPage.Msg1Label.Caption := CustomMessage('InstallVc');
        if not FileExists(VcPath) then
          RaiseException(CustomMessage('VcMissing'));
        if not Exec(VcPath, '/install /quiet /norestart', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
          RaiseException(CustomMessage('VcStartFail'));
        if not RuntimeExitOk(ResultCode) then
          RaiseException(Format(CustomMessage('VcFail'), [ResultCode]));
        if (ResultCode = 3010) or (ResultCode = 1641) then
          NeedsRestart := True;
      end;
    except
      Result := GetExceptionMessage;
    end;
  finally
    DownloadPage.Hide;
  end;
end;

function RemovePreviousVersion: String;
var
  Uninstall, Location, Exe: String;
  ResultCode: Integer;
begin
  Result := '';
  CleaningUpgrade := False;
  if not FindPreviousInstall(Uninstall, Location) then
    Exit;

  CleaningUpgrade := True;
  Log('Removing previous WinCleaner install before {#MyAppVersion}');
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

  Exe := RemoveQuotes(Uninstall);
  if not Exec(Exe, '/VERYSILENT /NORESTART /SUPPRESSMSGBOXES', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := CustomMessage('RemoveFail');
    Exit;
  end;
  if (ResultCode <> 0) and (ResultCode <> 3010) then
  begin
    Result := Format(CustomMessage('RemoveCode'), [ResultCode]);
    Exit;
  end;

  { Uninstall removes registered files. Drop anything it left in the old folder. }
  if IsWinCleanerDir(Location) then
    DelTree(Location, True, True, True);
end;

function InitializeSetup: Boolean;
begin
  CleaningUpgrade := False;
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

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo, MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MemoDirInfo;
  if MemoTasksInfo <> '' then
    Result := Result + NewLine + NewLine + MemoTasksInfo;
  Result := Result + NewLine + NewLine + CustomMessage('ReadyNote');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := EnsureRequirements(NeedsRestart);
  if Result <> '' then
    Exit;
  Result := RemovePreviousVersion;
end;
