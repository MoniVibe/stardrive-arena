# StarDrive Arena Overlay Install Guide

StarDrive Arena ships as a free overlay for StarDrive BlackBox Jupiter 1.60. The release zip contains only built DLLs and does not include commercial game content.

## Requirements

- StarDrive BlackBox Jupiter 1.60 installed from https://stardriveteam.itch.io/jupiter-160
- Windows 10 version 1803 or newer
- x64 PC

## Install

1. Close StarDrive if it is running.
2. Open your StarDrive BlackBox Jupiter 1.60 game folder.
3. Back up these files from the game folder:
   - `StarDrive.dll`
   - `SDUtils.dll`
   - `SDGraphics.dll`
   - `SDLockstep.dll`
   - `Plugins\StarDriveArena.dll`, if it already exists
4. Extract `stardrive-arena-<version>.zip` over the game folder. The zip is laid out so the four root DLLs land beside `StarDrive.exe`, and `StarDriveArena.dll` lands in `Plugins\`.
5. Start StarDrive.

To uninstall, close the game and restore the five backed-up files.

## Hosting a 4X Multiplayer Game

1. Start StarDrive and choose `4X Multiplayer` from the main menu.
2. Leave `PORT` at `47377` unless you need a different TCP port.
3. Choose the host race, trait, game settings, and slot setup. Slots can be human, AI, or closed.
4. Press `HOST`. The lobby listens on the selected port.
5. Share your IP address or VPN address and port with the other players.
6. Press `READY` for the host player.
7. After all connected human players are ready, press `LAUNCH`.

The host owns the authoritative simulation. Joiners send commands to the host and the host drives the live game.

## Joining a 4X Multiplayer Game

1. Install the same StarDrive Arena release as the host.
2. Start StarDrive and choose `4X Multiplayer` from the main menu.
3. Enter the host IP address or VPN name in `HOST`.
4. Enter the host port in `PORT`; the default is `47377`.
5. If more than one remote player is joining, choose a unique slot from `P3` through `P9`.
6. Press `JOIN`, then press `READY` after the connection succeeds.
7. Wait for the host to launch.

All players must run the same release. Multiplayer startup is protocol-version gated, and mismatched protocol/build environments are rejected before launch.

## Networking Notes

- The lobby uses TCP port `47377` by default.
- The host must allow inbound traffic on that port through Windows Firewall and any router or VPN firewall.
- LAN games can usually use the host machine's LAN IP.
- For non-LAN games, use port forwarding or a VPN such as Radmin VPN or Tailscale, then join using the host's VPN address.

## Known Limits

- Beta multiplayer path.
- 2-player live games have been tested.
- Up to 8 players have been covered by headless tests.
- Joiner movement interpolation is still cosmetically rough.
- Report issues on the GitHub repository: https://github.com/MoniVibe/stardrive-arena/issues
