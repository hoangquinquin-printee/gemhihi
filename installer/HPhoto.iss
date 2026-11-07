#define AppName "gemhihi"
#define AppVersion "1.0.25"
#define AppPublisher "gemhihi"
#define AppExeName "gemhihi.exe"

[Setup]
AppId={{D83A6A5C-5408-4F6B-9CD7-6B5C7261D2D8}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\gemhihi
DefaultGroupName={#AppName}
OutputDir=..\artifacts\installer
OutputBaseFilename=gemhihi-Setup-1.0.25
SetupIconFile=..\src\PatternDigitizerPro.UI\Assets\gemhihi.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#AppExeName}

[Files]
Source: "..\artifacts\release\gemhihi-1.0.25\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion; Excludes: "*.pdb"

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\gemhihi"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
