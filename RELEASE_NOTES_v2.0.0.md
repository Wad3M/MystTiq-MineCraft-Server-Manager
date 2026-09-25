# MystMC v2.0.0 — Functional MystUI Merge

## Architecture
- v1.8.8 Polished Vector Icons is the visible application shell.
- v1.5.9 Stabilization Build2 remains the functional server-management backend.
- The original functional TabControl/page instances are detached from the legacy shell and mounted directly into the v1.8.8 PageContent surface.
- Existing page event handlers and services remain owned by MainWindow, preserving the stabilization build's behavior.

## Functional bindings
The v1.8.8 toolbar now invokes real actions for Start, Stop, Restart, Backup, Console, File Manager, and Settings. Kill currently uses the existing stop path because the stabilization backend does not expose a separate public force-kill command.

Navigation is connected to the closest existing functional backend page. UI Preview remains the 1.8.8 visual preview page.

## Validation note
This package was structurally validated in the build environment. Native WPF compilation requires the .NET 10 Windows Desktop SDK on Windows.
