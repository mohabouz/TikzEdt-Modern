#ifndef AppVersion
  #define AppVersion "0.3.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\artifacts\installer-payload\win-x64"
#endif
#ifndef SetupOutputDir
  #define SetupOutputDir "..\artifacts\setup"
#endif

[Setup]
AppId={{8B743F29-7F43-4B02-B2CA-F7F5CE68B7B8}
AppName=TikzEdt Modern
AppVersion={#AppVersion}
AppVerName=TikzEdt Modern {#AppVersion} (Preview)
AppPublisher=TikzEdt contributors
VersionInfoVersion={#AppVersion}.0
DefaultDirName={localappdata}\Programs\TikzEdt Modern
DefaultGroupName=TikzEdt Modern
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputDir={#SetupOutputDir}
OutputBaseFilename=TikzEdt-Modern-{#AppVersion}-Setup-win-x64
SetupIconFile=..\TikzEdt.Modern\Assets\Branding\TikzEdt.ico
UninstallDisplayIcon={app}\TikzEdt.Modern.exe
LicenseFile=..\LICENSE
InfoBeforeFile=PREREQUISITES.txt
WizardStyle=modern
Compression=lzma2/normal
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
Uninstallable=yes
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "PREREQUISITES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\TikzEdt Modern"; Filename: "{app}\TikzEdt.Modern.exe"; WorkingDir: "{userdocs}"
Name: "{group}\Uninstall TikzEdt Modern"; Filename: "{uninstallexe}"
Name: "{autodesktop}\TikzEdt Modern"; Filename: "{app}\TikzEdt.Modern.exe"; WorkingDir: "{userdocs}"; Tasks: desktopicon

[Run]
Filename: "{app}\TikzEdt.Modern.exe"; Description: "Launch TikzEdt Modern"; WorkingDir: "{userdocs}"; Flags: nowait postinstall skipifsilent

; User documents, LocalAppData preferences, preamble files and the Temp preview
; cache are deliberately NOT included in UninstallDelete.
