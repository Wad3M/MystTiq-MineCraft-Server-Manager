# MystMC v2.2.0: Run Multiple Servers

## Multiple servers at once
- Each server profile now has its own runtime (`Core/ServerInstance.cs`): its own Java process, console history, online-player list, and uptime.
- Start a server, select another one, and start that too. The toolbar (Start, Stop, Restart, Kill, Backup), Console, Players, and Performance pages act on the **selected** server.
- The Dashboard has a new **All Servers** card listing every server with its port, memory, and state, plus Start/Stop and Select buttons.
- Running servers show a `●` next to their name in the server picker.
- The footer shows how many servers are running.

## Port conflict check
- Starting a server is refused when its port is already used by another running MystMC server or by any other program on the machine. The error names the conflict and suggests changing `server-port`.

## Safer exit and recovery
- Closing MystMC lists every running server and stops them all in parallel. Any that don't stop within 20 seconds can be force killed.
- Process recovery now remembers every running server (`runtime-sessions.json`), not just one. The old single-server `runtime-session.json` is migrated automatically.

## Scheduled tasks
- Start, stop, restart, broadcast, command, and backup tasks now target their own server directly, without changing the selected server or being blocked by another running server.

## Build
- Added a GitHub Actions workflow that builds the app on Windows for every push and pull request.
