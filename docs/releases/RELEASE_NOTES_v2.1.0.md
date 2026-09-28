# MystMC v2.1.2 — Native Backend Integration

## Architecture
- Removed the runtime dependency on the legacy `MainWindow`.
- `MainWindow.xaml`, `MainWindow.xaml.cs`, and `MainWindow.ExternalHost.cs` remain in source history but are excluded from compilation.
- Added `Core/NativeBackendController.cs` as the service/controller layer used directly by the polished v1.8.8 shell.
- The visible `PrototypeWindow` is now the real production window and owns application lifetime, dialogs, navigation, selected-server state, and toolbar actions.

## Live server integration
- Loads real profiles directly through `ProfileStore`.
- Restores a prior Java process through `RuntimeSessionStore` when possible and clearly labels it as recovered/limited control.
- Start, graceful Stop, Restart, Force Kill, Backup, Console, File Manager, and Settings call backend services directly.
- Graceful Stop no longer silently escalates to Kill in the shell workflow; the user is warned before force termination.
- Selected Server and Running Server are tracked independently and shown explicitly when they differ.
- Shutdown is controlled by the polished shell and asks before stopping/killing a running server.

## Dashboard
- Removed hard-coded sample server/player/TPS/MSPT/CPU/memory/world/plugin/backup values.
- Shows live player count, Java process CPU, working set, uptime, selected server data, real world/plugin/backup counts, and recent console output.
- TPS/MSPT are displayed as unavailable unless authoritative server telemetry exists; MystMC does not fabricate them.

## Functional pages
- Create/import server profiles
- Live Console and commands
- Worlds scan/import/rename/archive/delete
- Players list, OP, Kick, Ban
- Plugin JAR install/enable/disable/delete
- Datapack install/enable/disable/delete
- Resource-pack ZIP validation, SHA-1 calculation, and server.properties configuration
- Built-in server templates with preview/apply
- Plugin Packs with Modrinth compatible-build resolution and installation
- Migration preview/copy
- Process performance
- Health analyzer
- Log analyzer
- Startup analyzer
- Optimization recommendations
- Scheduled tasks with persistent storage and due-task execution
- Manual and automatic backups with retention
- Profile and server.properties settings
- Help/About/UI Preview

## Reliability
- Player collection updates are thread-safe; WPF no longer binds directly to a collection mutated by Java output callbacks.
- Startup errors remain visible and are written to `MystMC_startup.log`.
- Background scheduler/automatic-backup maintenance failures are logged instead of terminating the UI.
