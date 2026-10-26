# MystTiq Minecraft Server Manager v0.6.0

## Crash auto-restart
Servers can now restart themselves after a crash. Turn it on per server in **Settings → Server**: "Restart automatically if the server crashes".

- **What counts as a crash:** the server exits with an error code when nobody asked it to stop. These never trigger a restart:
  - stopping or killing it from MystTiq,
  - typing `stop` in the Console,
  - a clean exit (code 0), such as an operator using `/stop` in game.
- **Limits so it can't loop forever:** by default up to 3 restarts within 15 minutes, waiting 10 seconds before each one. All three numbers can be changed. When the limit is reached, the server is marked **Failed** and the console points you to Health → Logs.
- **You stay in control:** starting, stopping or killing a server yourself resets the count. While a restart is pending, the Dashboard shows **Restarting** with a **Cancel restart** button.
- Every restart is logged in the server's console.
