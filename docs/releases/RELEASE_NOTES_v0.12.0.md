# MystTiq Minecraft Server Manager v0.12.0

## Automatic Java download
A missing or outdated Java is the most common reason a new server won't start. MystTiq now fixes that for you.

- **When you start a server or create one,** MystTiq checks that its Java is new enough for that Minecraft version. Minecraft 26.1 and later needs Java 25, 1.20.5–1.21.x needs Java 21, 1.18–1.20.4 needs Java 17, and older versions need Java 8.
- **If the Java is missing or too old,** MystTiq offers to download the right one. It's **Eclipse Temurin** from adoptium.net (about 50 MB), checked against Adoptium's SHA-256 checksum, and the server is set to use it.
- **Shared and self-contained:**
  - Downloaded runtimes are kept in `%LOCALAPPDATA%\ASimpleMinecraftServer\Java`, one per Java version, and every server that needs that version shares it.
  - Nothing is installed system-wide, and your PATH isn't touched.
- **Already downloaded?** It's used straight away, with no question asked.
- **Settings** has a **Download Java N** button to switch a server to the right Java at any time.
- If you'd rather keep your own Java, choose **No**. You can still start with it, as before.
