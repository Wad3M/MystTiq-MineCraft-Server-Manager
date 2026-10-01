# Code signing policy

Windows releases of MystTiq Minecraft Server Manager are signed so Windows can confirm who published them and that they weren't changed after the build.

Free code signing provided by [SignPath.io](https://about.signpath.io/), certificate by [SignPath Foundation](https://signpath.org/).

> **Status:** application to SignPath Foundation in progress. Until it's approved, releases are unsigned. Each release still includes `SHA256SUMS.txt` so you can check the download (see [RELEASING.md](RELEASING.md)).

## What gets signed

Only `MystTiq-MineCraft-Server-Manager-vX.Y.Z-win-x64.exe`, built by the [Release workflow](../.github/workflows/release.yml) on GitHub-hosted runners from this repository's source. Nothing built on a personal computer is ever signed.

## Team roles

| Role | Members |
|---|---|
| Committers and reviewers | [Wad3M](https://github.com/Wad3M) |
| Approvers | [Wad3M](https://github.com/Wad3M) |

Every signing request is approved by hand in SignPath before the certificate is used.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it.

It only connects to the internet when you use a feature that needs it:

| When you… | It contacts | To |
|---|---|---|
| Open Create Server or install a server | Mojang (`piston-meta.mojang.com`), PaperMC (`fill.papermc.io`), PurpurMC (`api.purpurmc.org`), FabricMC (`meta.fabricmc.net`) | List Minecraft versions and download the server JAR you picked |
| Search or install add-ons, or install a plugin pack | Modrinth (`api.modrinth.com`, `cdn.modrinth.com`) | Search for and download plugins or mods |
| Open the EULA link | `aka.ms` in your browser | Show the Minecraft EULA |
| Click **Show public address** on the Dashboard | ipify (`api.ipify.org`) | Find your network's public IP so you can share it with friends |

These requests carry the app name and version in the User-Agent header and nothing else about you. The app has no telemetry, analytics or crash reporting. The Minecraft servers you run are separate programs, and their own network use is up to them.
