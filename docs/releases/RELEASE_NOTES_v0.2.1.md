# MystTiq Minecraft Server Manager v0.2.1

Fixes found in the post-0.2.0 review. These make multi-server setups work out of the box and make downloads safer.

## Creating servers
- **Port field.** Create Server now has a Port field, pre-filled with the next free port (starting at 25565), skipping ports used by other servers or other programs. Before, every new server got 25565, so a second server always hit the port-conflict error.
- **Minecraft EULA.** You now have to tick "I have read and accept the Minecraft End User License Agreement" (with a link to it) before Install is enabled. Before, the app accepted the EULA for you.

## Safer downloads
- **Server JARs are checked** against the checksum each provider publishes: SHA-1 for Vanilla (Mojang), SHA-256 for Paper and Folia (PaperMC), and MD5 for Purpur (its API offers nothing stronger; the exact build is pinned so the checksum matches). A file that fails the check is deleted and the install stops. Fabric publishes no checksum for its server launcher, so it relies on HTTPS alone.
- **Modrinth plugins are checked** against Modrinth's SHA-512 hash before they are installed.
- **Plugin Packs only install exact matches.** Each plugin is looked up by its Modrinth project ID. Before, if the exact project wasn't found, the top search result was installed instead, which could be an unrelated plugin. Plugins that aren't on Modrinth, or have no build for your Minecraft version, are now skipped and listed after the install.
- The app now identifies itself to Modrinth and PaperMC with its name and repository link, as their APIs ask.

## Starting servers
- **Java is checked before a server starts.** If Java can't be found, the app says so and how to fix it. If the Java version is older than the Minecraft version needs (Java 21 for 1.20.5+, Java 25 for 26.x), it asks before starting.
