; VibeDeck Windows Setup — Inno Setup 6+
; Built by scripts\package-windows-setup.ps1

#ifndef MyAppVersion
  #define MyAppVersion "0.1.31"
#endif

#ifndef MyPayloadDir
  #define MyPayloadDir "..\..\artifacts\windows-setup\payload"
#endif

#ifndef MyOutputDir
  #define MyOutputDir "..\..\artifacts\windows-setup"
#endif

#define MyAppName "VibeDeck"
#define MyAppPublisher "VibeDeck"
#define MyAppURL "https://github.com/mabyes1/VibeDeck"
#define MyAppExeName "VibeDeck.Host.exe"
#define MyServiceName "VibeDeckHost"
#define MyServiceDisplayName "VibeDeck Host"

[Setup]
AppId={{A8F3C2E1-7B4D-4F9A-9C21-6E8B0D4F1A2C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\..\LICENSE
OutputDir={#MyOutputDir}
OutputBaseFilename=VibeDeck-Setup-{#MyAppVersion}
SetupIconFile=vibedeck.ico
UninstallDisplayIcon={app}\vibedeck.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
LanguageDetectionMethod=uilanguage
ShowLanguageDialog=auto
DisableWelcomePage=no
DisableDirPage=yes
DisableReadyPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=force
RestartIfNeededByRun=no
VersionInfoVersion={#MyAppVersion}.0
VersionInfoProductName={#MyAppName}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} Setup

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesetraditional"; MessagesFile: "ChineseTraditional.isl"
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"

[CustomMessages]
english.ProductIntroCaption=Ready for a calmer desktop
english.ProductIntroDescription=VibeDeck turns an idle phone into a secure second screen and information board.
english.ProductIntroBody=Setup will install VibeDeck, add its firewall permission, create Start Menu and desktop shortcuts, and start it automatically when you sign in. Your paired devices and layouts are kept during future updates.
english.AddingFirewallRule=Allowing VibeDeck on your local network...
english.EnablingAutostart=Enabling VibeDeck at Windows sign-in...
english.StartingHost=Starting VibeDeck in your desktop session...
english.OpenVibeDeck=Open VibeDeck now
chinesetraditional.ProductIntroCaption=讓桌面多一個自在的空間
chinesetraditional.ProductIntroDescription=VibeDeck 可把閒置手機變成安全的副螢幕與資訊板。
chinesetraditional.ProductIntroBody=安裝程式會完成 VibeDeck 安裝、允許本機網路連線、建立開始功能表與桌面捷徑，並在登入 Windows 時自動啟動。日後更新會保留已配對裝置與版面配置。
chinesetraditional.AddingFirewallRule=正在允許 VibeDeck 使用本機網路…
chinesetraditional.EnablingAutostart=正在設定 Windows 登入時啟動 VibeDeck…
chinesetraditional.StartingHost=正在於你的桌面工作階段啟動 VibeDeck…
chinesetraditional.OpenVibeDeck=立即開啟 VibeDeck
japanese.ProductIntroCaption=デスクトップに、もっと心地よい余白を
japanese.ProductIntroDescription=VibeDeck は、使っていないスマートフォンを安全なセカンド スクリーンと情報ボードに変えます。
japanese.ProductIntroBody=セットアップは VibeDeck のインストール、ローカル ネットワークの許可、スタート メニューとデスクトップのショートカット作成、Windows サインイン時の自動起動を行います。以後の更新でも、ペアリング済みのデバイスとレイアウトは保持されます。
japanese.AddingFirewallRule=ローカル ネットワークで VibeDeck を許可しています…
japanese.EnablingAutostart=Windows サインイン時の VibeDeck 起動を設定しています…
japanese.StartingHost=デスクトップ セッションで VibeDeck を起動しています…
japanese.OpenVibeDeck=VibeDeck を今すぐ開く

[Files]
Source: "{#MyPayloadDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
; Host runs in the signed-in desktop session and owns mutable product state.
; SECURITY: no "users-modify" here. The data root holds the private CA key, the
; HTTPS host key and the trusted-device store; world-writable (or even world-
; readable) access lets any local account steal the machine-trusted CA or write
; itself into the trusted-device list. HardenDataDirectoryAcl in [Code] tightens
; the whole tree to SYSTEM + Administrators + the installing (interactive) user
; right after files are copied and before the Host is started.
Name: "{commonappdata}\VibeDeck"
Name: "{commonappdata}\VibeDeck\certs"
Name: "{commonappdata}\VibeDeck\devices"
Name: "{commonappdata}\VibeDeck\secrets"
Name: "{commonappdata}\VibeDeck\logs"

[InstallDelete]
; Remove replaceable web/runtime trees so deleted modules cannot survive an upgrade.
Type: filesandordirs; Name: "{app}\wwwroot"
Type: filesandordirs; Name: "{app}\Installers"
Type: filesandordirs; Name: "{app}\runtimes"
Type: filesandordirs; Name: "{app}\connectors"
Type: filesandordirs; Name: "{app}\licenses"
; Remove the pre-0.1.1 binary name after upgrading.
; Remove the pre-0.1.18 script launchers after upgrading.
Type: files; Name: "{app}\Open-VibeDeck.cmd"
Type: files; Name: "{app}\Open-VibeDeck.vbs"
Type: files; Name: "{app}\Start-VibeDeck-Host.vbs"

[Icons]
; Primary product entry: native Host opens the PC web UI and stays background-only afterwards.
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--open"; WorkingDir: "{app}"; IconFilename: "{app}\vibedeck.ico"; Comment: "Open VibeDeck on this PC"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Parameters: "--open"; WorkingDir: "{app}"; IconFilename: "{app}\vibedeck.ico"; Comment: "Open VibeDeck on this PC"

[Run]
; Configure Windows Firewall through its COM API before Host starts. Launching
; netsh.exe from Setup can surface an unhandled 0xc0000142 loader dialog.
; PrepareRuntimeEnvironment hardens the ProgramData ACLs BEFORE the first Host
; process starts, then configures the firewall rule.
Filename: "{app}\{#MyAppExeName}"; Parameters: "--register-autostart"; WorkingDir: "{app}"; Flags: runhidden runasoriginaluser; StatusMsg: "{cm:EnablingAutostart}"; BeforeInstall: PrepareRuntimeEnvironment
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Flags: runhidden nowait runasoriginaluser; StatusMsg: "{cm:StartingHost}"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--open"; WorkingDir: "{app}"; Description: "{cm:OpenVibeDeck}"; Flags: postinstall nowait skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\sc.exe"; Parameters: "stop {#MyServiceName}"; Flags: runhidden; RunOnceId: "StopVibeDeckHost"
Filename: "{sys}\sc.exe"; Parameters: "delete {#MyServiceName}"; Flags: runhidden; RunOnceId: "DeleteVibeDeckHost"
Filename: "{app}\{#MyAppExeName}"; Parameters: "--unregister-autostart"; WorkingDir: "{app}"; Flags: runhidden; RunOnceId: "RemoveVibeDeckAutostart"

[Registry]
; Autostart belongs to the signed-in desktop user whose display Host captures.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "VibeDeckHost"; Flags: deletevalue

[Code]
const
  FirewallRuleName = 'VibeDeck Host';
  NetFwActionAllow = 1;
  NetFwRuleDirectionIn = 1;
  NetFwProfile2All = $7FFFFFFF;
  NetFwIpProtocolAny = 256;

var
  ProductIntroPage: TOutputMsgWizardPage;

procedure RemoveFirewallRule;
var
  FirewallPolicy: Variant;
begin
  try
    FirewallPolicy := CreateOleObject('HNetCfg.FwPolicy2');
    try
      FirewallPolicy.Rules.Remove(FirewallRuleName);
      Log('Removed Windows Firewall rule: ' + FirewallRuleName);
    except
      { The rule may not exist. Uninstall and repair should remain silent. }
      Log('Windows Firewall rule did not need removal: ' + GetExceptionMessage);
    end;
  except
    { Firewall configuration must never strand Setup behind a system dialog. }
    Log('WARNING: Could not access Windows Firewall policy: ' + GetExceptionMessage);
  end;
end;

procedure ConfigureFirewallRule;
var
  FirewallPolicy, FirewallRule: Variant;
begin
  try
    FirewallPolicy := CreateOleObject('HNetCfg.FwPolicy2');
    try
      FirewallPolicy.Rules.Remove(FirewallRuleName);
    except
      { A first install has no existing rule. }
    end;

    FirewallRule := CreateOleObject('HNetCfg.FWRule');
    FirewallRule.Name := FirewallRuleName;
    FirewallRule.Description := 'Allow paired phones to reach VibeDeck Host';
    FirewallRule.ApplicationName := ExpandConstant('{app}\{#MyAppExeName}');
    FirewallRule.Protocol := NetFwIpProtocolAny;
    FirewallRule.Direction := NetFwRuleDirectionIn;
    FirewallRule.Enabled := True;
    FirewallRule.Profiles := NetFwProfile2All;
    FirewallRule.Action := NetFwActionAllow;
    FirewallPolicy.Rules.Add(FirewallRule);
    Log('Configured Windows Firewall rule without netsh.exe: ' + FirewallRuleName);
  except
    { Keep installation usable even on machines where Firewall is unavailable. }
    Log('WARNING: Could not configure Windows Firewall rule: ' + GetExceptionMessage);
  end;
end;

procedure InitializeWizard;
begin
  ProductIntroPage := CreateOutputMsgPage(
    wpWelcome,
    CustomMessage('ProductIntroCaption'),
    CustomMessage('ProductIntroDescription'),
    CustomMessage('ProductIntroBody'));
end;

{ Resolves the SID of the pre-elevation (interactive) user. Setup itself runs
  elevated, possibly under a different admin account, so GetUserNameString would
  name the wrong principal. }
function TryGetOriginalUserSid(var Sid: String): Boolean;
var
  TempFile, Text: String;
  Output: AnsiString;
  ResultCode, P, E: Integer;
  C: Char;
begin
  Result := False;
  Sid := '';
  TempFile := ExpandConstant('{tmp}\vibedeck-original-user-sid.txt');
  try
    if not ExecAsOriginalUser(ExpandConstant('{cmd}'),
        '/c whoami /user /fo csv > "' + TempFile + '"',
        '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      Exit;
    if ResultCode <> 0 then
      Exit;
    if not LoadStringFromFile(TempFile, Output) then
      Exit;
    Text := String(Output);
    P := Pos('S-1-', Text);
    if P = 0 then
      Exit;
    E := P;
    while E <= Length(Text) do
    begin
      C := Text[E];
      if (C = 'S') or (C = '-') or ((C >= '0') and (C <= '9')) then
        E := E + 1
      else
        Break;
    end;
    Sid := Copy(Text, P, E - P);
    Result := Length(Sid) > 8;
  except
    Log('WARNING: could not resolve original user SID: ' + GetExceptionMessage);
  end;
end;

procedure RunIcacls(const Args: String);
var
  ResultCode: Integer;
begin
  if Exec(ExpandConstant('{sys}\icacls.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log(Format('icacls %s -> exit code %d', [Args, ResultCode]))
  else
    Log('WARNING: icacls could not be started for: ' + Args);
end;

{ Tightens ProgramData\VibeDeck (CA/HTTPS private keys, trusted-device store,
  secrets, logs) to SYSTEM + Administrators (full) + the installing interactive
  user (modify). The Host runs NON-elevated in that user's desktop session and
  needs read/write over the whole data root; every other local account is cut
  off entirely. Children are then reset to inherit so ACLs from older
  world-writable installs (users-modify up to 0.1.31) are repaired too. }
procedure HardenDataDirectoryAcl;
var
  DataRoot, UserSid, UserGrant, StaleRemovals: String;
  UserSidResolved: Boolean;
begin
  DataRoot := ExpandConstant('{commonappdata}\{#MyAppName}');
  try
    ForceDirectories(DataRoot);
    ForceDirectories(DataRoot + '\certs');
    ForceDirectories(DataRoot + '\devices');
    ForceDirectories(DataRoot + '\secrets');
    ForceDirectories(DataRoot + '\logs');

    UserSidResolved := TryGetOriginalUserSid(UserSid);
    if UserSidResolved then
      UserGrant := '"*' + UserSid + ':(OI)(CI)M"'
    else
    begin
      { Fallback (e.g. silent deployment from SYSTEM context): grant the
        INTERACTIVE logon group modify so the product still works for signed-in
        users. Weaker than a single-user grant, but combined with the DPAPI
        protection of key material it still blocks other accounts from using
        the CA key or forging device-store MACs. }
      UserGrant := '"*S-1-5-4:(OI)(CI)M"';
      Log('WARNING: falling back to INTERACTIVE grant for the VibeDeck data root.');
    end;

    { 1. Protect the root: drop inheritance, keep only SYSTEM / Administrators /
         the interactive user. 2. Remove stale EXPLICIT grants: /inheritance:r
         only strips inherited ACEs and /grant:r only replaces the SIDs it
         names, so the explicit users-modify ACE written by installers up to
         0.1.31 would otherwise survive — and step 3 would re-propagate it to
         every child, silently keeping the whole tree world-writable.
         Users / Everyone / Authenticated Users always go; INTERACTIVE only
         when we hold a stronger single-user grant (it may be OUR fallback).
      3. Reset every child to inherit from the now-clean root so stale
         explicit ACEs from earlier installs are removed there as well. }
    RunIcacls('"' + DataRoot + '" /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" ' + UserGrant);
    StaleRemovals := '/remove:g *S-1-5-32-545 /remove:g *S-1-1-0 /remove:g *S-1-5-11';
    if UserSidResolved then
      StaleRemovals := StaleRemovals + ' /remove:g *S-1-5-4';
    RunIcacls('"' + DataRoot + '" ' + StaleRemovals);
    RunIcacls('"' + DataRoot + '\*" /reset /T /C /Q');
  except
    { ACL hardening must never strand Setup; the Host additionally protects key
      material with DPAPI so a failed hardening degrades, not breaks. }
    Log('WARNING: data directory ACL hardening failed: ' + GetExceptionMessage);
  end;
end;

{ BeforeInstall hook of the first [Run] entry: guarantees ACLs are tightened
  before any Host process is started by Setup. }
procedure PrepareRuntimeEnvironment;
begin
  HardenDataDirectoryAcl;
  ConfigureFirewallRule;
end;

function ServiceExists(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\sc.exe'), 'query {#MyServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode)
    and (ResultCode = 0);
end;

procedure StopRunningHost;
var
  ResultCode: Integer;
begin
  { Inno's restart-manager close pass is not reliable for the background-only
    Host because it has no normal top-level window. Kill the product process
    explicitly before [Files] replaces the loaded assemblies. Setup runs only
    for VibeDeck, and [Run] starts a fresh Host as the original desktop user. }
  if Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /T /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
    Log(Format('Stopped running VibeDeck Host before update -> exit code %d', [ResultCode]))
  else
    Log('VibeDeck Host was not running or taskkill could not be started.');
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  StopRunningHost;
  { A legacy service locks the executable before [Files] runs and also places
    Host in Session 0. Remove it before the upgrade copies any product files. }
  if ServiceExists() then
  begin
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#MyServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(1000);
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#MyServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    RemoveFirewallRule;
    Exec(ExpandConstant('{sys}\sc.exe'), 'stop {#MyServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete {#MyServiceName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;
