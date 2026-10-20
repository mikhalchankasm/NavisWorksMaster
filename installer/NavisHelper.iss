#define AppName "NavisHelper"
#ifndef AppVersion
#define AppVersion "2.14.0.0"
#endif
#ifndef SourceDir
#define SourceDir "..\artifacts\distribution\NavisHelper-full-win-x64-framework-dependent-installer-source"
#endif
#ifndef BundleInstallDir
#ifdef InstallerSmokeTest
#error InstallerSmokeTest requires an explicit isolated BundleInstallDir
#endif
#define BundleInstallDir "{userappdata}\Autodesk\ApplicationPlugins\NavisHelper.bundle"
#endif

[Setup]
AppId={{C2BD54D7-2F4E-4A8E-8ED5-9FDF784638AD}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=NavisHelper
DefaultDirName={localappdata}\NavisHelper
DefaultGroupName=NavisHelper
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
OutputBaseFilename=NavisHelperSetup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
RestartApplications=no
#ifdef InstallerSmokeTest
CloseApplications=no
UsePreviousAppDir=no
UsePreviousGroup=no
; The smoke uninstalls through {app}\unins000.exe; no Apps & Features entry is written.
CreateUninstallRegKey=no
#else
CloseApplications=yes
#endif

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[InstallDelete]
Type: filesandordirs; Name: "{#BundleInstallDir}"
#ifndef InstallerSmokeTest
Type: files; Name: "{group}\Configure MCP clients.lnk"
Type: files; Name: "{group}\Configure detected MCP clients.lnk"
Type: files; Name: "{group}\Настроить обнаруженные MCP-клиенты.lnk"
Type: files; Name: "{group}\Detect MCP clients.lnk"
Type: files; Name: "{group}\Обнаружить MCP-клиенты.lnk"
#endif

[Files]
Source: "{#SourceDir}\ClaudeDesktop\NavisHelper.mcpb"; DestDir: "{app}\ClaudeDesktop"; Flags: ignoreversion
Source: "{#SourceDir}\McpServer\*"; DestDir: "{app}\McpServerPayload"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\McpConfigurator\*"; DestDir: "{app}\McpConfigurator"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\tools\*"; DestDir: "{app}\tools"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\SETUP_PROMPT.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceDir}\UPDATE_PROMPT.md"; DestDir: "{app}"; Flags: ignoreversion skipifsourcedoesntexist
Source: "{#SourceDir}\manifest.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\mcp-client-config.example.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#SourceDir}\NavisHelper.bundle\*"; DestDir: "{#BundleInstallDir}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
#ifndef InstallerSmokeTest
Name: "{group}\{cm:ConfigureMcpShortcut}"; Filename: "{app}\McpConfigurator\NavisHelper.McpConfigurator.exe"; Parameters: "--configure --clients claude-code,codex,cursor,opencode,kimi,zcode --mcp-server ""{app}\McpServer\NavisHelper.McpServer.exe"""
Name: "{group}\{cm:DetectMcpShortcut}"; Filename: "{app}\McpConfigurator\NavisHelper.McpConfigurator.exe"; Parameters: "--detect --mcp-server ""{app}\McpServer\NavisHelper.McpServer.exe"""
Name: "{group}\{cm:ClaudeDesktopShortcut}"; Filename: "{app}\ClaudeDesktop"
#endif

[Run]
#ifndef InstallerSmokeTest
Filename: "{app}\McpConfigurator\NavisHelper.McpConfigurator.exe"; Parameters: "--configure --clients claude-code,codex,cursor,opencode,kimi,zcode --mcp-server ""{app}\McpServer\NavisHelper.McpServer.exe"""; Description: "{cm:ConfigureMcpClients}"; Flags: postinstall skipifsilent
#endif

[UninstallRun]
#ifndef InstallerSmokeTest
Filename: "{app}\McpConfigurator\NavisHelper.McpConfigurator.exe"; Parameters: "--remove --clients all"; Flags: runhidden skipifdoesntexist; RunOnceId: "RemoveNavisHelperMcpClients"
#endif

[UninstallDelete]
Type: filesandordirs; Name: "{#BundleInstallDir}"
Type: filesandordirs; Name: "{app}\versions"
Type: filesandordirs; Name: "{app}\ClaudeDesktop"

[Code]
var
  McpServerActivationFailed: Boolean;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
  Installed: Boolean;
