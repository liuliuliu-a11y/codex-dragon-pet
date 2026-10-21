#ifndef AppVersion
  #define AppVersion "0.4.0"
#endif
#ifndef PayloadDir
  #error PayloadDir must point to a prepared binary package
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#define SourceRoot AddBackslash(SourcePath) + ".."

[Setup]
AppId={{280EA0C8-2EC1-43D2-8DE6-40149D14C2EC}
AppName=Codex 小龙桌宠
AppVersion={#AppVersion}
AppPublisher=liuliuliu-a11y
AppPublisherURL=https://github.com/liuliuliu-a11y/codex-dragon-pet
AppSupportURL=https://github.com/liuliuliu-a11y/codex-dragon-pet/issues
DefaultDirName={localappdata}\Programs\CodexDragonPet
DefaultGroupName=Codex 小龙桌宠
PrivilegesRequired=lowest
MinVersion=10.0
ArchitecturesAllowed=x64compatible
WizardStyle=modern
DisableProgramGroupPage=yes
SetupIconFile={#SourceRoot}\assets\tray.ico
UninstallDisplayIcon={app}\bin\CodexDragonPet.exe
OutputDir={#OutputDir}
OutputBaseFilename=CodexDragonPet-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
CloseApplications=no
RestartApplications=no
Uninstallable=yes
SetupMutex=CodexDragonPetInstaller

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinesesimp.DesktopTask=创建桌面快捷方式
chinesesimp.AutoLinkTask=打开 Codex 时自动启动小龙（登录时运行轻量联动程序）
chinesesimp.LaunchPet=启动 Codex 小龙桌宠
chinesesimp.DotNetMissing=需要 .NET Framework 4.8。请先从 Microsoft 安装该运行时，再重新运行安装包。
english.DesktopTask=Create a desktop shortcut
english.AutoLinkTask=Start the pet with Codex (run the lightweight link helper at sign-in)
english.LaunchPet=Launch Codex Dragon Pet
english.DotNetMissing=Microsoft .NET Framework 4.8 is required. Install it from Microsoft before running this setup again.

[Tasks]
Name: "desktopicon"; Description: "{cm:DesktopTask}"
Name: "autolink"; Description: "{cm:AutoLinkTask}"

[Files]
Source: "{#PayloadDir}\bin\CodexDragonPet.exe"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "{#PayloadDir}\bin\CodexDragonPet.exe.config"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "{#PayloadDir}\bin\CodexPetWatcher.exe"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "{#PayloadDir}\bin\CodexPetWatcher.exe.config"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "{#PayloadDir}\bin\assets\dragon-sprites-clean.png"; DestDir: "{app}\bin\assets"; Flags: ignoreversion
Source: "{#PayloadDir}\bin\assets\dragon-actions.png"; DestDir: "{app}\bin\assets"; Flags: ignoreversion
Source: "{#PayloadDir}\bin\assets\layered\*.png"; DestDir: "{app}\bin\assets\layered"; Flags: ignoreversion
Source: "{#PayloadDir}\bin\assets\layered\manifest.json"; DestDir: "{app}\bin\assets\layered"; Flags: ignoreversion
Source: "{#PayloadDir}\bin\assets\tray.ico"; DestDir: "{app}\bin\assets"; Flags: ignoreversion
Source: "{#PayloadDir}\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\DEPENDENCIES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\start.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\start-with-codex.cmd"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\preview-v0.2.0.png"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PayloadDir}\docs\*.md"; DestDir: "{app}\docs"; Flags: ignoreversion
Source: "{#PayloadDir}\scripts\Start-WithCodex.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#PayloadDir}\scripts\Install.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#PayloadDir}\scripts\Disable-CodexLink.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion
Source: "{#SourceRoot}\installer\Stop-Installed.ps1"; DestDir: "{app}\scripts"; Flags: ignoreversion

[Icons]
Name: "{group}\Codex 小龙桌宠"; Filename: "{app}\bin\CodexDragonPet.exe"; WorkingDir: "{app}\bin"
Name: "{group}\卸载 Codex 小龙桌宠"; Filename: "{uninstallexe}"
Name: "{userdesktop}\Codex Dragon Pet"; Filename: "{app}\bin\CodexDragonPet.exe"; WorkingDir: "{app}\bin"; Tasks: desktopicon
Name: "{userstartup}\Codex Dragon Pet Link"; Filename: "{app}\bin\CodexPetWatcher.exe"; WorkingDir: "{app}\bin"; Tasks: autolink

[Run]
Filename: "{app}\bin\CodexPetWatcher.exe"; Flags: nowait runhidden; Tasks: autolink
Filename: "{app}\bin\CodexDragonPet.exe"; Description: "{cm:LaunchPet}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\scripts\Stop-Installed.ps1"" -InstallRoot ""{app}"""; Flags: runhidden waituntilterminated; RunOnceId: "StopInstalledPet"

[Code]
function InitializeSetup: Boolean;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then
    SuppressibleMsgBox(CustomMessage('DotNetMissing'), mbCriticalError, MB_OK, IDOK);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ExitCode: Integer;
begin
  Result := '';
  ExtractTemporaryFile('Stop-Installed.ps1');
  if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ExpandConstant('{tmp}\Stop-Installed.ps1') +
    '" -InstallRoot "' + ExpandConstant('{app}') + '"', '', SW_HIDE, ewWaitUntilTerminated, ExitCode) then
    Result := 'Could not prepare the installation.'
  else if ExitCode <> 0 then
    Result := 'Please close this installation of the pet and retry. Codex can stay open.';
end;
