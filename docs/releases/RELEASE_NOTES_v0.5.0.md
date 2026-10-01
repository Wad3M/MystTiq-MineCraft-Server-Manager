# MystTiq Minecraft Server Manager v0.5.0

## How to Connect
The Dashboard has a new **How to Connect** card for the selected server, showing the address to type into Minecraft (Multiplayer → Add Server):

| Where your friend is | Address shown |
|---|---|
| This PC | `localhost:<port>` |
| Same network (LAN) | Your PC's local IPv4 address and the port, e.g. `192.168.1.20:25566`. Worked out on your PC, with no internet lookup. |
| Internet | Your public IP and the port, after you click **Show public address**. |

- Each address has a **Copy** button.
- The Internet row reminds you which port to forward on your router and to which local address.
- The public address comes from `api.ipify.org`, and only when you click the button, in line with the privacy policy. It's remembered until you close the app.

## Fixed
- **Buttons on the Dashboard could miss clicks.** The Dashboard refreshes every second, and a click that landed during a refresh did nothing. Refreshes now wait while the mouse button is held down.
