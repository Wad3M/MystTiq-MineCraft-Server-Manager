# v1.8.6 – Build and Run Fix

- Fixed `Build-And-Run.bat` losing its own path after changing directories.
- Build commands now target the project through an absolute path without changing the batch file working directory.
- Added explicit project-not-found and run-failure messages.
- Renamed the `Icon(...)` helper to `CreateIcon(...)` so it no longer hides `Window.Icon`.
- Expected result: clean build with no CS0108 warning, followed by application launch.
