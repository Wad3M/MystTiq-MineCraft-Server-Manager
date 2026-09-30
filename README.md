# MystTiq Minecraft Server Manager

A simple Windows app for creating and running Minecraft servers.

Pick a server type and version, give it a name, and the app downloads it, sets it up, and gives you a start button and a console. The main goal is to **run several different servers side by side**, for example a Paper survival server, a Fabric modded server, and a Vanilla test world, each on its own port, from one window.

> **Status:** v0.4.2, an early release. Multi-server support is new, so please report anything odd.

## Features

- **One-click server setup.** Downloads Vanilla, Paper, Purpur, Folia, or Fabric (checked against the provider's published checksum), or uses your own JAR. It picks a free port and writes a starting `server.properties` for you.
- **Run several servers at once.** Each server has its own folder, version, memory limit, Java path, console, and player list. The dashboard shows them all with Start/Stop buttons.
- **Port conflict check.** A server won't start if its port is already taken.
- **Start, stop, restart, and a live console.** Graceful stop, with a warning before any force kill.
- **Backups.** Manual or scheduled, with a retention limit and automatic safety backups before updates.
- **Add-ons: plugins and mods.** Paper, Purpur, and Folia servers get plugins; Fabric servers get mods. Search Modrinth in the app (only compatible, server-side results), install with one click (SHA-512 verified), or add a JAR from a file.
- **Datapacks.** Install, enable, disable, or remove them.
- **Worlds and players.** Import, rename, and archive worlds. Op, kick, or ban players.
- **Diagnostics.** Health, log, startup, and performance checks. They only read files and never change a server unless you tell them to.
- **Minecraft-style icons.** The UI uses the MystCraft icon pack.

## Download

Get the latest `.exe` from the [Releases page](https://github.com/Wad3M/MystTiq-MineCraft-Server-Manager/releases). It is a single self-contained file, so you don't need to install .NET to run it.

Windows may show "Windows protected your PC" because the file isn't code-signed. Click **More info → Run anyway**.

To start Minecraft servers you also need **Java 21** (for example from [Adoptium](https://adoptium.net)).

## Build from source

You need Windows 10 or 11 (x64) and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

1. Get the code, either with **Code → Download ZIP** on GitHub or with:
   ```bat
   git clone https://github.com/Wad3M/MystTiq-MineCraft-Server-Manager.git
   ```
2. Open the project folder in File Explorer and double-click **`Build-And-Run.bat`**. It restores packages, builds, and launches the app. The first run takes a minute while dependencies download.

To build the single `.exe` yourself, run `Publish-Windows.bat`. The output is in `src\ASimpleMinecraftServer\bin\Release\net10.0-windows\win-x64\publish\`.

If the build fails, run `dotnet --version` in a command prompt. It should print a version starting with `10.`.

## Running more than one server

1. Create or add each server.
2. Each server needs its own port. Create Server fills in the next free one (25565, 25566, …). For servers you add from an existing folder, set `server-port` in its settings. The app refuses to start a server whose port is already in use.
3. Start them from the **All Servers** card on the Dashboard, or select one in the server picker and press **Start**.

The server picker at the top chooses which server the toolbar and every page control. Running servers show a `●` next to their name.

Keep an eye on memory. Each server reserves the RAM set in its profile, so three 4 GB servers need at least 12 GB free.

When you close the app it offers to stop every running server safely first.

## Where things are stored

| What | Where |
|---|---|
| Server list and app settings | `%APPDATA%\ASimpleMinecraftServer\servers.json` |
| Running server processes (for recovery) | `%APPDATA%\ASimpleMinecraftServer\runtime-sessions.json` |
| Server files | The install folder you pick, one subfolder per server |
| Backups | `C:\GameServers\_Backups`, or `%APPDATA%\ASimpleMinecraftServer\Backups` if that folder can't be created |
| Startup errors | `MystTiq_startup.log` next to the executable |

`ASimpleMinecraftServer` is the project's original internal name. It is kept for these folders so existing server lists carry over.

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
  UI/Pages/                   Views for server.properties, logs, startup, and optimization
docs/
  releases/                   Release notes
  icons/                      Icon pack manifest and design spec
  validation/                 Build and merge validation records from earlier versions
  RELEASING.md                How to publish a new release
  RELEASE_CHECKLIST.md        Manual test checklist before a release
```

## Versioning

The project restarted its version numbers at **0.2.0** when it moved to this repository. Earlier builds were numbered 1.x and 2.x (up to 2.2.0) under the names "A Simple Minecraft Server" and "MystMC". Their notes are kept in [`docs/releases/`](docs/releases/).

The version lives in one place: `<Version>` in `src/ASimpleMinecraftServer/ASimpleMinecraftServer.csproj`. The window title and About page read it from there.

## Roadmap

1. **Crash auto-restart.** The per-server crash recovery settings exist but are not wired up yet.
2. **Forge and NeoForge** server types, with their mods on the Add-ons page.
3. **Modpack import** from Modrinth `.mrpack` files.
4. **"How friends join" card** showing the address and port to share.
5. **Whitelist** on/off with a player list.

## Release notes

The latest is [v0.4.2](docs/releases/RELEASE_NOTES_v0.4.2.md). See [`docs/releases/`](docs/releases/) for all versions.
