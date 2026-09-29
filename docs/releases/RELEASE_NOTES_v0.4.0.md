# MystTiq Minecraft Server Manager v0.4.0

This release includes 0.3.0, which wasn't published separately. Its notes are in [RELEASE_NOTES_v0.3.0.md](https://github.com/Wad3M/MystTiq-MineCraft-Server-Manager/blob/main/docs/releases/RELEASE_NOTES_v0.3.0.md).

## New in 0.3.0: Add-ons for plugins and mods
- The **Add-ons** page manages plugins on Paper, Purpur and Folia servers and mods on Fabric servers.
- **Search Modrinth** in the app. Only server-side add-ons for your server's loader and Minecraft version are shown.
- **One-click install** of the newest compatible build, checked against Modrinth's SHA-512 hash.
- **One-click Fabric API** on Fabric servers that don't have it yet.
- **Wrong-type protection:** plugins can't go on Fabric, mods can't go on Paper, and client-only mods are refused.
- **Settings** can now edit the server type and Minecraft version, so servers added from an existing folder work with Add-ons.

## New in 0.4.0: A simpler sidebar
The sidebar goes from 22 pages to 10. Related tools are now tabs on one page, and each page remembers the tab you last used.

| Page | What's on it |
|---|---|
| Dashboard | All your servers at a glance |
| Create Server | Install a new server or add an existing one |
| Console | Live output and commands |
| **Players** | Who's online, with op, kick, and ban |
| **Worlds** | Import, rename, archive, and delete worlds |
| **Add-ons** | Tabs: Add-ons (plugins or mods), Plugin Packs, Datapacks, Resource Pack |
| **Backups** | Tabs: Backups (manual and automatic), Scheduled Tasks |
| **Health** | Tabs: Health, Performance, Logs, Startup, Optimization |
| **Settings** | Tabs: Server (profile and server.properties), Presets (formerly Templates) |
| **Help** | Getting started, troubleshooting, and version info (formerly About) |

Tabs are only loaded when you open them, so the Health page no longer runs every analyzer at once.

## Removed
- **Migration.** Add an existing server from Create Server instead, or copy the folder yourself.
- **UI Preview.** It was a design tool left over from development.
- **Underworld, Ender, and Aether themes.** Minecraft Green (the default), Dark, and Light remain. If you were using a removed theme, the app starts in Minecraft Green.

## Under the hood
- Removed about 5,700 lines of unused code and markup: the old v1 main window, 13 page views and 7 view-model/navigation classes nothing used, and unused services for notifications, update checks, migration, and logging.
- Removed unused images and theme files. The download is slightly smaller.
