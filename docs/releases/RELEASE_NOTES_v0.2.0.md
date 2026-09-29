# MystTiq Minecraft Server Manager v0.2.0

The first release from this repository. Version numbers restart at 0.2.0; this build follows the old MystMC v2.1.5.

## Run several servers at once
- Each server has its own Java process, console history, online-player list, and uptime.
- Start a server, select another, and start that one too. The toolbar (Start, Stop, Restart, Kill, Backup) and the Console, Players, and Performance pages act on the **selected** server.
- The Dashboard has a new **All Servers** card listing every server with its port, memory, and state, plus Start/Stop and Select buttons.
- Running servers show a `●` next to their name in the server picker, and the footer shows how many are running.

## Port conflict check
- A server won't start if its port is already used by another running server or by any other program on the machine. The error names the conflict and suggests changing `server-port`.

## Safer exit and recovery
- Closing the app lists every running server and stops them all at once. Any that don't stop within 20 seconds can be force killed.
- Process recovery remembers every running server (`runtime-sessions.json`), not just one. The old single-server file is migrated automatically.

## Scheduled tasks
- Start, stop, restart, broadcast, command, and backup tasks target their own server directly, without changing the selected server.

## New icons
- The UI uses the MystCraft icon pack: 200 Minecraft-style icons (compass, beacon, command block, redstone torch and more) instead of the Windows glyph font.

## Build and release
- A GitHub Actions build runs on Windows for every push and fails on any compiler warning.
- Tagged versions publish a self-contained Windows `.exe` to GitHub Releases automatically.

## Known limitations
- Crash auto-restart settings exist per server but are not wired up yet.
- The `.exe` is not code-signed, so Windows SmartScreen shows a warning on first run.
