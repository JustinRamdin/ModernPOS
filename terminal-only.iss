#define MyAppName "ModernPOS Terminal"
#define MyAppVersion "1.0.1"

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\ModernPOS\Terminal
DefaultGroupName=ModernPOS
OutputBaseFilename=ModernPOS-Terminal-1.0.1
Compression=lzma
SolidCompression=yes

[Files]
Source: "publish\Pos.Terminal\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{group}\ModernPOS Terminal"; Filename: "{app}\Pos.Terminal.exe"

[Run]
Filename: "{app}\Pos.Terminal.exe"; Description: "Launch ModernPOS Terminal"; Flags: nowait postinstall skipifsilent
