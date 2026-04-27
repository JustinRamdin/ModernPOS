# Building installers for `Pos.Terminal` and `Pos.ServerApp`

This repo uses **Inno Setup** scripts to package both desktop apps.

## 1) Publish both apps (self-contained win-x64)

Run these commands from the repository root:

```bash
dotnet publish src/Pos.Terminal/Pos.Terminal.csproj -c Release -r win-x64 --self-contained true -o publish/Pos.Terminal
dotnet publish src/Pos.ServerApp/Pos.ServerApp.csproj -c Release -r win-x64 --self-contained true -o publish/Pos.ServerApp
```

## 2) Build installer EXEs with Inno Setup

Use `ISCC.exe` (Inno Setup Compiler) against each `.iss` file:

```bash
iscc terminal-only.iss
iscc serverapp-only.iss
```

On a default Windows install, `ISCC.exe` is usually here:

`C:\Program Files (x86)\Inno Setup 6\ISCC.exe`

## 3) Output location

Both installer EXEs are written to the `Output/` directory:

- `Output/ModernPOS-Terminal-1.0.1.exe`
- `Output/ModernPOS-ServerApp-1.0.1.exe`

## 4) Versioning

When releasing a new version:

1. Update `MyAppVersion` in `terminal-only.iss`.
2. Update `MyAppVersion` in `serverapp-only.iss`.
3. (Optional, recommended) also update `OutputBaseFilename` in both files to match.
