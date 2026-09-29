# MystTiq Minecraft Server Manager v0.4.0

## A simpler sidebar
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
