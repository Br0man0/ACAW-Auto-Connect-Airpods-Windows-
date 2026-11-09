; Installateur d'ACAW (Inno Setup 6, https://jrsoftware.org/isinfo.php).
; Construit par build/build-release.ps1, qui passe la version et les dossiers :
;   ISCC /DAppVersion=0.4.0 /DSourceDir=<publication> /DOutputDir=<sortie> installer\acaw.iss
; Explications : docs/guide/09-publier-une-version.md et ADR-0005.
;
; Choix (ADR-0005) : installation POUR L'UTILISATEUR SEUL, sans droits administrateur, dans
; %LOCALAPPDATA%\Programs\ACAW ; jamais de paquet MSIX (il redirigerait AppData).

#ifndef AppVersion
  #error Passer la version : ISCC /DAppVersion=x.y.z (lue dans Directory.Build.props par build-release.ps1)
#endif
#ifndef SourceDir
  #error Passer le dossier publié : ISCC /DSourceDir=...
#endif
#ifndef OutputDir
  #define OutputDir "."
#endif

[Setup]
; Identifiant fixe : c'est lui qui fait qu'une nouvelle version remplace l'ancienne. Ne jamais le changer.
AppId={{8E3F1C2A-5B7D-4A9E-9C61-2F4D7B8A1E05}
AppName=ACAW
AppVersion={#AppVersion}
AppVerName=ACAW {#AppVersion}
AppPublisher=Romsy
AppComments=Bascule automatique des AirPods entre ce PC et l'iPhone
VersionInfoVersion={#AppVersion}
DefaultDirName={userpf}\ACAW
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename=acaw-setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayName=ACAW
UninstallDisplayIcon={app}\acaw-tray.exe
; ACAW est arrêté proprement par « acaw quit » (voir [Code]) : pas besoin du gestionnaire de redémarrage.
CloseApplications=no
SetupLogging=yes

[Languages]
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"

[Tasks]
Name: "startup"; Description: "Démarrer ACAW avec Windows"

[InstallDelete]
; Restes d'une installation faite à la main avec « dotnet publish » (fichiers de débogage).
Type: files; Name: "{app}\*.pdb"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\ACAW"; Filename: "{app}\acaw-tray.exe"; Comment: "Bascule automatique des AirPods"

[Run]
; Même code que la case « Démarrer avec Windows » du menu de l'icône (StartupShortcut).
Filename: "{app}\acaw.exe"; Parameters: "startup on"; Flags: runhidden; Tasks: startup; StatusMsg: "Démarrage avec Windows…"
Filename: "{app}\acaw.exe"; Parameters: "startup off"; Flags: runhidden; Tasks: not startup
Filename: "{app}\acaw-tray.exe"; Description: "Lancer ACAW"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\acaw.exe"; Parameters: "startup off"; Flags: runhidden; RunOnceId: "StartupOff"

[Code]
// Arrête ACAW avant de remplacer ou de supprimer ses fichiers : d'abord proprement (« acaw quit » : l'hôte
// s'arrête et rend le son qu'il aurait coupé), puis de force en dernier recours (version trop ancienne
// pour connaître « quit », ou ACAW bloqué).
procedure StopAcaw();
var
  ResultCode: Integer;
begin
  if FileExists(ExpandConstant('{app}\acaw.exe')) then
    Exec(ExpandConstant('{app}\acaw.exe'), 'quit', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM acaw-tray.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopAcaw();
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  StopAcaw();
  Result := True;
end;

// Les réglages (%APPDATA%\ACAW) et le journal (%LOCALAPPDATA%\ACAW) ne sont pas dans {app} : on demande.
// En désinstallation silencieuse, on les garde.
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    if MsgBox('Supprimer aussi les réglages et le journal d''ACAW ?' + #13#10 +
              '(Choix des AirPods, délais, journal. Répondre Non pour les retrouver après une réinstallation.)',
              mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    begin
      DelTree(ExpandConstant('{userappdata}\ACAW'), True, True, True);
      DelTree(ExpandConstant('{localappdata}\ACAW'), True, True, True);
    end;
end;
