#define MyAppName "ModernPOS"
#define MyAppVersion "1.0.0"

[Setup]
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\ModernPOS
DefaultGroupName=ModernPOS
OutputBaseFilename=ModernPOS-Setup
Compression=lzma
SolidCompression=yes

[Types]
Name: "server"; Description: "Install Server";
Name: "client"; Description: "Install Client";

[Files]
Source: "..\publish\Pos.ServerApp\*"; DestDir: "{app}\ServerApp"; Flags: recursesubdirs createallsubdirs; Types: server
Source: "..\publish\Pos.Terminal\*"; DestDir: "{app}\Terminal"; Flags: recursesubdirs createallsubdirs; Types: client

[Icons]
Name: "{group}\ModernPOS Server"; Filename: "{app}\ServerApp\Pos.ServerApp.exe"; Types: server
Name: "{group}\ModernPOS Terminal"; Filename: "{app}\Terminal\Pos.Terminal.exe"; Types: client

[Run]
Filename: "{app}\ServerApp\Pos.ServerApp.exe"; Description: "Launch ModernPOS Server"; Flags: nowait postinstall skipifsilent; Types: server
Filename: "{app}\Terminal\Pos.Terminal.exe"; Description: "Launch ModernPOS Terminal"; Flags: nowait postinstall skipifsilent; Types: client
