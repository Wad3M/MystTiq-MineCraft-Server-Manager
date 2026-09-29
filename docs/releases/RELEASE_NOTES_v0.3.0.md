# MystTiq Minecraft Server Manager v0.3.0

## Add-ons: plugins and mods in one place
The **Plugins** page is now **Add-ons**, and it adapts to the selected server:
- **Paper, Purpur, Folia:** manages plugins in the `plugins` folder.
- **Fabric:** manages mods in the `mods` folder.
- **Vanilla and custom JARs:** explains that these can't load add-ons and which server types can.

On the page you can:
- **See what's installed:** name, version, enabled or disabled, health, and what each add-on depends on.
- **Search Modrinth** from inside the app. Results only include server-side add-ons built for your server's loader and Minecraft version, so client-only mods and plugins for other platforms don't appear.
- **Install with one click.** The newest compatible build is downloaded and checked against Modrinth's SHA-512 hash before it's installed. After installing, the app lists anything else the add-on needs.
- **Install Fabric API with one click** on Fabric servers that don't have it yet, since most Fabric mods need it.
- **Install from a file, enable or disable, delete, and open the folder.**

Safety checks when installing, whether from Modrinth or from a file:
- A plugin can't go into a Fabric server, and a Fabric mod can't go into a Paper server.
- Client-only mods (such as shaders or minimaps) are refused with an explanation.

## Settings
- **Server type and Minecraft version are now editable.** Servers added from an existing folder only get a guessed type and no version. Setting them lets Add-ons and the Java check work properly.

## Other
- Plugin Packs use the same install path as Add-ons, so they get the same checks.
