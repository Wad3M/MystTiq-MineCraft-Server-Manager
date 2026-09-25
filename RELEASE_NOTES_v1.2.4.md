# A Simple Minecraft Server v1.2.4

## Plugin Update Manager

- Added installed-plugin update checks through Modrinth.
- Filters available releases for the selected Minecraft version and supported server loaders.
- Shows installed version, latest compatible version, and update status in the Plugin Manager.
- Added Check Updates, Update Selected, and Update All actions.
- Requires the selected server to be stopped before update operations.
- Preserves plugin configuration folders.
- Stores timestamped copies of replaced JAR files in `plugins\.asms-update-backups`.
- Preserves enabled or disabled plugin state after updating.
- Validates downloaded JAR archives and plugin descriptors before replacement.
- Restores the previous JAR automatically if replacement fails.
- Reports unmatched and incompatible plugins without changing them.
