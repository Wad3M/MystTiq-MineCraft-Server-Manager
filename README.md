# MystMC

A simple Windows app for creating and running Minecraft servers.

Pick a server type and version, give it a name, and MystMC downloads it, sets it up, and gives you a start button and a console. The main goal is to **run several different servers side by side**, for example a Paper survival server, a Fabric modded server, and a Vanilla test world, each on its own port, from one window.

> **Status:** Current version is v2.1.5. Right now MystMC runs **one server at a time**. Running several servers at once is the next major goal (see [Roadmap](#roadmap)).

## Features

- **One-click server setup.** Downloads Vanilla, Paper, Purpur, Folia, or Fabric, or uses your own JAR. It accepts the EULA and writes a starting `server.properties` for you.
- **Many server profiles.** Each server has its own folder, type, version, memory limit, and Java path.
- **Start, stop, restart, and a live console.** Graceful stop, with a warning before any force kill.
- **Backups.** Manual or scheduled, with a retention limit and automatic safety backups before updates.
- **Plugins and datapacks.** Install from Modrinth or from a file, then enable, disable, or remove them.
- **Worlds and players.** Import, rename, and archive worlds. Op, kick, or ban players.
- **Crash recovery.** Restarts a crashed server automatically, up to a limit.
- **Diagnostics.** Health, log, startup, and performance checks. They only read files and never change a server unless you tell them to.

## Requirements

- Windows 10 or 11 (x64)
- [.NET 10 SDK](https://dotnet.microsoft.com/download), needed only to build from source
- Java installed and on `PATH`, or set per server. Newer Minecraft versions need Java 21.

## Quick start

```bat
Build-And-Run.bat
```

This restores, builds, and launches the app in Release mode.

To build a single self-contained `.exe`:

```bat
Publish-Windows.bat
```

The output is in `src\ASimpleMinecraftServer\bin\Release\net10.0-windows\win-x64\publish\`.

## Running more than one server

Every Minecraft server needs its own **port**. The default is `25565`. When you create a second server, give it a different `server-port` in its settings (for example `25566`, `25567`, and so on). The server list shows each server's port.

Also keep an eye on memory. Each server reserves the RAM set in its profile, so three 4 GB servers need at least 12 GB free.

## Where things are stored

| What | Where |
|---|---|
| Server list and app settings | `%APPDATA%\ASimpleMinecraftServer\servers.json` |
| Server files | The install folder you pick, one subfolder per server |
| Backups | `C:\GameServers\_Backups`, or `%APPDATA%\ASimpleMinecraftServer\Backups` if that folder can't be created |
| Startup errors | `MystMC_startup.log` next to the executable |

## Project layout

```
src/ASimpleMinecraftServer/
  PrototypeWindow.xaml(.cs)   Main window (the UI shell)
  Core/                       Backend: launcher, profiles, backups, plugins, worlds
  Installers/                 One installer per server type
  Services/                   Downloads, version lookup, Java detection, health checks
  Models/                     Data types (ServerProfile, BackupRecord, and so on)
  Themes/                     WPF themes and styles
  UI/                         Pages, dialogs, navigation
docs/
  releases/                   Release notes for every version
  validation/                 Build and merge validation records
  RELEASE_CHECKLIST.md        Manual test checklist before a release
```

`MainWindow.xaml*` is the old v1 window. It is kept for reference and excluded from the build.

## Roadmap

1. **Run multiple servers at once.** Give each server its own launcher and console, with start and stop controls in the server list and a combined dashboard showing all running servers.
2. **Port conflict check.** Warn before starting a server whose port is already in use.
3. **Tidy up.** Remove the old v1 window and simplify the UI shell.

## Release notes

See [`docs/releases/`](docs/releases/). The latest is [v2.1.5](docs/releases/RELEASE_NOTES_v2.1.5.md).
