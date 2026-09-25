# v1.2.6 — Scheduled Tasks

- Added a dedicated Scheduled Tasks page.
- Schedule broadcasts, console commands, starts, stops, restarts, and update checks.
- Tasks are stored persistently per server profile.
- Supports 15-minute through 24-hour intervals.
- Tasks can be enabled, disabled, deleted, or run immediately.
- Records next-run time and the latest execution result.
- Scheduler safely skips actions when their required server state is unavailable.
- Corrected a duplicated local variable inherited from the v1.2.5 baseline.
