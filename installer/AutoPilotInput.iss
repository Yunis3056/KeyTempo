#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\keytempo-portable"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts\keytempo-release"
#endif

[Setup]
AppId={{C84881E7-ABCB-4F56-B898-5B0A62A4D786}
AppName=KeyTempo
AppVersion={#MyAppVersion}
AppPublisher=KeyTempo contributors
DefaultDirName={localappdata}\Programs\KeyTempo
DefaultGroupName=KeyTempo
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#OutputDir}
OutputBaseFilename=KeyTempo-{#MyAppVersion}-win-x64-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\KeyTempo.exe
LicenseFile={#SourceDir}\LICENSE
CloseApplications=yes
RestartApplications=no
SetupLogging=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\KeyTempo"; Filename: "{app}\KeyTempo.exe"
Name: "{group}\User guide"; Filename: "{app}\docs\USER_GUIDE.md"
Name: "{autodesktop}\KeyTempo"; Filename: "{app}\KeyTempo.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\KeyTempo.exe"; Description: "Launch KeyTempo"; Flags: nowait postinstall skipifsilent

