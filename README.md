# MystMC

A simple Windows app for creating and running Minecraft servers.

Pick a server type and version, give it a name, and MystMC downloads it, sets it up, and gives you a start button and a console. The main goal is to **run several different servers side by side**, for example a Paper survival server, a Fabric modded server, and a Vanilla test world, each on its own port, from one window.

> **Status:** v2.2.0 adds multi-server support. This is new, so please report anything odd.

## Features

- **One-click server setup.** Downloads Vanilla, Paper, Purpur, Folia, or Fabric, or uses your own JAR. It accepts the EULA and writes a starting `server.properties` for you.
- **Run several servers at once.** Each server has its own folder, version, memory limit, Java path, console, and player list. The dashboard shows them all with Start/Stop buttons.
- **Port conflict check.** A server won't start if its port is already taken.
- **Start, stop, restart, and a live console.** Graceful stop, with a warning before any force kill.
- **Backups.** Manual or scheduled, with a retention limit and automatic safety backups before updates.
- **Plugins and datapacks.** Install from Modrinth or from a file, then enable, disable, or remove them.
- **Worlds and players.** Import, rename, and archive worlds. Op, kick, or ban players.
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

1. Create or add each server.
2. Give each one its own `server-port` in its settings (for example `25565`, `25566`, `25567`). MystMC refuses to start a server whose port is already in use.
3. Start them from the **All Servers** card on the Dashboard, or select one in the server picker and press **Start**.

The server picker at the top chooses which server the toolbar, Console, Players, and Performance pages control. Running servers show a `●` next to their name.

Keep an eye on memory. Each server reserves the RAM set in its profile, so three 4 GB servers need at least 12 GB free.

When you close MystMC it offers to stop every running server safely first.

## Where things are stored

| What | Where |
|---|---|
| Server list and app settings | `%APPDATA%\ASimpleMinecraftServer\servers.json` |
| Running server processes (for recovery) | `%APPDATA%\ASimpleMinecraftServer\runtime-sessions.json` |
| Server files | The install folder you pick, one subfolder per server |
| Backups | `C:\GameServers\_Backups`, or `%APPDATA%\ASimpleMinecraftServer\Backups` if that folder can't be created |
| Startup errors | `MystMC_startup.log` next to the executable |

## Project layout

```
src/ASimpleMinecraftServer/
  PrototypeWindow.xaml(.cs)   Main window (the UI shell)
  Core/                       Backend: per-server instances, launcher, profiles, backups, plugins
  Installers/                 One installer per server type
  Services/                   Downloads, version lookup, Java detection, health checks
  Models/                     Data types (ServerProfile, BackupRecord, and so on)
  Themes/                     WPF themes and styles
  Assets/Icons/               MystCraft icon pack (64×64 PNG), mapped in PrototypeWindow.xaml.cs
  UI/                         Pages, dialogs, navigation
docs/
  releases/                   Release notes for every version
  validation/                 Build and merge validation records
  icons/                      Icon pack manifest and design spec
  RELEASE_CHECKLIST.md        Manual test checklist before a release
```

`MainWindow.xaml*` is the old v1 window. It is kept for reference and excluded from the build.

## Roadmap

1. **Crash auto-restart.** The per-server crash recovery settings exist but are not wired up yet.
2. **Suggest a free port** when creating a new server.
3. **Tidy up.** Remove the old v1 window and simplify the UI shell.

## Release notes

See [`docs/releases/`](docs/releases/). The latest is [v2.2.0](docs/releases/RELEASE_NOTES_v2.2.0.md).
