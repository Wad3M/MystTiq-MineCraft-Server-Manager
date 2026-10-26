# MystTiq Minecraft Server Manager v0.15.0

## System tray and start with Windows
MystTiq can now keep your servers running in the background, and bring them back after a reboot.

- **Close to tray:**
  - Closing the window now hides MystTiq in the system tray, and your servers keep running.
  - Double-click the tray icon to open it again, or right-click it and choose **Exit** to quit. Running servers are stopped safely first, as before.
  - You can turn this off in **Settings → App**.
- **Start with Windows:** in **Settings → App**, tick **Start MystTiq in the tray when I sign in to Windows**. This adds MystTiq to your user's start-up list, so no admin rights are needed. Untick it to remove it.
- **Start servers automatically:** each server's **Settings → Server** tab has **Start this server when MystTiq opens**. Together with sign-in start, your servers are back up after a reboot. If MystTiq already downloaded the right Java for a server, it's used automatically.
- **Crash notifications:** when a server crashes while MystTiq is in the tray or in the background, a Windows notification says so, and says whether it's being restarted.
- **One MystTiq at a time:** opening MystTiq again while it's already running (for example from the Start menu while it's in the tray) brings the existing window forward. It no longer starts a second copy that would compete for the same servers.
