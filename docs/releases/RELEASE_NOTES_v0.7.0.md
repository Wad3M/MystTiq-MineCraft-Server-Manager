# MystTiq Minecraft Server Manager v0.7.0

## Play with friends without port forwarding
Router port forwarding is the hardest part of hosting from home. The **How to Connect** card on the Dashboard now covers the two common ways around it.

- **Private networks (Tailscale, ZeroTier, Hamachi, Radmin VPN).** If your PC is on one of these, the card shows its address on that network, for example `100.101.102.103:25565` for Tailscale, with a Copy button. Friends who join the same network connect with that address; no router changes needed. These addresses are found on your PC; nothing is looked up online.
- **playit.gg.** A new "No port forwarding?" row explains this free tunnel service and opens its download page. In the playit app, add a "Minecraft Java" tunnel to the port shown and share the address it gives you. MystTiq only opens the page in your browser; it doesn't install or run anything for playit.

## Changed
- Overlay-network adapters are no longer mixed into the "Same network (LAN)" row.
