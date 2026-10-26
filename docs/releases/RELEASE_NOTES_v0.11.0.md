# MystTiq Minecraft Server Manager v0.11.0

## Forge and NeoForge servers
**Forge** and **NeoForge** are now server types in Create Server, next to Vanilla, Paper, Purpur, Folia and Fabric.

- **Install:**
  - MystTiq downloads the official installer from `maven.minecraftforge.net` or `maven.neoforged.net` and checks it against the SHA-1 checksum the Maven repository publishes.
  - It then runs the installer once with Java (`--installServer`). This takes a few minutes because the installer downloads Minecraft and the loader's libraries. The installer is deleted afterwards.
  - The newest stable loader build for the chosen Minecraft version is used. A beta is used only when that version has no stable build.
- **Start:**
  - Modern Forge and NeoForge start from the `win_args.txt` argument file the installer writes, and MystTiq launches them that way.
  - Your `user_jvm_args.txt` is still applied, and the memory you set in MystTiq wins.
  - Older Forge versions that ship a `forge-….jar` still work.
- **Add Existing Server** recognizes Forge and NeoForge folders. It also reads the Minecraft version from the installed loader.
- **Mods:**
  - Searching Modrinth on Forge and NeoForge servers shows mods for that loader.
  - Mod JARs are read from `META-INF/mods.toml` or `neoforge.mods.toml`, and each mod's name and version are shown.
  - MystTiq refuses a mod built for the wrong loader. NeoForge servers also accept Forge mods, because older ones still load them.
- **Modpacks:** Forge and NeoForge `.mrpack` modpacks can now be imported, using the exact loader version the pack asks for.
- **Bedrock crossplay** works on NeoForge (Geyser-NeoForge). Geyser has no Forge version, so the Crossplay tab says so on Forge servers.
- The "Install Fabric API" hint is now shown only on Fabric servers.
