# Installer-ready assets

This folder provides a single-installer flow with two install types:

- **Server** → installs `Pos.ServerApp`
- **Client** → installs `Pos.Terminal`

Use `ModernPOS.InnoSetup.iss` with published outputs:

- `publish/Pos.ServerApp`
- `publish/Pos.Terminal`

The server option launches first-run setup (company, super user, port), while the client option launches LAN server selection and login.

## Build one installer (`ModernPOS-Setup.exe`)

From the repository root:

1. Publish the Server app output:

   ```bash
   dotnet publish src/Pos.ServerApp/Pos.ServerApp.csproj -c Release -r win-x64 --self-contained true -o publish/Pos.ServerApp
   ```

2. Publish the Terminal app output:

   ```bash
   dotnet publish src/Pos.Terminal/Pos.Terminal.csproj -c Release -r win-x64 --self-contained true -o publish/Pos.Terminal
   ```

3. Compile the Inno Setup script:

   ```bash
   iscc installer/ModernPos.InnoSetup.iss
   ```

The generated installer is named `ModernPOS-Setup.exe` (from `OutputBaseFilename`) and is produced by Inno Setup in its output folder.

### Notes

- The `.iss` file expects these exact publish folders:
  - `publish/Pos.ServerApp`
  - `publish/Pos.Terminal`
- If `iscc` is not in PATH, run it via full path, e.g.:

  ```bash
  "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\ModernPos.InnoSetup.iss
  ```