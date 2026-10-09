#define AppVersion "1.0.1"
#define PublishDir "..\artifacts\publish"
[Setup]
AppId={{8CB1112D-9CD0-42F9-A1EA-9172447020CC}
AppName=BatteryHelper
AppVersion={#AppVersion}
AppPublisher=BatteryHelper
DefaultDirName={autopf}\BatteryHelper
DefaultGroupName=BatteryHelper
DisableProgramGroupPage=yes
OutputDir=..\artifacts\dist
OutputBaseFilename=BatteryHelper-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
SetupIconFile=..\src\BatteryHelper.App\Assets\BatteryHelper.ico
UninstallDisplayIcon={app}\BatteryHelper.exe
CloseApplications=yes
RestartApplications=no
MinVersion=10.0.22000
LicenseFile=..\LICENSE
[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: "startup"; Description: "登录后自动启动 BatteryHelper"; Flags: checkedonce
[Files]
Source: "{#PublishDir}\app\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDir}\service\*"; DestDir: "{app}\service"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\Install-Service.ps1"; DestDir: "{app}\setup"; Flags: ignoreversion
Source: "..\.vendor\lhm\LibreHardwareMonitor.Windows.Forms\Resources\PawnIO_setup.exe"; DestDir: "{app}\drivers"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\.vendor\lhm\LICENSE"; DestDir: "{app}\licenses"; DestName: "LibreHardwareMonitor-MPL-2.0.txt"; Flags: ignoreversion
[Icons]
Name: "{group}\BatteryHelper"; Filename: "{app}\BatteryHelper.exe"
Name: "{group}\卸载 BatteryHelper"; Filename: "{uninstallexe}"
[Run]
Filename: "{app}\BatteryHelper.exe"; Parameters: "--configure-startup on"; Tasks: startup; Check: PowerServiceReady; Flags: runasoriginaluser waituntilterminated runhidden
Filename: "{app}\BatteryHelper.exe"; Parameters: "--configure-startup off"; Tasks: not startup; Check: PowerServiceReady; Flags: runasoriginaluser waituntilterminated runhidden
Filename: "{app}\BatteryHelper.exe"; Description: "启动 BatteryHelper"; Check: PowerServiceReady; Flags: nowait postinstall skipifsilent runasoriginaluser
[UninstallRun]
Filename: "{app}\BatteryHelper.exe"; Parameters: "--quit"; Flags: runhidden waituntilterminated; RunOnceId: "ClosePowerUi"
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\setup\Install-Service.ps1"" -Action Uninstall -ApplicationDirectory ""{app}"""; Flags: runhidden waituntilterminated; RunOnceId: "RemovePowerService"
[Code]
var
  ServiceConfigured: Boolean;

function PowerServiceReady: Boolean;
begin
  Result := ServiceConfigured;
end;

function GetCustomSetupExitCode: Integer;
begin
  if ServiceConfigured then Result := 0 else Result := 10;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if FileExists(ExpandConstant('{app}\BatteryHelper.exe')) then begin
    Exec(ExpandConstant('{app}\BatteryHelper.exe'), '--quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Sleep(500);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then begin
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
      ExpandConstant('-NoProfile -ExecutionPolicy Bypass -File "{app}\setup\Install-Service.ps1" -Action Install -ApplicationDirectory "{app}"'),
      '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
      RaiseException('无法启动功率采集组件配置程序。');
    if ResultCode <> 0 then
      RaiseException('功率采集组件配置失败。请查看 README 中的安装说明。');
    ServiceConfigured := True;
  end;
end;
