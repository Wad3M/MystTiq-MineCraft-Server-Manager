# MystMC v2.0.6 — Direct Functional Page Wiring

This release corrects the v2 UI/backend integration architecture.

## Fixed
- The v1.8.8 polished shell remains the visible application window.
- The old MainWindow is no longer mounted as a TabControl inside the polished shell.
- MainWindow now remains hidden only as the proven v1.5.9 controller/service host.
- Each real v1.5.9 functional page is detached individually and mounted directly into the v1.8.8 PageContent region.
- Existing event handlers and backend service references stay attached to those real page controls.
- Players now opens the actual v1.5.9 Online Players panel, including Refresh, Make Admin, Kick, and Ban.
- Start, Stop, Restart, Kill, Backup, Console, File Manager, Settings, and server selection remain wired to the proven backend.
- Prototype-only v1.8.8 navigation entries no longer silently open unrelated backend modules. They clearly state when v1.5.9 has no matching functional module.

## Directly wired functional pages
Dashboard, Create Server, Console, Worlds, Players, Plugins, Migration/Management, Performance, Health, Log Analyzer, Startup Analyzer, Optimization, Scheduler, Auto Backups, Settings/Configuration, Help/First Run Wizard, and About.

## Prototype-only UI entries without a v1.5.9 backend module
Datapacks, Resource Packs, and Templates.

## Validation
- App.xaml, MainWindow.xaml, and PrototypeWindow.xaml parse as XML/XAML.
- Modified source brace counts are balanced.
- Archive integrity checked after packaging.
- Native WPF compilation cannot be performed in this environment because the .NET Windows Desktop SDK is not installed.
