# MystTiq Minecraft Server Manager v0.8.0

## Modrinth modpack import
Create a whole modded server from one `.mrpack` file, the format Modrinth modpacks are downloaded in.

On **Create Server**:
1. Fill in the name, install folder, memory and port.
2. Tick the EULA box.
3. Click **Install Modrinth Modpack (.mrpack)…** and choose the file.

MystTiq then:
- installs the exact Minecraft version and **Fabric loader version** the pack asks for,
- downloads every file the pack marks for servers, and skips client-only files such as shaders and minimaps,
- applies the pack's `overrides` and then its `server-overrides` (config files and so on).

Players still need the same modpack installed in their own game to join.

### Safety
- Every file is checked against the pack's SHA-512 hash, and a mismatch stops the install.
- Downloads only come over HTTPS from the hosts the Modrinth format allows: `cdn.modrinth.com`, `github.com`, `raw.githubusercontent.com` and `gitlab.com`.
- A pack can't write outside the server folder. File paths that try to (`..`, absolute paths, drive letters) are refused.
- If an install fails partway, the half-built server is removed so you can simply try again.

### Not yet
- Forge, NeoForge and Quilt modpacks are recognised, but the app says it can't install them yet. Forge and NeoForge come in 0.11.0.
