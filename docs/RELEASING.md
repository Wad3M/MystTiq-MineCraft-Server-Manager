# Releasing

Releases are built and published by GitHub Actions (`.github/workflows/release.yml`) when a version tag is pushed.

1. Set the new version in `src/ASimpleMinecraftServer/ASimpleMinecraftServer.csproj` (`Version`, `FileVersion`, `AssemblyVersion`).
2. Write `docs/releases/RELEASE_NOTES_vX.Y.Z.md` and add it to `docs/releases/README.md`.
3. Merge to `main` and wait for the **Build** workflow to pass.
4. Work through [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).
5. Start the release, either way works:
   - **From GitHub:** Actions tab → **Release** → **Run workflow** on `main`. The tag is created for you from the `.csproj` version.
   - **From Git:** tag the commit on `main` and push the tag:
     ```bat
     git tag v0.2.0
     git push origin v0.2.0
     ```

The **Release** workflow then:
- reads the version from the `.csproj` (and, for a pushed tag, checks the tag matches) and checks the release notes file exists,
- builds a self-contained single-file Windows x64 `.exe`,
- creates a GitHub Release named after the tag, using the release notes as its description, with the `.exe` and a `SHA256SUMS.txt` attached.

To verify a download, run this in PowerShell and compare the result with `SHA256SUMS.txt`:
```powershell
Get-FileHash .\MystTiq-MineCraft-Server-Manager-v0.2.0-win-x64.exe -Algorithm SHA256
```

## Code signing (SignPath Foundation)

The Release workflow signs the `.exe` through SignPath, but only once these are set in the repository under **Settings → Secrets and variables → Actions**. Until then it publishes unsigned builds and says so in the run log.

| Name | Type | Where it comes from |
|---|---|---|
| `SIGNPATH_API_TOKEN` | Secret | SignPath: create a CI user with submitter permission on the project, then copy its API token |
| `SIGNPATH_ORGANIZATION_ID` | Variable | SignPath organization settings |
| `SIGNPATH_PROJECT_SLUG` | Variable | The project's slug in SignPath |
| `SIGNPATH_SIGNING_POLICY_SLUG` | Variable | Usually `release-signing` |
| `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG` | Variable | Optional; leave unset to use the project's default |

When signing is on, the workflow:
1. uploads the unsigned `.exe` as a build artifact,
2. submits it to SignPath and waits while an approver approves the request in SignPath,
3. checks the returned file's Authenticode signature is valid (the release fails if it isn't),
4. publishes the signed file and its SHA-256 checksum.

Never put the API token anywhere except the repository secret.
