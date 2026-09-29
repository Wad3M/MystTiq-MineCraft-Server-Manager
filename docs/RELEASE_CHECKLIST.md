# Release Checklist

Test on Windows with the build you are about to release.

## Basics
- [ ] `Build-And-Run.bat` builds and opens the app.
- [ ] The title bar and About page show the new version.
- [ ] Every sidebar page and every tab opens without an error, and icons display in all three themes.

## Servers
- [ ] Create a new server (download a Paper or Vanilla JAR) and add an existing server folder.
- [ ] Start, stop, restart, and kill a server. Console output appears and commands work.
- [ ] Run two servers on different ports at the same time. Switching the server picker switches the console, players, and performance views.
- [ ] Starting a server on a port that's already in use shows an error and doesn't start it.
- [ ] Closing the app with servers running offers to stop them all, and does.

## Data
- [ ] Manual backup and restore work. Scheduled backups run.
- [ ] Plugins and datapacks install, enable, disable, and delete.
- [ ] Health, Log, Startup, Performance, and Optimization tools run and don't change server files.

## Publish
- [ ] The release `.exe` from GitHub Releases starts on a machine without the .NET SDK.
