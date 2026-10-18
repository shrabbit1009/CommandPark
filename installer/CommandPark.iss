; CommandPark 安装脚本（Inno Setup 6.5+ / 7）
;
;   ISCC.exe installer\CommandPark.iss
;   ISCC.exe /DMyAppVersion=1.2.3 /DPublishDir=D:\build\aot installer\CommandPark.iss
;
; 与压缩包版的关系：安装包的内容 = {#PublishDir} 下的全部文件（含 lang\ 翻译、licenses\ 许可证），
; 两者由同一次 dotnet publish 的产物生成，只有「怎么写进系统」不同。
;
; 几个刻意的选择：
;   1) 默认启用开机自启动：写 HKCU\...\Run，命令行带 --no-display，
;      自启动时不弹「已在后台启动」提示，直接常驻后台等全局热键。
;      任务页可以取消；取消后升级不会再自动勾选（UsePreviousTasks 记住上次的选择）。
;   2) 只对「安装它的那个用户」自启动（HKCU，而不是 HKLM）：
;      全局热键是每个会话各注册一份，多用户机器的其它用户登录时不会平白多出一个常驻进程。
;   3) 默认配置释放到用户 Roaming AppData；已存在时不覆盖，程序目录配置仍作为便携模式优先。
;   4) 界面语言：简体中文 + 英文，跟随系统语言自动选择。

