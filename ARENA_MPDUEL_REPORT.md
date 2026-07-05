# Arena MP Duel Report

## Wiring Map

- Lobby Star Gladiator launch now builds an Arena `SessionStartMessage` instead of the 4X start payload.
- `SessionLobbyMessage.Fleet` carries each peer's encoded fleet design names during Host/Join/Ready.
- Host launch flow:
  - `StartHost()` opens `TcpLockstepTransport.HostMulti(port)`.
  - joiner connects and sends lobby state with race, readiness, build fingerprint, and fleet manifest.
  - `LaunchAsHost()` requires exactly one connected ready joiner in Star Gladiator mode.
  - `BuildArenaStartMessage()` packs host fleet, join fleet, seed, RNG seed, input delay, max turns, speed, pause, SettingsHash, and session fingerprint.
  - host sends start, waits for `SessionStartAck`, then `LaunchVisibleArena(Host, start)` arms `ArenaFightScreen`.
- Join flow:
  - joiner connects by host/IP port and sends lobby state.
  - on start, `ValidateArenaStart()` reconstructs settings and checks protocol, SettingsHash, legal fleets, and session fingerprint.
  - mismatch sends negative `SessionStartAck` with a clear error and does not launch.
  - accepted start launches `ArenaFightScreen` with `ArenaMultiplayerLiveSession(Join, transport, settings)`.
- Fight live path:
  - existing lockstep turn exchange remains the simulation authority path.
  - winner, turn limit, desync, and disconnect all halt through `CompleteMultiplayerLive()`.
  - end panel shows winner/draw/void, host/join losses, turn count, final hash, and desync/disconnect flag.
  - Rematch reuses the TCP transport with same settings and deterministic next seed; Lobby disposes transport and returns to Star Gladiator MP lobby.

## Fleet Selection

- Local lobby fleet defaults to the active Arena career fleet via `ArenaCareer.FieldedFleetVessels()`.
- If no career fleet exists, it falls back to deterministic `CareerManager.StartingRosterDesigns()`.
- Only design names travel over the wire; each sim resolves designs locally before spawning.

## Self-Test Proof

- Star Gladiator lobby self-test now runs `ArenaMultiplayerSession.RunLoopbackTcpSelfTest()`.
- The proof uses real loopback TCP host/join, session-start validation, lockstep commands, remote checksums, and final hash comparison.
- Added mismatch coverage: corrupting `SettingsHash` is rejected with `Arena multiplayer settings mismatch`.

## Verification Run

- `dotnet restore UnitTests\SDUnitTests.csproj`
- `dotnet build UnitTests\SDUnitTests.csproj --no-restore`
- `dotnet test UnitTests\SDUnitTests.csproj --no-build --filter "FullyQualifiedName~LockstepNetworkTransportTests"`
- `dotnet test UnitTests\SDUnitTests.csproj --no-build --filter "FullyQualifiedName~ArenaMultiplayerLobbyEntryAndSelfTest_Headless"`
- `dotnet test UnitTests\SDUnitTests.csproj --no-build --filter "FullyQualifiedName~ArenaMultiplayerLockstepTests"`
- `dotnet test UnitTests\SDUnitTests.csproj --no-build --filter "FullyQualifiedName~Authoritative4XLobbySelfTest_RunsRealLoopbackUiCommandProof_Headless"`
- `dotnet test UnitTests\SDUnitTests.csproj --no-build --filter "FullyQualifiedName~Authoritative4XLobbyNetworkFlow"`

All listed tests passed. Full `dotnet build StarDrive.sln --no-restore` is blocked in this shell by missing Visual Studio VC++ targets for `SDNative` and by absent restore assets before restore.

## Live Two-Machine QA Checklist

- Host: Star Gladiator -> Multiplayer -> Host, confirm lobby shows career/default fleet in host slot.
- Join: enter host IP/port -> Join, confirm host sees joiner ready state and fleet summary.
- Host and join: press Ready, then host presses Launch.
- Confirm joiner rejects a deliberately mismatched build/settings payload with a readable lobby error.
- Confirm both machines enter ArenaFightScreen, show multiplayer HUD, and advance turns without local input mutating world state.
- Let one fleet die or hit max turns; both machines should show the same winner/draw, losses, turn count, final hash, and no desync flag.
- Pull network mid-match; remaining peer should halt with disconnect panel and telemetry, not hang.
- Trigger Rematch on both peers; confirm deterministic next-seed match starts and hashes stay synchronized.
- Back to Lobby should close the live session and return to Star Gladiator MP lobby cleanly.
