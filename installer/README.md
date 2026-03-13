# Installer-ready assets

This folder provides a single-installer flow with two install types:

- **Server** → installs `Pos.ServerApp`
- **Client** → installs `Pos.Terminal`

Use `ModernPOS.InnoSetup.iss` with published outputs:

- `publish/Pos.ServerApp`
- `publish/Pos.Terminal`

The server option launches first-run setup (company, super user, port), while the client option launches LAN server selection and login.
