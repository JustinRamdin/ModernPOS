#define MyAppName "ModernPOS Server App"
#define MyAppExeName "Pos.ServerApp.exe"
#define MyAppVersion "1.0.1"

[Setup]
AppId={{462CA6B6-927E-48BA-87D7-4A94E7FCF4F4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\ModernPOS\ServerApp
DefaultGroupName=ModernPOS
OutputBaseFilename=ModernPOS-ServerApp-1.0.1
OutputDir=Output
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern

[Files]
Source: "publish\Pos.ServerApp\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ModernPOS Server App"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch ModernPOS Server App"; Flags: nowait postinstall skipifsilent
