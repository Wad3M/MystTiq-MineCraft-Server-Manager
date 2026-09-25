# MystMC v2.1.5

Current build: Server Creation & Download Installer.

# MystMC v2.1.2

This build uses the polished v1.8.8 MystMC shell with a native service/controller backend derived from the v1.5.9 Stabilization Build2 functionality. The legacy MainWindow is not instantiated and is excluded from compilation.

# MystMC v2.0.4

MystMC is the production merge of the v1.8.8 polished MystUI shell with the v1.5.9 Stabilization Build2 Minecraft server-management backend.

# A Simple Minecraft Server v1.2.2

A Windows desktop manager for creating, configuring, running, backing up, and maintaining Minecraft servers.

## New in v1.1

The Plugin Manager now includes a built-in Modrinth browser. Select a Paper, Purpur, or Folia server, open **Plugin Manager → Browse Plugins**, search by name, and install a compatible server plugin directly into the selected server.

## Build

Run `Build-And-Run.bat` for a local development build, or `Release-Validation.ps1` for the release validation and publish workflow.


## v1.2.2 Player Management
The selected-server pane now includes live online-player monitoring with refresh, Make Admin, Kick, and Ban controls.


## v1.2 Update Center

The Update Center can check and safely install official Paper, Purpur, and Vanilla server JARs. Updates require the server to be stopped and automatically create a safety backup and previous-JAR copy.


## v1.2.5 Scheduled Backups
Automatic profile-based backups, safe live-server flushing, retention controls, and next-run status are available from the Backups page.


## v1.2.9 Stabilization

This stabilization release consolidates the v1.2 automation features: plugin updates, scheduled backups, scheduled tasks, crash recovery, and notifications. It also corrects the crash-recovery exit-code log formatting and refreshes release metadata.
