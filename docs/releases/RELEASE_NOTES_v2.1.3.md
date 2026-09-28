# MystMC v2.1.3 — Server Creation & Download Installer

## Server creation fixes
- Replaced the v2 Create Server profile-only workflow with the real ServerInstaller pipeline.
- Server name now automatically determines the server directory name.
- Added a separate Install Root field and read-only generated Server Folder preview.
- New installs refuse to overwrite a non-empty generated folder; existing servers should use Add Existing Server.
- A server profile is only added after the server JAR installation succeeds.

## Automatic server downloads
Supported automatic server types:
- Vanilla (Mojang server JAR)
- Paper (PaperMC downloads service)
- Purpur (PurpurMC downloads API)
- Folia (PaperMC downloads service)
- Fabric (Fabric executable server launcher)
- Custom JAR (local file copied as server.jar)

The Minecraft Version control now loads the full version list available from the selected provider. It remains editable so a version can be entered manually if provider enumeration fails.

## Installation behavior
MystMC now:
1. Generates the server folder from Install Root + Server Name.
2. Downloads/copies the selected server implementation as server.jar.
3. Writes eula.txt with eula=true.
4. Creates a baseline server.properties when one is not already present.
5. Saves the new server profile only after installation succeeds.
6. Selects the new server automatically.

## Other compatibility updates
- Folia is treated as Bukkit-compatible for plugin packs/health checks.
- Existing-server detection recognizes Folia and Fabric JAR names.
- Version metadata bumped consistently to 2.1.3.