begin
  if CurStep = ssPostInstall then
  begin
    Installed := Exec(ExpandConstant('{app}\McpConfigurator\NavisHelper.McpConfigurator.exe'),
      '--install-server "' + ExpandConstant('{app}\McpServerPayload') + '" --install-root "' + ExpandConstant('{app}') + '"',
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    if Installed then
      Installed := ResultCode = 0;
    if Installed then
      DelTree(ExpandConstant('{app}\McpServerPayload'), True, True, True)
    else
    begin
      McpServerActivationFailed := True;
      { A plain MsgBox ignores /SUPPRESSMSGBOXES and blocks a silent install forever. }
      SuppressibleMsgBox(ExpandConstant('{cm:McpServerActivationFailed}'), mbError, MB_OK, IDOK);
    end;
  end;
end;

{ /SUPPRESSMSGBOXES hides the failure message; the exit code still reports it. }
function GetCustomSetupExitCode(): Integer;
begin
  if McpServerActivationFailed then
    Result := 100
  else
    Result := 0;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    { Remove the junction itself before deleting stored versions. }
    RemoveDir(ExpandConstant('{app}\McpServer'));
  end;
end;

function IsProcessRunning(ProcessName: string): Boolean;
var
  ResultCode: Integer;
begin
  Result := False;
  if Exec(ExpandConstant('{cmd}'), '/C tasklist /FI "IMAGENAME eq ' + ProcessName + '" | find /I "' + ProcessName + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    Result := ResultCode = 0;
  end;
end;

#ifndef SelfContained
function OpenDotNet9DownloadPage(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := ShellExec('', 'https://dotnet.microsoft.com/download/dotnet/9.0/runtime', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
end;

function CheckDotNet9Runtime(DotNetExe: string): Boolean;
var
  ResultCode: Integer;
  Output: TExecOutput;
  Index: Integer;
begin
  Result := False;
  if ExecAndCaptureOutput(DotNetExe, '--list-runtimes', '', SW_SHOWNORMAL, ewWaitUntilTerminated, ResultCode, Output) then
  begin
    if (ResultCode = 0) and not Output.Error then
    begin
      for Index := 0 to GetArrayLength(Output.StdOut) - 1 do
      begin
        if Pos('Microsoft.NETCore.App 9.', Output.StdOut[Index]) = 1 then
        begin
          Result := True;
          exit;
        end;
      end;
    end;
  end;
end;

function HasDotNet9Runtime(): Boolean;
var
  LocalDotNet: string;
  ProgramFilesDotNet: string;
begin
  Result := False;
  LocalDotNet := ExpandConstant('{localappdata}\Microsoft\dotnet\dotnet.exe');
  ProgramFilesDotNet := ExpandConstant('{pf}\dotnet\dotnet.exe');

  if FileExists(LocalDotNet) and CheckDotNet9Runtime(LocalDotNet) then
  begin
    Result := True;
    exit;
  end;

  if FileExists(ProgramFilesDotNet) and CheckDotNet9Runtime(ProgramFilesDotNet) then
  begin
    Result := True;
    exit;
  end;

  Result := CheckDotNet9Runtime('dotnet');
end;
#endif

function HasLegacyMachineWideInstall(): Boolean;
begin
  Result :=
    DirExists(ExpandConstant('{commonappdata}\Autodesk\ApplicationPlugins\NavisHelper.bundle')) or
    DirExists(ExpandConstant('{autopf}\NavisHelper'));
end;

function InitializeSetup(): Boolean;
begin
#ifndef InstallerSmokeTest
  if IsProcessRunning('Roamer.exe') then
  begin
    MsgBox(ExpandConstant('{cm:CloseNavisworksBeforeInstall}'), mbError, MB_OK);
    Result := False;
    exit;
  end;

  if HasLegacyMachineWideInstall() then
  begin
    MsgBox(ExpandConstant('{cm:LegacyMachineWideInstallDetected}'), mbError, MB_OK);
    Result := False;
    exit;
  end;

#ifndef SelfContained
  if not HasDotNet9Runtime() then
  begin
    if MsgBox(ExpandConstant('{cm:DotNet9RuntimeRequired}'), mbError, MB_YESNO) = IDYES then
      OpenDotNet9DownloadPage();
    Result := False;
    exit;
  end;
#endif
#endif

  Result := True;
end;

[CustomMessages]
english.ConfigureMcpClients=Connect NavisHelper to the AI apps on this computer (all except Claude Desktop)
english.ConfigureMcpShortcut=Connect NavisHelper to AI apps
english.DetectMcpShortcut=Show which AI apps were found
english.ClaudeDesktopShortcut=NavisHelper extension for Claude Desktop
english.McpServerActivationFailed=Could not switch your AI apps to the new NavisHelper version. Close your AI apps (Claude, Codex, Cursor and others) and run the setup again. The previous version keeps working; the new files are kept in {app}\McpServerPayload.
english.CloseNavisworksBeforeInstall=Close Autodesk Navisworks and run the setup again.
english.DotNet9RuntimeRequired=NavisHelper needs the free Microsoft .NET 9 Runtime to work with AI apps. Open the Microsoft download page? Install it, then run the NavisHelper setup again. Choose No to cancel.
english.LegacyMachineWideInstallDetected=An old NavisHelper installation for all users was found in Program Files or ProgramData. The new version installs for your account only and cannot remove the old one. Ask your computer administrator to run tools\remove_machinewide_bundle.ps1 -Force from the NavisHelper ZIP in PowerShell opened as administrator, then run the setup again.
russian.ConfigureMcpClients=Подключить NavisHelper к AI-программам на этом компьютере (кроме Claude Desktop)
russian.ConfigureMcpShortcut=Подключить NavisHelper к AI-программам
russian.DetectMcpShortcut=Показать найденные AI-программы
russian.ClaudeDesktopShortcut=Расширение NavisHelper для Claude Desktop
russian.McpServerActivationFailed=Не удалось переключить AI-программы на новую версию NavisHelper. Закройте AI-программы (Claude, Codex, Cursor и другие) и запустите установку ещё раз. Прежняя версия продолжает работать; новые файлы сохранены в {app}\McpServerPayload.
russian.CloseNavisworksBeforeInstall=Закройте Autodesk Navisworks и запустите установку ещё раз.
russian.DotNet9RuntimeRequired=Для работы NavisHelper с AI-программами нужен бесплатный компонент Microsoft .NET 9 Runtime. Открыть страницу загрузки Microsoft? Установите его и запустите установку NavisHelper ещё раз. «Нет» — отменить установку.
russian.LegacyMachineWideInstallDetected=На компьютере осталась старая установка NavisHelper для всех пользователей (в Program Files или ProgramData). Новая версия ставится только для вашей учётной записи и не может удалить старую сама. Попросите администратора компьютера выполнить tools\remove_machinewide_bundle.ps1 -Force из ZIP-архива NavisHelper в PowerShell, запущенном от имени администратора, затем запустите установку ещё раз.

[Messages]
english.FinishedLabel=NavisHelper is installed. Next steps:%n%n1. Open Navisworks. The ribbon gets a NavisHelper tab; its NavisHelper button opens the panel.%n2. Click Finish, then restart the AI apps you use (Claude Code, Codex, Cursor and others). If the box below is checked, clicking Finish also connects NavisHelper to them.%n3. Claude Desktop: open Settings → Extensions, install an extension from a file and pick NavisHelper.mcpb, then restart Claude Desktop. The Start-menu shortcut "NavisHelper extension for Claude Desktop" opens its folder.
russian.FinishedLabel=NavisHelper установлен. Что дальше:%n%n1. Откройте Navisworks. На ленте появится вкладка NavisHelper, а кнопка NavisHelper на ней открывает панель.%n2. Нажмите «Завершить», затем перезапустите AI-программы, которыми пользуетесь (Claude Code, Codex, Cursor и другие). Если флажок ниже включён, при нажатии «Завершить» NavisHelper подключится к ним.%n3. Claude Desktop: откройте Settings → Extensions, выберите установку расширения из файла и укажите NavisHelper.mcpb, затем перезапустите Claude Desktop. Папку с ним открывает ярлык «Расширение NavisHelper для Claude Desktop» в меню «Пуск».
english.FinishedLabelNoIcons=NavisHelper is installed. Next steps:%n%n1. Open Navisworks. The ribbon gets a NavisHelper tab; its NavisHelper button opens the panel.%n2. Click Finish, then restart the AI apps you use (Claude Code, Codex, Cursor and others). If the box below is checked, clicking Finish also connects NavisHelper to them.%n3. Claude Desktop: open Settings → Extensions, install an extension from a file and pick NavisHelper.mcpb from the ClaudeDesktop folder of the NavisHelper installation (by default %LOCALAPPDATA%\NavisHelper\ClaudeDesktop), then restart Claude Desktop.
russian.FinishedLabelNoIcons=NavisHelper установлен. Что дальше:%n%n1. Откройте Navisworks. На ленте появится вкладка NavisHelper, а кнопка NavisHelper на ней открывает панель.%n2. Нажмите «Завершить», затем перезапустите AI-программы, которыми пользуетесь (Claude Code, Codex, Cursor и другие). Если флажок ниже включён, при нажатии «Завершить» NavisHelper подключится к ним.%n3. Claude Desktop: откройте Settings → Extensions, выберите установку расширения из файла и укажите NavisHelper.mcpb из папки ClaudeDesktop в папке установки NavisHelper (по умолчанию %LOCALAPPDATA%\NavisHelper\ClaudeDesktop), затем перезапустите Claude Desktop.
