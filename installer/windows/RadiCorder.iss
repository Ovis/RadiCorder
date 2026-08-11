#define MyAppName "RadiCorder"
#define MyAppPublisher "RadiCorder Project"

#ifndef MyAppVersion
  #define MyAppVersion "0.0.0-dev"
#endif

#ifndef SourceDir
  #error SourceDir is not defined. Use /DSourceDir=... when invoking ISCC.
#endif

#ifndef OutputDir
  #define OutputDir "..\..\output\installer"
#endif

#ifndef InstallerFlavor
  #define InstallerFlavor "fd"
#endif

#ifndef DotNetRuntimeMode
  #define DotNetRuntimeMode "CheckOnly"
#endif

[Setup]
AppId={{4BB44B0E-8F90-43A5-84F3-6D6B2FF6CC02}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\RadiCorder
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=RadiCorder-setup-{#MyAppVersion}-{#InstallerFlavor}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x86 x64 arm64
ArchitecturesInstallIn64BitMode=x64 arm64
SetupLogging=yes
UsePreviousAppDir=yes
DisableDirPage=no
UninstallDisplayIcon={app}\RadiCorder.exe
AppMutex=RadiCorderRunningMutex
CloseApplications=yes

[Languages]
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion
Source: "precheck-dotnet.ps1"; Flags: dontcopy
Source: "{#SourceDir}\RadiCorder.runtimeconfig.json"; Flags: dontcopy

[Run]
; 既存タスクがある更新ケースでも通るように -Force を付与。
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\windows\install.ps1"" -InstallDir ""{app}"" -AppSourceDir ""{app}"" -RecordDir ""{code:GetRecordDir}"" -TempDir ""{code:GetTempDir}"" -HttpPort {code:GetHttpPort} -DotNetRuntimeMode ""{#DotNetRuntimeMode}"" -Force"; Flags: waituntilterminated; StatusMsg: "RadiCorder タスクを設定しています..."
Filename: "{code:GetAccessUrl}"; Description: "インストーラー終了後にブラウザで RadiCorder を開く"; Flags: postinstall shellexec skipifsilent unchecked nowait

[UninstallRun]
; データ保持が既定ポリシーのため -RemoveData は渡さない。
Filename: "powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\windows\uninstall.ps1"" -InstallDir ""{app}"" -ServiceName ""RadiCorder"""; Flags: waituntilterminated runhidden

[Code]
var
  StorageDirPage: TInputDirWizardPage;
  PortPage: TInputQueryWizardPage;
  PortPageInitialized: Boolean;
  StorageDirPageInitialized: Boolean;
  AccessUrlLabel: TNewStaticText;
  DotNetPrecheckErrorMessage: String;

function GetHttpPort(Param: String): String; forward;
function GetAccessUrl(Param: String): String; forward;
function TryParsePortNumber(PortText: String; var PortNumber: Integer): Boolean; forward;

procedure InitializeWizard;
begin
  StorageDirPage := CreateInputDirPage(
    wpSelectDir,
    '保存先フォルダの設定',
    '録音保存先と一時保存先を設定してください。',
    'ここで指定した値は radicorder.settings.json に反映されます。後から変更することも可能です。',
    False,
    ''
  );

  StorageDirPage.Add('録音保存先');
  StorageDirPage.Add('一時保存先');

  PortPage := CreateInputQueryPage(
    StorageDirPage.ID,
    '接続ポート設定',
    'ブラウザから接続するポート番号を設定してください。',
    '通常は 8085 のままで問題ありません。'
  );
  PortPage.Add('アクセスに使うポート番号 (1-65535):', False);

  AccessUrlLabel := TNewStaticText.Create(WizardForm.FinishedLabel.Parent);
  AccessUrlLabel.Parent := WizardForm.FinishedLabel.Parent;
  AccessUrlLabel.Left := WizardForm.FinishedLabel.Left;
  AccessUrlLabel.Top := WizardForm.FinishedLabel.Top + WizardForm.FinishedLabel.Height + ScaleY(12);
  AccessUrlLabel.Width := WizardForm.FinishedLabel.Parent.Width - WizardForm.FinishedLabel.Left;
  AccessUrlLabel.AutoSize := False;
  AccessUrlLabel.WordWrap := True;
  AccessUrlLabel.Caption := '';
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if (CurPageID = StorageDirPage.ID) and (not StorageDirPageInitialized) then
  begin
    StorageDirPage.Values[0] := AddBackslash(WizardDirValue) + 'record';
    StorageDirPage.Values[1] := AddBackslash(WizardDirValue) + 'temp';
    StorageDirPageInitialized := True;
  end;

  if (CurPageID = PortPage.ID) and (not PortPageInitialized) then
  begin
    PortPage.Values[0] := '8085';
    PortPageInitialized := True;
  end;

  if CurPageID = wpFinished then
  begin
    AccessUrlLabel.Caption :=
      'アクセスURL(ローカル): ' + GetAccessUrl('') + #13#10 +
      'アクセスURL(LAN): http://<このPCのIP>:' + GetHttpPort('') + '/';
  end;
end;

function RequiresDotNetPrecheck: Boolean;
begin
  Result := CompareText('{#DotNetRuntimeMode}', 'CheckOnly') = 0;
end;

function RunDotNetPrecheck: Boolean;
var
  ResultCode: Integer;
  ScriptPath: String;
  RuntimeConfigPath: String;
  LogPath: String;
  Args: String;
begin
  Result := True;
  DotNetPrecheckErrorMessage := '';

  if not RequiresDotNetPrecheck then
  begin
    Log('DotNet precheck skipped. DotNetRuntimeMode=' + '{#DotNetRuntimeMode}');
    exit;
  end;

  ExtractTemporaryFile('precheck-dotnet.ps1');
  ExtractTemporaryFile('RadiCorder.runtimeconfig.json');

  ScriptPath := ExpandConstant('{tmp}\precheck-dotnet.ps1');
  RuntimeConfigPath := ExpandConstant('{tmp}\RadiCorder.runtimeconfig.json');
  LogPath := ExpandConstant('{tmp}\radicorder-dotnet-precheck.log');

  Args :=
    '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath + '"' +
    ' -RuntimeConfigPath "' + RuntimeConfigPath + '"' +
    ' -LogPath "' + LogPath + '"';

  if not Exec('powershell.exe', Args, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    DotNetPrecheckErrorMessage :=
      '事前チェックの実行に失敗しました。PowerShell の実行権限を確認してください。';
    Result := False;
    exit;
  end;

  if ResultCode <> 0 then
  begin
    DotNetPrecheckErrorMessage :=
      '必要な .NET ランタイムが不足しているため、インストールを中断しました。' + #13#10 +
      '詳細ログ: ' + LogPath + #13#10 +
      '終了コード: ' + IntToStr(ResultCode);
    Result := False;
    exit;
  end;

  Log('DotNet precheck passed.');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  PortText: String;
  PortNumber: Integer;
begin
  Result := True;

  if CurPageID = StorageDirPage.ID then
  begin
    if Trim(StorageDirPage.Values[0]) = '' then
    begin
      MsgBox('録音保存先を入力してください。', mbError, MB_OK);
      Result := False;
      exit;
    end;

    if Trim(StorageDirPage.Values[1]) = '' then
    begin
      MsgBox('一時保存先を入力してください。', mbError, MB_OK);
      Result := False;
      exit;
    end;
  end;

  if CurPageID = PortPage.ID then
  begin
    PortText := Trim(PortPage.Values[0]);
    if (PortText = '') or (not TryParsePortNumber(PortText, PortNumber)) then
    begin
      MsgBox('ポート番号は数値で入力してください。', mbError, MB_OK);
      Result := False;
      exit;
    end;

    if (PortNumber < 1) or (PortNumber > 65535) then
    begin
      MsgBox('ポート番号は 1 から 65535 の範囲で入力してください。', mbError, MB_OK);
      Result := False;
      exit;
    end;
  end;
end;

function TryParsePortNumber(PortText: String; var PortNumber: Integer): Boolean;
begin
  try
    PortNumber := StrToInt(PortText);
    Result := True;
  except
    Result := False;
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not RunDotNetPrecheck then
    Result := DotNetPrecheckErrorMessage;
end;

function GetRecordDir(Param: String): String;
begin
  Result := StorageDirPage.Values[0];
end;

function GetTempDir(Param: String): String;
begin
  Result := StorageDirPage.Values[1];
end;

function GetHttpPort(Param: String): String;
var
  PortText: String;
  PortNumber: Integer;
begin
  PortText := Trim(PortPage.Values[0]);
  if (PortText = '') or (not TryParsePortNumber(PortText, PortNumber)) then
  begin
    Result := '8085';
    exit;
  end;

  if (PortNumber < 1) or (PortNumber > 65535) then
  begin
    Result := '8085';
    exit;
  end;

  Result := IntToStr(PortNumber);
end;

function GetAccessUrl(Param: String): String;
begin
  Result := 'http://127.0.0.1:' + GetHttpPort('') + '/';
end;
