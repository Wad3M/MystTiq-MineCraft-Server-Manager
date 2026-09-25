# v1.2.7 — Crash Recovery

- Detects unexpected Java process exits.
- Automatically restarts the affected server after a configurable delay.
- Per-server enable/disable setting.
- Configurable restart limits prevent infinite crash loops.
- Restart attempts are tracked in a rolling 15-minute window.
- Recovery activity is written to the live manager log.
