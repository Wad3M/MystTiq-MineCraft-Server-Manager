# Releasing

Releases are built and published by GitHub Actions (`.github/workflows/release.yml`) when a version tag is pushed.

1. Set the new version in `src/ASimpleMinecraftServer/ASimpleMinecraftServer.csproj` (`Version`, `FileVersion`, `AssemblyVersion`).
2. Write `docs/releases/RELEASE_NOTES_vX.Y.Z.md` and add it to `docs/releases/README.md`.
3. Merge to `main` and wait for the **Build** workflow to pass.
4. Work through [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).
5. Tag the commit on `main` and push the tag:
   ```bat
   git tag v0.2.0
   git push origin v0.2.0
   ```

The **Release** workflow then:
- checks the tag matches the version in the `.csproj` and that the release notes file exists,
- builds a self-contained single-file Windows x64 `.exe`,
- creates a GitHub Release named after the tag, using the release notes as its description, with the `.exe` and a `SHA256SUMS.txt` attached.

To verify a download, run this in PowerShell and compare the result with `SHA256SUMS.txt`:
```powershell
Get-FileHash .\MystTiq-MineCraft-Server-Manager-v0.2.0-win-x64.exe -Algorithm SHA256
```
