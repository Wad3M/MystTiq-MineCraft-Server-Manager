# A Simple Minecraft Server v1.2.5

## Scheduled Backups

- Automatic backup schedules are now evaluated for every saved server profile, not only the currently selected profile.
- Stopped server profiles can be backed up automatically.
- Running servers are flushed with `save-all flush` before backup when the manager owns the console session.
- Backup schedules remain active after a server stops or crashes.
- Added last-backup and next-backup status information to the Backups page.
- Automatic retention now removes only old automatic backups and never prunes manual or safety backups.
- Added structured success, skip, and failure logging for scheduled backup activity.
- Schedule and retention settings remain stored in each server profile.
