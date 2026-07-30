; Buckett installer (Inno Setup 6).
;
; Built by make_app.ps1, which passes the version and the published folder:
;   ISCC.exe /DAppVersion=1.7.4 installer.iss
;
; Installs per-user into %LOCALAPPDATA%\Programs\Buckett. That is deliberate:
; no UAC prompt, and — more importantly — the folder stays writable, so the
; app's built-in updater can swap itself in place. A Program Files install
; would need administrator rights for every update.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\dist\Buckett"
#endif

[Setup]
AppId={{7B2E4C10-9A6D-4F31-B58E-2C7A9D1E4F60}
AppName=Buckett
AppVersion={#AppVersion}
AppVerName=Buckett {#AppVersion}
AppPublisher=Mac2100
AppPublisherURL=https://github.com/Mac2100/Buckett
AppSupportURL=https://github.com/Mac2100/Buckett/issues
AppUpdatesURL=https://github.com/Mac2100/Buckett/releases
DefaultDirName={autopf}\Buckett
DefaultGroupName=Buckett
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=Buckett-Setup-{#AppVersion}
SetupIconFile=..\src\Buckett\Assets\app.ico
UninstallDisplayIcon={app}\Buckett.exe
UninstallDisplayName=Buckett
; make_app.ps1 copies the repository LICENSE here before invoking ISCC.
LicenseFile=..\dist\Buckett\LICENSE.txt
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Buckett lives in the notification area, so it may well be running during an
; upgrade; let the restart manager close it rather than failing on locked files.
CloseApplications=yes
RestartApplications=no
; Must match SingleInstance.cs. Without this, setup and — worse — uninstall
; happily ran while Buckett was still going, leaving the app resident with its
; drop target on screen after the user had removed it.
AppMutex=Buckett.SingleInstance,Global\Buckett.SingleInstance

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Buckett"; Filename: "{app}\Buckett.exe"
Name: "{autodesktop}\Buckett"; Filename: "{app}\Buckett.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Buckett.exe"; Description: "Launch Buckett"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; AppMutex asks the user to close Buckett first, and the restart manager has a
; go at it too. This is the backstop for the case that produced the bug report:
; the app still resident after an uninstall, tray icon and desktop drop target
; included, with the executable already deleted from under it. Runs before any
; files are removed. Buckett holds no unsaved documents, so there is nothing to
; lose by being firm about it.
Filename: "{sys}\taskkill.exe"; Parameters: "/IM Buckett.exe /F"; Flags: runhidden skipifdoesntexist; RunOnceId: "StopBuckett"

[UninstallDelete]
; The updater's staging area, if an update was interrupted. Account data in
; %APPDATA%\Buckett and credentials in Windows Credential Manager are left
; alone so a reinstall picks up where the user left off.
Type: filesandordirs; Name: "{app}\*.tmp"
