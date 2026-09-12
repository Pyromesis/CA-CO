; =============================================================
; CA-CO — Instalador Inno Setup
; Per-usuario (sin UAC), firmado con la CA local (SignTool casign).
; Compilar: ISCC.exe /S"casign=..." installer\CA-CO.iss
;   (o .\installer\Build-Installer.ps1 que lo hace todo)
; =============================================================
#define AppVersion "1.2.0"
#define Staging "staging\CA-CO"

[Setup]
AppId={{E30D5966-029F-4D7D-9C53-23014FCCA544}
AppName=CA-CO
AppVersion={#AppVersion}
AppVerName=CA-CO {#AppVersion}
AppPublisher=CA-CO
AppCopyright=CA-CO
AppComments=Tu biblioteca personal, offline-first.
VersionInfoDescription=Instalador de CA-CO
VersionInfoProductName=CA-CO
VersionInfoCopyright=CA-CO
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
DefaultDirName={localappdata}\Programs\CA-CO
UsePreviousAppDir=yes
DisableDirPage=no
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
SetupIconFile=..\src\CA-CO.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\CaCo.App.exe
WizardStyle=modern
WizardSizePercent=120
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
PrivilegesRequired=lowest
CloseApplications=yes
CloseApplicationsFilter=CaCo.App.exe
RestartApplications=no
OutputDir=dist
OutputBaseFilename=CA-CO-Setup-{#AppVersion}-x64
SignTool=casign
SignedUninstaller=yes
UninstallDisplayName=CA-CO

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "deletedata"; Description: "Al desinstalar, borrar tambien la biblioteca y los ajustes (%LOCALAPPDATA%\CA-CO)"; GroupDescription: "Desinstalacion:"; Flags: unchecked

[Files]
Source: "{#Staging}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs restartreplace; Excludes: "*.pdb,*.xml"

[Icons]
Name: "{userprograms}\CA-CO"; Filename: "{app}\CaCo.App.exe"; WorkingDir: "{app}"; Comment: "Tu biblioteca personal, offline-first."
Name: "{userdesktop}\CA-CO"; Filename: "{app}\CaCo.App.exe"; WorkingDir: "{app}"; Comment: "Tu biblioteca personal, offline-first."

[Run]
Filename: "{app}\CaCo.App.exe"; Description: "Abrir CA-CO"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\CA-CO"; Tasks: deletedata

[Code]
const
  WV2_KEY = 'SOFTWARE\Microsoft\EdgeUpdate\ClientState\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  EDGE_KEY = 'SOFTWARE\Microsoft\EdgeUpdate\ClientState\{56EB18F8-B008-4CBD-B6D2-8C97FE7E9062}';
  UNINST_KEY = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{E30D5966-029F-4D7D-9C53-23014FCCA544}_is1';

var
  OldAppPath: String;

function GetPreviousAppPath(): String;
var
  S: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU, UNINST_KEY, 'Inno Setup: App Path', S) then
    Result := S;
end;

function MotorWebDisponible(): Boolean;
var
  Ver: String;
begin
  { El setup es de 32 bits: HKLM solo ve WOW6432Node, asi que hay que leer
    tambien la vista de 64 bits (HKLM64). Y en algunos equipos la clave
    ClientState existe pero sin valor 'pv': por eso se comprueba ademas
    la presencia real en disco (runtime y Edge). }
  Result :=
    DirExists(ExpandConstant('{pf32}\Microsoft\EdgeWebView\Application')) or
    DirExists(ExpandConstant('{pf}\Microsoft\EdgeWebView\Application')) or
    FileExists(ExpandConstant('{pf32}\Microsoft\Edge\Application\msedge.exe')) or
    FileExists(ExpandConstant('{pf}\Microsoft\Edge\Application\msedge.exe')) or
    RegQueryStringValue(HKLM64, WV2_KEY, 'pv', Ver) or
    RegQueryStringValue(HKLM, WV2_KEY, 'pv', Ver) or
    RegQueryStringValue(HKCU, WV2_KEY, 'pv', Ver) or
    RegQueryStringValue(HKLM64, EDGE_KEY, 'pv', Ver) or
    RegQueryStringValue(HKLM, EDGE_KEY, 'pv', Ver) or
    RegQueryStringValue(HKCU, EDGE_KEY, 'pv', Ver);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  { Se lee ANTES de instalar: despues el registro ya apunta a la nueva ruta. }
  OldAppPath := GetPreviousAppPath();
  if not MotorWebDisponible() then
    MsgBox('No se detecto WebView2 ni Microsoft Edge. CA-CO lo necesita para el visor PDF.' + #13#10 +
      'Instala WebView2 Evergreen (gratis, Microsoft) y vuelve a abrir CA-CO.',
      mbInformation, MB_OK);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if (CurStep = ssDone) and (ExpandConstant('{param:relaunch|0}') = '1') then
  begin
    { Actualización desde la app: reabrir al terminar (el instalador ya cerró la anterior). }
    Exec(ExpandConstant('{app}\CaCo.App.exe'), '', '', SW_SHOW, ewNoWait, Code);
  end;
  if CurStep = ssPostInstall then
  begin
    { Actualizacion con cambio de carpeta: la instalacion anterior queda
      huerfana (mismo AppId, el registro ya apunta aqui). Si parece nuestra
      (contiene CaCo.App.exe), se elimina para no dejar duplicados. Los
      accesos directos ya se reescribieron al nuevo destino. }
    if (OldAppPath <> '') and (CompareText(OldAppPath, ExpandConstant('{app}')) <> 0)
      and DirExists(OldAppPath)
      and FileExists(OldAppPath + '\CaCo.App.exe')
      and FileExists(OldAppPath + '\unins000.exe') then
    begin
      DelTree(OldAppPath, True, True, True);
    end;
  end;
end;
