# MystMC v2.0.5 — Startup Hotfix

- Makes the polished v1.8.8 shell the explicit WPF Application.MainWindow.
- Uses ShutdownMode.OnMainWindowClose so the hidden functional host cannot control application lifetime.
- Guards functional backend-host creation so startup exceptions no longer make MystMC silently disappear.
- Adds MystMC_startup.log beside the executable with full startup exception details.
- Adds global UI exception logging and a visible error dialog.
- Guards asynchronous MainWindow Loaded initialization against non-fatal startup failures.
