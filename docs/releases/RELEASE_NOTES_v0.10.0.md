# MystTiq Minecraft Server Manager v0.10.0

## Whitelist and operators editor
The **Players** page now has three tabs: **Online**, **Whitelist** and **Operators**.

- **Whitelist:**
  - Tick "Only allow players on the whitelist" to make the server private. This sets both `white-list` and `enforce-whitelist`, so unlisted players already online are removed too.
  - Add players by name, or remove them.
- **Operators:** make trusted players admins (level 4) or remove them.
- **Works whether the server is running or not:**
  - **Running:** changes are sent as Minecraft's own commands (`whitelist add`, `op`, `deop`, `whitelist on`) and apply straight away.
  - **Stopped:** MystTiq edits `whitelist.json`, `ops.json` and `server.properties` directly. On online-mode servers, each name is checked with Mojang to get the player's real UUID, and an unknown name is refused. On offline-mode servers, the UUID is computed exactly as Minecraft does.
- Names are validated (3–16 letters, numbers or underscores), so a typo or a stray character can't turn into a different command.