#define MyAppName "CommandPark"
; AppId 不能通过 {#...} 常量替换（{{ 本身就是转义），所以直接在下面写死 GUID 即可：#DC62A9B5-50A0-4DCE-84DC-5DDF437DD48D
#define MyAppPublisher "shRabbit"
#define MyAppUrl "https://home.shrabbit.com/products/commandpark"
#define MyAppSupportUrl "https://github.com/shrabbit1009/CommandPark"
#define MyAppExeName "CommandPark.Ui.exe"

; 版本号与目录可通过 /D 覆盖（CI 里从 tag 取版本号）
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\aot"
#endif
#ifndef OutputDir
  #define OutputDir "..\publish\installer"
#endif

[Setup]
AppId={{DC62A9B5-50A0-4DCE-84DC-5DDF437DD48D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppSupportUrl}
AppUpdatesURL={#MyAppSupportUrl}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
VersionInfoDescription={#MyAppName} {#MyAppVersion} Setup

DefaultDirName={autopf}\{#MyAppPublisher}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes

; MIT 许可证：安装向导里会显示，同时随程序一起释放
LicenseFile=..\LICENSE

OutputDir={#OutputDir}
OutputBaseFilename={#MyAppName}-{#MyAppVersion}-setup
SetupIconFile=..\src\CommandPark.Ui\Assets\Icons\black-line-full.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern dynamic

; 主程序是 win-x64（AOT/自包含）。x64compatible 也允许在 Windows 11 on Arm 上通过模拟安装。
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; .NET 10 要求 Windows 10 1607+；这里取 1809（Win10 的长期支持起点）作为实际底线
MinVersion=10.0.17763

; 安装到 Program Files 需要管理员；同时允许用户选择「只为我安装」（装到 %LocalAppData%）
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
; （commandline 允许用 /CURRENTUSER 或 /ALLUSERS 覆盖：方便用户只装给当前账户，也方便静默测试）
; 我们就是要动用户级区域（HKCU\...\Run 自启动项），明确关掉 Inno 的那条提醒，避免每次编译都刷警告
UsedUserAreasWarning=no
; 升级时如果程序/后台程序正在运行，交给重启管理器关闭；装完再恢复，热键不会失效
CloseApplications=yes
RestartApplications=yes

[Languages]
; 简体中文翻译来自 MIT 授权的第三方项目（见 languages\LICENSE.txt 与 THIRD-PARTY-NOTICES.md）
Name: "chinesesimplified"; MessagesFile: "languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimplified.StartupGroup=启动选项
chinesesimplified.StartupTask=开机自动启动（后台常驻，用全局热键唤起）
chinesesimplified.GitHubShortcut=访问 GitHub 仓库
chinesesimplified.UninstallShortcut=卸载 CommandPark
chinesesimplified.CloseBeforeUninstall=CommandPark 正在运行。请先关闭主程序，并从系统托盘退出后台程序，然后点击“重试”继续卸载。
chinesesimplified.UninstallOptionsCaption=卸载选项
chinesesimplified.UninstallOptionsTitle=选择要删除的本机数据
chinesesimplified.UninstallOptionsDescription=取消勾选可以保留对应数据；默认两项都会删除。
chinesesimplified.UninstallClicksOption=删除点击记录 clicks.htt
chinesesimplified.UninstallConfigOption=删除配置文件 commandpark.json / config.json
chinesesimplified.UninstallOptionsOk=继续卸载
chinesesimplified.UninstallOptionsCancel=取消
english.StartupGroup=Startup options
english.StartupTask=Start automatically at sign-in (run in the background, open with the global hotkey)
english.GitHubShortcut=Visit GitHub repository
english.UninstallShortcut=Uninstall CommandPark
english.CloseBeforeUninstall=CommandPark is running. Close the main program and exit the background program from the system tray, then click Retry to continue uninstalling.
english.UninstallOptionsCaption=Uninstall options
english.UninstallOptionsTitle=Choose local data to delete
english.UninstallOptionsDescription=Clear a checkbox to keep the corresponding data. Both are deleted by default.
english.UninstallClicksOption=Delete click history (clicks.htt)
english.UninstallConfigOption=Delete configuration files (commandpark.json / config.json)
english.UninstallOptionsOk=Uninstall
english.UninstallOptionsCancel=Cancel

[Tasks]
; 开机自启动：默认勾选，用户可以取消。升级时沿用上次的选择（UsePreviousTasks 默认 yes）。
Name: "startup"; Description: "{cm:StartupTask}"; GroupDescription: "{cm:StartupGroup}"
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 程序本体与全部运行时文件：主程序、后台程序、lang\ 翻译、runtimes\、许可证与第三方声明
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "commandpark.json,*.pdb"; \
  Flags: ignoreversion recursesubdirs createallsubdirs
; 用户配置：已经存在就不覆盖，保留用户自己的设置（新装时才释放默认配置）
Source: "{#PublishDir}\commandpark.json"; DestDir: "{userappdata}\{#MyAppPublisher}\{#MyAppName}"; Flags: onlyifdoesntexist
Source: "{#PublishDir}\commandpark.schema.json"; DestDir: "{userappdata}\{#MyAppPublisher}\{#MyAppName}"; Flags: ignoreversion

[InstallDelete]
; 旧版本把主程序快捷方式直接放在开始菜单根目录；新版改用 CommandPark 文件夹。
Type: files; Name: "{autoprograms}\{#MyAppName}.lnk"

[Icons]
; 开始菜单统一收进 CommandPark 文件夹：主程序、GitHub 仓库、卸载。
Name: "{autoprograms}\{#MyAppName}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\{#MyAppName}\{cm:GitHubShortcut}"; Filename: "{#MyAppSupportUrl}"; IconFilename: "{app}\{#MyAppExeName}"
Name: "{autoprograms}\{#MyAppName}\{cm:UninstallShortcut}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; 开机自启动：只有勾选了 startup 任务才会写；卸载时删除这个值。
; --no-display 让自启动这一次静默启动：拉起后台程序后直接退出，不弹提示窗口。
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; \
  ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"" --no-display"; \
  Flags: uninsdeletevalue; Tasks: startup

[Run]
; 装完可以顺手启动一次：第一次运行会拉起后台程序并提示热键，正是我们想让人看到的。
; 主程序清单要求管理员权限，postinstall 默认会退回原始用户令牌，所以必须显式用 runas 启动。
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; \
  Verb: "runas"; Flags: nowait postinstall skipifsilent shellexec

[UninstallDelete]
; 程序运行时留下的文件（后台程序 PID、临时文件）不是安装时写进去的，
; 需要显式删除，否则卸载后 {app} 目录会残留
Type: files; Name: "{app}\PID"
Type: files; Name: "{app}\*.tmp"

[Code]
var
  DeleteClickHistory: Boolean;
  DeleteConfigFiles: Boolean;

function IsCommandParkProcessRunning(const ImageName: String): Boolean;
var
  Cmd: String;
  Output: AnsiString;
  ResultCode: Integer;
  TempFile: String;
begin
  Result := False;
  TempFile := ExpandConstant('{tmp}\CommandPark-uninstall-tasklist.txt');
  DeleteFile(TempFile);
  Cmd := '/c tasklist /FI "IMAGENAME eq ' + ImageName + '" /FO CSV /NH > "' + TempFile + '"';

  if Exec(ExpandConstant('{cmd}'), Cmd, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and
     (ResultCode = 0) and FileExists(TempFile) then
  begin
    if LoadStringFromFile(TempFile, Output) then
      Result := Pos(LowerCase(ImageName), LowerCase(Output)) > 0;
  end;

  DeleteFile(TempFile);
end;

function ShowUninstallOptions(): Boolean;
var
  Form: TSetupForm;
  TitleLabel, DescriptionLabel: TNewStaticText;
  ClicksCheckBox, ConfigCheckBox: TNewCheckBox;
  OKButton, CancelButton: TNewButton;
  ButtonWidth: Integer;
begin
  Result := False;
  Form := CreateCustomForm(ScaleX(520), ScaleY(250), False, False);
  try
    Form.Caption := ExpandConstant('{cm:UninstallOptionsCaption}');
    Form.BorderStyle := bsDialog;
    Form.Position := poScreenCenter;

    TitleLabel := TNewStaticText.Create(Form);
    TitleLabel.Parent := Form;
    TitleLabel.Left := ScaleX(24);
    TitleLabel.Top := ScaleY(20);
    TitleLabel.Width := Form.ClientWidth - ScaleX(48);
    TitleLabel.Height := ScaleY(24);
    TitleLabel.AutoSize := False;
    TitleLabel.Font.Style := [fsBold];
    TitleLabel.Caption := ExpandConstant('{cm:UninstallOptionsTitle}');

    DescriptionLabel := TNewStaticText.Create(Form);
    DescriptionLabel.Parent := Form;
    DescriptionLabel.Left := ScaleX(24);
    DescriptionLabel.Top := ScaleY(52);
    DescriptionLabel.Width := Form.ClientWidth - ScaleX(48);
    DescriptionLabel.Height := ScaleY(42);
    DescriptionLabel.AutoSize := False;
    DescriptionLabel.WordWrap := True;
    DescriptionLabel.Caption := ExpandConstant('{cm:UninstallOptionsDescription}');

    ClicksCheckBox := TNewCheckBox.Create(Form);
    ClicksCheckBox.Parent := Form;
    ClicksCheckBox.Left := ScaleX(24);
    ClicksCheckBox.Top := ScaleY(108);
    ClicksCheckBox.Width := Form.ClientWidth - ScaleX(48);
    ClicksCheckBox.Height := ScaleY(24);
    ClicksCheckBox.Caption := ExpandConstant('{cm:UninstallClicksOption}');
    ClicksCheckBox.Checked := True;

    ConfigCheckBox := TNewCheckBox.Create(Form);
    ConfigCheckBox.Parent := Form;
    ConfigCheckBox.Left := ScaleX(24);
    ConfigCheckBox.Top := ScaleY(142);
    ConfigCheckBox.Width := Form.ClientWidth - ScaleX(48);
    ConfigCheckBox.Height := ScaleY(24);
    ConfigCheckBox.Caption := ExpandConstant('{cm:UninstallConfigOption}');
    ConfigCheckBox.Checked := True;

    OKButton := TNewButton.Create(Form);
    OKButton.Parent := Form;
    OKButton.Caption := ExpandConstant('{cm:UninstallOptionsOk}');
    OKButton.Top := Form.ClientHeight - ScaleY(23 + 16);
    OKButton.Height := ScaleY(23);
    OKButton.ModalResult := mrOk;
    OKButton.Default := True;

    CancelButton := TNewButton.Create(Form);
    CancelButton.Parent := Form;
    CancelButton.Caption := ExpandConstant('{cm:UninstallOptionsCancel}');
    CancelButton.Top := Form.ClientHeight - ScaleY(23 + 16);
    CancelButton.Height := ScaleY(23);
    CancelButton.ModalResult := mrCancel;
    CancelButton.Cancel := True;

    ButtonWidth := Form.CalculateButtonWidth([OKButton.Caption, CancelButton.Caption]);
    OKButton.Width := ButtonWidth;
    CancelButton.Width := ButtonWidth;
    CancelButton.Left := Form.ClientWidth - ScaleX(24) - ButtonWidth;
    OKButton.Left := CancelButton.Left - ScaleX(8) - ButtonWidth;

    Form.ActiveControl := OKButton;

    if Form.ShowModal() = mrOk then
    begin
      DeleteClickHistory := ClicksCheckBox.Checked;
      DeleteConfigFiles := ConfigCheckBox.Checked;
      Result := True;
    end;
  finally
    Form.Free();
  end;
end;

function InitializeUninstall(): Boolean;
begin
  Result := True;
  DeleteClickHistory := True;
  DeleteConfigFiles := True;

  // 主程序或后台程序还在运行时，先让用户正常退出，避免卸载文件被占用。
  while IsCommandParkProcessRunning('{#MyAppExeName}') or
        IsCommandParkProcessRunning('CommandPark.Background.Cpp.exe') do
  begin
    if MsgBox(ExpandConstant('{cm:CloseBeforeUninstall}'), mbConfirmation, MB_RETRYCANCEL) = IDCANCEL then
    begin
      Result := False;
      Exit;
    end;
  end;

  // 静默卸载保持默认行为：点击记录与配置文件都删除。
  if not UninstallSilent() then
  begin
    if not ShowUninstallOptions() then
    begin
      Result := False;
      Exit;
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  if DeleteClickHistory then
  begin
    DeleteFile(ExpandConstant('{app}\clicks.htt'));
    DeleteFile(ExpandConstant('{userappdata}\shRabbit\CommandPark\clicks.htt'));
  end;

  if DeleteConfigFiles then
  begin
    DeleteFile(ExpandConstant('{app}\commandpark.json'));
    DeleteFile(ExpandConstant('{app}\config.json'));
    DeleteFile(ExpandConstant('{userappdata}\shRabbit\CommandPark\commandpark.json'));
    DeleteFile(ExpandConstant('{userappdata}\shRabbit\CommandPark\config.json'));
    DeleteFile(ExpandConstant('{userappdata}\shRabbit\CommandPark\commandpark.schema.json'));
    RemoveDir(ExpandConstant('{userappdata}\shRabbit\CommandPark'));
    RemoveDir(ExpandConstant('{userappdata}\shRabbit'));
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // 升级时如果用户把「开机自动启动」取消了，注册表里旧的自启动项要清掉：
  // [Registry] 里的条目在未勾选时根本不会执行，所以这里补一刀。
  // 注意：[Code] 里不能用 { } 当注释（那是 Inno 的常量语法），要用 // 或 (* *)。
  if (CurStep = ssPostInstall) and (not WizardIsTaskSelected('startup')) then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', '{#MyAppName}');
end;