# MystTiq Minecraft Server Manager v0.9.0

## Bedrock crossplay
Bedrock players on phones, tablets, consoles and Windows can now join your Java server alongside Java players.

**Add-ons → Bedrock Crossplay → Install Crossplay** sets it up:
- installs **Geyser** (lets Bedrock clients connect) and **Floodgate** (lets them join without a Java account) from Modrinth, picking the right build for the server type and hash-checking each file,
- on Fabric servers, also installs **Fabric API** if it's missing,
- the first time the server starts, Geyser creates its settings. MystTiq then picks a free **Bedrock UDP port** (19132, or the next free one) and switches login to Floodgate.

Works on Paper, Purpur and Fabric servers. Geyser doesn't support Folia.

### Ports
- Each crossplay server needs its own Bedrock port. If two running servers would clash, MystTiq moves the second one's port automatically when it starts, and logs the change. You can also set the port yourself on the Bedrock Crossplay tab.
- The **How to Connect** card shows a **Bedrock players** row with the address and UDP port. To play over the internet, forward that UDP port on your router as well as the Java TCP port.
