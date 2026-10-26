# MystTiq Minecraft Server Manager v0.13.0

## Update server software
**Settings** has a new **Update Server Software** card. You no longer need to create a new server to update one.

- **Newest build:** pick the server's current Minecraft version to get the newest Paper, Purpur, Folia, Fabric, Forge or NeoForge build for it.
- **Newer Minecraft:** pick a newer version to move the server and its world up to it.
- **Safe by default:**
  - A full backup (`Safety-BeforeUpdate`) is made first. Restore it from Backups if anything goes wrong.
  - Server JARs are downloaded and checked before anything is replaced, so a failed download leaves the server as it was.
  - The old JAR is kept next to the new one as `*.previous`.
  - Older versions aren't offered, because Minecraft worlds can't be opened by an older version.
- **Add-on check:**
  - Before moving to a new Minecraft version, MystTiq checks each installed plugin or mod on Modrinth.
  - It lists any that have no release for that version yet, or that it couldn't find, so you can decide before updating.
- Forge and NeoForge re-run their official installer in place. MystTiq then switches the server to the new version's launch file. If the new version needs a newer Java, MystTiq offers to download it.
- The server must be stopped to update.

## Fixes
- The add-on update lookup now searches for mods on Fabric, Forge and NeoForge servers. It used to search only for plugins.
- Forge and NeoForge servers start the most recently installed loader version when several are in the folder.
