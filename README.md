<p align="center">
  <img src="docs/banner.png" alt="Rappelz Ping Tracker" width="100%">
</p>

<p align="center">
  <a href="https://github.com/TheBatur/rappelz-ping-tracker/releases/latest"><img alt="Latest release" src="https://img.shields.io/github/v/release/TheBatur/rappelz-ping-tracker?label=release&color=2ea043"></a>
  <a href="https://github.com/TheBatur/rappelz-ping-tracker/releases"><img alt="Downloads" src="https://img.shields.io/github/downloads/TheBatur/rappelz-ping-tracker/total?color=2ea043"></a>
  <img alt="Windows 10/11" src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4">
  <img alt="Size" src="https://img.shields.io/badge/size-36%20KB-555">
  <a href="LICENSE"><img alt="MIT License" src="https://img.shields.io/github/license/TheBatur/rappelz-ping-tracker?color=555"></a>
</p>

<h3 align="center">
  <a href="https://github.com/TheBatur/rappelz-ping-tracker/releases/latest/download/RappelzPingTracker.exe">⬇ Download RappelzPingTracker.exe</a>
</h3>

<p align="center">
  A tiny always-on-top box that shows your <b>real ping to the Rappelz server</b> while you play,
  just like an FPS counter.<br>One small <code>.exe</code>, nothing to install, free and open source.
  <br><br>
  <a href="README.tr.md">🇹🇷 Türkçe</a>
</p>

<p align="center">
  <img src="docs/demo.gif" alt="Dragging the ping box over the game" width="800">
  <br><sub>Stays on top of the game, drag it anywhere.</sub>
</p>

![In game](docs/in-game.png)

## Features

- Shows the live round-trip time of the connection your game client is using right now
- Green under 100 ms, yellow under 200 ms, red above
- Drag it anywhere; the position is remembered
- Never steals focus from the game, doesn't show up in Alt+Tab
- Several clients open? It follows the one in front
- English, or Turkish when Windows is set to Turkish
- ~36 KB, no installer, no runtime to download (uses the .NET Framework built into Windows 10/11)

## How to use

1. Download `RappelzPingTracker.exe` from [Releases](../../releases/latest) and put it anywhere.
2. Double-click it. Windows asks for administrator permission; click **Yes**
   (the game itself asks for the same).
3. Windows SmartScreen may say *"Windows protected your PC"* because the exe isn't
   code-signed. Click **More info → Run anyway**. You can also build it yourself (see below).
4. Start Rappelz. The box shows your ping once you're connected.

Right-click the box for: **Reset position**, **Send anonymous usage stats** (on/off),
**About / source code** and **Close**. Hover over it to see which server it is measuring.

## How it works

Rappelz servers don't answer normal (ICMP) pings, and opening extra connections to the game
server just to time them is a bad idea. Instead, Rappelz Ping Tracker reads what Windows already knows:

1. It finds `SFrame.exe` and its open TCP connections (`GetExtendedTcpTable`).
2. It turns on Windows' built-in per-connection statistics for those connections
   (`SetPerTcpConnectionEStats`, this is the part that needs administrator rights).
3. Every second it reads the round-trip time samples of the busiest connection
   (`GetPerTcpConnectionEStats`) and shows the average of the last 3 seconds.

It **does not send anything to the game server**, and it doesn't touch the game process,
its memory or its traffic. If you play through a VPN, the value includes the VPN, because
that's the latency your game actually gets.

The number can be a few ms higher than a plain ping or TCP handshake to the same server,
because it is measured on the game's own data packets, so it also includes the time the
server takes to acknowledge them. That's the delay you feel in game.

## FAQ

**Why does it need administrator rights?**
Windows only lets administrators switch on the per-connection statistics. Without them the
box shows *no admin*. The game client itself also runs as administrator.

**Which servers does it work with?**
Any Rappelz client that runs as `SFrame.exe`. It was tested on Rappelz US, with and without
a VPN. It measures whatever server your client is connected to at the moment, including the
login server before you pick a character.

**It says "no game" while the game is open.**
The game process must be called `SFrame.exe`. If your client is named differently, start
Rappelz Ping Tracker with `--process=<name without .exe>`.

**Fullscreen?**
The box is a normal always-on-top window, so it shows over the game in windowed or
borderless mode. Exclusive fullscreen can hide it.

## Privacy: anonymous usage stats

On first launch Rappelz Ping Tracker asks whether it may send anonymous usage counts, so we can see
how many people use it. You can turn it off at any time from the right-click menu.

| Sent | Never sent or stored |
| --- | --- |
| A random install ID (a new GUID, not derived from your hardware) | Your IP address (only used in memory to look up the country, then dropped) |
| A random ID per session, the app version | Your Windows user name or computer name |
| How long the app has been open | Hardware IDs (MAC, disk serial, …) |
| Lowest / average / highest ping and the game server address | Your game account, characters, chat, or anything from the game process |
| Country (looked up on the server from the connection, the IP is not kept) | |

It sends one small message at start, one every 10 minutes and one when you close it.
If there's no internet or the server is down, nothing happens. The server code is in
[`server/`](server/) (no access logs, see [`server/Caddyfile.snippet`](server/Caddyfile.snippet)).
Country lookup uses [IP Geolocation by DB-IP](https://db-ip.com) (CC BY 4.0).

## Building from source

Windows 10/11 already includes the C# compiler that's needed:

```bat
build.bat
```

The output is `dist\RappelzPingTracker.exe`. Test switches:
`RappelzPingTracker.exe --process=chrome` (measure another program) and `--lang=en` / `--lang=tr`.

Settings (window position, install ID, usage stats on/off) live in
`HKEY_CURRENT_USER\Software\RappelzPing`. Delete that key to reset everything.

## Disclaimer

Rappelz Ping Tracker is a fan-made tool. It is not affiliated with or endorsed by the publishers of Rappelz.

## License

[MIT](LICENSE)
