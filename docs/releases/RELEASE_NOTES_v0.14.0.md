# MystTiq Minecraft Server Manager v0.14.0

## Pre-generate the world
**Worlds** has a new **Pre-generate** tab. Generating the map before players explore it removes most of the lag that new terrain causes.

- **Install Chunky with one click:** Chunky is a free plugin/mod from Modrinth, and the download is hash checked. It works on Paper, Purpur, Folia, Fabric, Forge and NeoForge.
- **Pick a radius:** choose 1,000 to 10,000 blocks, or type your own. MystTiq shows roughly how many chunks that is and how much disk space it will take before you start.
- **World border (optional):** a matching world border keeps players inside the generated area.
- **Live progress:** the tab shows the percentage, chunks done and time left, read from Chunky's console output.
- **Pause, Continue and Cancel** are always available. Chunky remembers a paused run across restarts, so press **Continue** after starting the server again. Chunks already generated are kept if you cancel.
- Generation runs on the server while it's online, so players can keep playing. It covers the overworld, centred on 0,0.
