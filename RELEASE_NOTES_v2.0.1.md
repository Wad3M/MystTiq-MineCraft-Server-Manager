# MystMC v2.0.1 — Action Binding Pass

- Connected the v1.8.8 Kill toolbar object to a real force-terminate backend path instead of aliasing normal Stop.
- Preserved graceful Stop separately so Start/Stop/Restart/Kill now have distinct functional behavior.
- Kept the v1.8.8 shell and all v1.5.9 server-management services intact.

## Validation
- XAML parsed successfully in the build environment.
- Required backend/source files were structurally validated.
- Native WPF compilation still requires the .NET 10 Windows Desktop SDK on Windows.
