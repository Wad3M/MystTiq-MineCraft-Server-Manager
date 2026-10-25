# MystTiq Minecraft Server Manager v0.5.1

Fixes from a code review, plus the project's first automated tests.

## Fixed
- **Servers no longer vanish from the list when their drive is disconnected.** Before, a server whose folder couldn't be found at startup (for example on an unplugged USB drive) was dropped, and the next save removed it for good. Now it stays in the list, marked "Folder missing".
- **A damaged server list no longer stops the app from starting.** The list is saved safely (written to a temporary file first, with a `.bak` copy of the previous version). If `servers.json` can't be read, the app uses the backup copy, sets the damaged file aside, and tells you.
- **Backups of running servers are now consistent.** The app pauses world saving (`save-off`), forces a save and waits for the server to confirm "Saved the game", makes the backup, then always turns saving back on (`save-on`). Before, the server could change world files while they were being zipped.
- **Restoring a backup takes a safety backup first.** If a restore goes wrong, the files it replaced are in a `Safety-BeforeRestore` backup.
- **Renaming a server keeps its backups.** They're moved to the new name, so they stay listed and covered by retention.
- **Add Existing Server detects more.** It picks the right JAR (`server.jar` first), recognises Purpur, Paper and Fabric folders, and reads the real Minecraft version from Paper/Purpur's `version_history.json` or a vanilla JAR. Before, the version was always blank or "Detected".
- **Restarts are more robust.** A late "process exited" signal from the old server can no longer affect the new one.

## New
- **Remove from List** (Settings → Server). It removes a server from MystTiq without touching its folder, worlds or backups.

## Tests
- Automated tests cover version and Java rules, add-on type checks, server-folder detection, the server list and session files, backups (create, restore, rename, retention) and install validation. They run on Windows for every push.
