; The Windows installer, built by Inno Setup 6 from the published editor:
;   ISCC /DVersion=<version> /DSource=<published editor folder> /DOutput=<folder> packaging\windows\talesmith.iss
; It installs for the current user without administrator rights, and offers an installation for everyone in its first dialog.

#ifndef Version
  #error Pass the version with /DVersion=
#endif
#ifndef Source
  #error Pass the published editor's folder with /DSource=
#endif
#ifndef Output
  #define Output "."
#endif

[Setup]
; Upgrades find an earlier installation by this id, so it never changes.
AppId={{EC0842C9-E6E8-43FD-9774-6B906F41204A}
AppName=Talesmith
AppVersion={#Version}
AppVerName=Talesmith {#Version}
AppPublisher=Dylan de Beer
AppPublisherURL=https://talesmith.dev
AppSupportURL=https://github.com/CodeByDylan/Talesmith/issues
AppUpdatesURL=https://github.com/CodeByDylan/Talesmith/releases
DefaultDirName={autopf}\Talesmith
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\..\LICENSE
SetupIconFile=..\..\src\Talesmith.App\talesmith.ico
UninstallDisplayIcon={app}\talesmith.exe
UninstallDisplayName=Talesmith
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
OutputDir={#Output}
OutputBaseFilename=talesmith-{#Version}-win-x64-setup

[Tasks]
Name: desktopicon; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; An upgrade replaces the players and license texts as a whole, so none of the previous version's files stay behind.
Type: filesandordirs; Name: "{app}\players"
Type: filesandordirs; Name: "{app}\licenses"

[Files]
Source: "{#Source}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Talesmith"; Filename: "{app}\talesmith.exe"
Name: "{autodesktop}\Talesmith"; Filename: "{app}\talesmith.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\talesmith.exe"; Description: "{cm:LaunchProgram,Talesmith}"; Flags: nowait postinstall skipifsilent
