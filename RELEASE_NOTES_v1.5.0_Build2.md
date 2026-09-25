# A Simple Minecraft Server v1.5.0 Build 2

## Compiler fix

- Added a project-level `global using System.IO;` declaration.
- Resolves missing `File`, `Directory`, `Path`, `FileInfo`, `SearchOption`, `IOException`, `InvalidDataException`, and `FileNotFoundException` symbols in the v1.4.2-v1.5.0 service layer.
- No feature behavior was changed.
