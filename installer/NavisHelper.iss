#define AppName "NavisHelper"
#ifndef AppVersion
#define AppVersion "2.9.0.0"
#endif
#ifndef SourceDir
#define SourceDir "..\artifacts\distribution\NavisHelper-full-win-x64-framework-dependent-2.9.0.0"
#endif
#ifndef BundleInstallDir
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
CloseApplications=yes
RestartApplications=no
#ifdef InstallerSmokeTest
Uninstallable=no
CreateUninstallRegKey=no
#endif

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[InstallDelete]
Type: filesandordirs; Name: "{#BundleInstallDir}"

[Files]
Source: "{#SourceDir}\McpServer\*"; DestDir: "{app}\McpServer"; Flags: ignoreversion recursesubdirs createallsubdirs
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
Name: "{group}\Configure MCP clients"; Filename: "{app}\McpConfigurator\NavisHelper.McpConfigurator.exe"; Parameters: "--configure --clients all --create-missing --mcp-server ""{app}\McpServer\NavisHelper.McpServer.exe"""
Name: "{group}\Detect MCP clients"; Filename: "{app}\McpConfigurator\NavisHelper.McpConfigurator.exe"; Parameters: "--detect --mcp-server ""{app}\McpServer\NavisHelper.McpServer.exe"""

[Run]
#ifndef InstallerSmokeTest
Filename: "{app}\McpConfigurator\NavisHelper.McpConfigurator.exe"; Parameters: "--configure --clients all --create-missing --mcp-server ""{app}\McpServer\NavisHelper.McpServer.exe"""; Description: "{cm:ConfigureMcpClients}"; Flags: postinstall
#endif

[UninstallRun]
Filename: "{app}\McpConfigurator\NavisHelper.McpConfigurator.exe"; Parameters: "--remove --clients all"; Flags: runhidden skipifdoesntexist; RunOnceId: "RemoveNavisHelperMcpClients"

[UninstallDelete]
Type: filesandordirs; Name: "{#BundleInstallDir}"

[Code]
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
english.ConfigureMcpClients=Configure MCP clients
english.CloseNavisworksBeforeInstall=Close Autodesk Navisworks before installing. The Navisworks process is still running.
english.DotNet9RuntimeRequired=NavisHelper requires .NET 9 Runtime for its MCP server. Open the official Microsoft download page now? After installation, run this installer again. Choose No to cancel and install it later.
english.LegacyMachineWideInstallDetected=A previous machine-wide NavisHelper installation was found in Program Files or ProgramData. This installer supports per-user folders only and cannot safely remove protected legacy files. From the release ZIP or repository, run tools\remove_machinewide_bundle.ps1 -Force in an elevated PowerShell, then run this installer again.
russian.ConfigureMcpClients=Настроить MCP-клиенты
russian.CloseNavisworksBeforeInstall=Перед установкой нужно закрыть Autodesk Navisworks. Сейчас процесс Navisworks ещё запущен.
russian.DotNet9RuntimeRequired=Для MCP-сервера NavisHelper нужен .NET 9 Runtime. Открыть официальную страницу Microsoft для скачивания? После установки запустите этот installer заново. Нажмите «Нет», чтобы отменить установку и поставить Runtime позже.
russian.LegacyMachineWideInstallDetected=Обнаружена предыдущая системная установка NavisHelper в Program Files или ProgramData. Этот installer поддерживает только пользовательские папки и не может безопасно удалить защищённые старые файлы. В elevated PowerShell из release ZIP или репозитория выполните tools\remove_machinewide_bundle.ps1 -Force, затем запустите installer ещё раз.
