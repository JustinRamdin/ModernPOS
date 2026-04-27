#define MyAppName "ModernPOS Terminal"
#define MyAppExeName "Pos.Terminal.exe"
#define MyAppVersion "1.0.1"

[Setup]
AppId={{A03D1170-8A8D-4F85-BD6C-8C4F9F822D95}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\ModernPOS\Terminal
DefaultGroupName=ModernPOS
OutputBaseFilename=ModernPOS-Terminal-1.0.1
OutputDir=Output
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern

[Files]
Source: "publish\Pos.Terminal\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ModernPOS Terminal"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ModernPOS Terminal"; Flags: nowait postinstall skipifsilent
