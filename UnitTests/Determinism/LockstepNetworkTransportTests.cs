using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDLockstep;

namespace UnitTests.Determinism;

[TestClass]
public class LockstepNetworkTransportTests
{
    [TestMethod]
    public void LockstepMessageCodec_RoundTripsSessionAndFrameMessages()
    {
        var frame = new CommandFrame(42);
        frame.Add(new SimCommand(42, 2, 7, SimCommandKind.AttackTarget, 123, 456));
        frame.Sort();

        RoundTrip(2, new CommandFrameMessage { FromPeer = 0, Frame = frame }, decoded =>
        {
            var msg = (CommandFrameMessage)decoded.Message;
            Assert.AreEqual(2, decoded.ToPeer);
            Assert.AreEqual(0, msg.FromPeer);
            Assert.AreEqual(42u, msg.Frame.Tick);
            Assert.AreEqual(1, msg.Frame.Commands.Count);
            Assert.AreEqual(SimCommandKind.AttackTarget, msg.Frame.Commands[0].Kind);
        });

        RoundTrip(0, new SessionStartMessage
        {
            FromPeer = 0,
            ProtocolVersion = 1,
            MatchSeed = 0x5EED,
            RngSeed = 0xA12EA000u,
            InputDelay = 3,
            MaxTurns = 400,
            CommandEveryTurns = 2,
            GameSpeed = 2f,
            StartPaused = true,
            SettingsHash = "arena-phase1",
            BuildHash = "env-plus-settings",
            BuildSummary = "Jupiter 045; CA; settings arena-phase1",
            HostRacePreference = "United",
            JoinRacePreference = "Kulrathi",
            HostLoadoutTrait = "Ace",
            JoinLoadoutTrait = "Swarm",
            HostFleet = "Fang Strafer\u001fFang Strafer",
            JoinFleet = "Vulcan Scout",
            ExtraRemnant = 4,
            CustomMineralDecay = 1.5f,
            VolcanicActivity = 0.5f,
            ShipMaintenanceMultiplier = 1.2f,
            FTLModifier = 0.75f,
            EnemyFTLModifier = 0.25f,
            GravityWellRange = 12000f,
            AIUsesPlayerDesigns = false,
            UseUpkeepByHullSize = true,
            DisableRemnantStory = true,
            EnableRandomizedAIFleetSizes = true,
            DisableAlternateAITraits = true,
            DisablePirates = true,
            DisableResearchStations = true,
            DisableMiningOps = true,
            AuthoritativePlayerRoster = "2,1,SG9zdA==,VW5pdGVk,;3,1,Sm9pbg==,S3VscmF0aGk=,QXF1YXRpYw==",
        }, decoded =>
        {
            var msg = (SessionStartMessage)decoded.Message;
            Assert.AreEqual(0, decoded.ToPeer);
            Assert.AreEqual(0x5EED, msg.MatchSeed);
            Assert.AreEqual(0xA12EA000u, msg.RngSeed);
            Assert.AreEqual(3, msg.InputDelay);
            Assert.AreEqual(2, msg.CommandEveryTurns);
            Assert.AreEqual(2f, msg.GameSpeed);
            Assert.IsTrue(msg.StartPaused);
            Assert.AreEqual("arena-phase1", msg.SettingsHash);
            Assert.AreEqual("env-plus-settings", msg.BuildHash);
            Assert.AreEqual("Jupiter 045; CA; settings arena-phase1", msg.BuildSummary);
            Assert.AreEqual("United", msg.HostRacePreference);
            Assert.AreEqual("Kulrathi", msg.JoinRacePreference);
            Assert.AreEqual("Ace", msg.HostLoadoutTrait);
            Assert.AreEqual("Swarm", msg.JoinLoadoutTrait);
            Assert.AreEqual("Fang Strafer\u001fFang Strafer", msg.HostFleet);
            Assert.AreEqual("Vulcan Scout", msg.JoinFleet);
            Assert.AreEqual(4, msg.ExtraRemnant);
            Assert.AreEqual(1.5f, msg.CustomMineralDecay);
            Assert.AreEqual(0.5f, msg.VolcanicActivity);
            Assert.AreEqual(1.2f, msg.ShipMaintenanceMultiplier);
            Assert.AreEqual(0.75f, msg.FTLModifier);
            Assert.AreEqual(0.25f, msg.EnemyFTLModifier);
            Assert.AreEqual(12000f, msg.GravityWellRange);
            Assert.IsFalse(msg.AIUsesPlayerDesigns);
            Assert.IsTrue(msg.UseUpkeepByHullSize);
            Assert.IsTrue(msg.DisableRemnantStory);
            Assert.IsTrue(msg.EnableRandomizedAIFleetSizes);
            Assert.IsTrue(msg.DisableAlternateAITraits);
            Assert.IsTrue(msg.DisablePirates);
            Assert.IsTrue(msg.DisableResearchStations);
            Assert.IsTrue(msg.DisableMiningOps);
            Assert.AreEqual("2,1,SG9zdA==,VW5pdGVk,;3,1,Sm9pbg==,S3VscmF0aGk=,QXF1YXRpYw==",
                msg.AuthoritativePlayerRoster);
        });

        RoundTrip(0, new SessionHelloMessage
        {
            FromPeer = 2,
            ProtocolVersion = 1,
            PeerId = 2,
            PlayerName = "Arena Join",
            BuildHash = "env",
            BuildSummary = "Jupiter 045; CA",
        }, decoded =>
        {
            var msg = (SessionHelloMessage)decoded.Message;
            Assert.AreEqual(2, msg.FromPeer);
            Assert.AreEqual(2, msg.PeerId);
            Assert.AreEqual("env", msg.BuildHash);
            Assert.AreEqual("Jupiter 045; CA", msg.BuildSummary);
        });

        RoundTrip(0, new SessionLobbyMessage
        {
            FromPeer = 2,
            PeerId = 2,
            Ready = true,
            PlayerName = "Join",
            RacePreference = "Kulrathi",
            LoadoutTrait = "Swarm",
            TraitOptions = "Aquatic|Cybernetic",
            Fleet = "Fang\u001fClaw",
            BuildHash = "env",
            BuildSummary = "Jupiter 045; CA",
        }, decoded =>
        {
            var msg = (SessionLobbyMessage)decoded.Message;
            Assert.AreEqual(2, msg.FromPeer);
            Assert.AreEqual(2, msg.PeerId);
            Assert.IsTrue(msg.Ready);
            Assert.AreEqual("Join", msg.PlayerName);
            Assert.AreEqual("Kulrathi", msg.RacePreference);
            Assert.AreEqual("Swarm", msg.LoadoutTrait);
            Assert.AreEqual("Aquatic|Cybernetic", msg.TraitOptions);
            Assert.AreEqual("Fang\u001fClaw", msg.Fleet);
            Assert.AreEqual("env", msg.BuildHash);
        });

        RoundTrip(1, new SessionStartAckMessage
        {
            FromPeer = 3,
            PeerId = 3,
            Accepted = true,
            StartFingerprint = "0x1234",
            Error = "",
        }, decoded =>
        {
            var msg = (SessionStartAckMessage)decoded.Message;
            Assert.AreEqual(1, decoded.ToPeer);
            Assert.AreEqual(3, msg.FromPeer);
            Assert.AreEqual(3, msg.PeerId);
            Assert.IsTrue(msg.Accepted);
            Assert.AreEqual("0x1234", msg.StartFingerprint);
            Assert.AreEqual("", msg.Error);
        });

        RoundTrip(2, new SessionControlMessage { FromPeer = 0, Paused = true, GameSpeed = 0.5f }, decoded =>
        {
            var msg = (SessionControlMessage)decoded.Message;
            Assert.AreEqual(0, msg.FromPeer);
            Assert.IsTrue(msg.Paused);
            Assert.AreEqual(0.5f, msg.GameSpeed);
        });
    }

    [TestMethod]
    public void TcpLockstepTransport_LocalhostRoundTrip()
    {
        int port = FreeTcpPort();
        using TcpLockstepTransport host = TcpLockstepTransport.Host(port);
        using TcpLockstepTransport client = TcpLockstepTransport.Join("127.0.0.1", port);
        Assert.IsTrue(host.WaitForConnection(TimeSpan.FromSeconds(3)), "Host did not accept localhost client.");
        Assert.IsTrue(client.IsConnected, "Client did not connect.");

        var hostMessages = new List<LockstepMessage>();
        var clientMessages = new List<LockstepMessage>();
        host.Register(0, hostMessages.Add);
        client.Register(2, clientMessages.Add);

        client.Send(0, new SessionReadyMessage { FromPeer = 2, PeerId = 2, Ready = true });
        PumpUntil(() => hostMessages.Count == 1, host, client);
        Assert.IsInstanceOfType(hostMessages[0], typeof(SessionReadyMessage));
        Assert.AreEqual(2, hostMessages[0].FromPeer);

        var frame = new CommandFrame(3);
        frame.Add(new SimCommand(3, 1, 0, SimCommandKind.NoOp));
        host.Send(2, new CommandFrameMessage { FromPeer = 0, Frame = frame });
        PumpUntil(() => clientMessages.Count == 1, host, client);
        var frameMsg = (CommandFrameMessage)clientMessages[0];
        Assert.AreEqual(3u, frameMsg.Frame.Tick);
        Assert.AreEqual(SimCommandKind.NoOp, frameMsg.Frame.Commands[0].Kind);
    }

    // Regression for the 2026-07-05 live arm-handshake deadlock: the shared lobby transport
    // addressed peers in a different id space than the fight driver, so a send to an unrouted peer
    // was SILENTLY parked in PendingRemote forever. The transport must now (a) fire RoutingAlarm on
    // such a send-to-nowhere, and (b) deliver the parked message once MapPeerRoute supplies a route.
    [TestMethod]
    public void TcpLockstepTransport_UnroutableSendFiresAlarm_ThenMapPeerRouteDelivers()
    {
        int port = FreeTcpPort();
        using TcpLockstepTransport host = TcpLockstepTransport.Host(port);   // remote peer id 2
        using TcpLockstepTransport client = TcpLockstepTransport.Join("127.0.0.1", port);
        Assert.IsTrue(host.WaitForConnection(TimeSpan.FromSeconds(3)), "Host did not accept client.");

        var alarms = new List<string>();
        host.RoutingAlarm = alarms.Add;

        // Peer 7 has no connection, no route, and host.RemotePeerId(2) != 7 — but the transport IS
        // connected, so this is exactly the silent send-to-nowhere. Alarm must fire once.
        host.Send(7, new SessionControlMessage { FromPeer = 0, Paused = false, GameSpeed = 1f });
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (alarms.Count == 0 && DateTime.UtcNow < deadline) { host.Poll(); Thread.Sleep(5); }
        Assert.AreEqual(1, alarms.Count, "RoutingAlarm should fire once for an unroutable connected send.");
        Assert.IsTrue(host.PendingRemoteCount >= 1, "The unroutable message should be parked, not lost.");

        // Supplying a route (peer 7 travels over the peer-2 connection) must flush the parked
        // message onto the wire, where the client receives it addressed to peer 7.
        var clientMsgs = new List<LockstepMessage>();
        client.Register(7, clientMsgs.Add);
        host.MapPeerRoute(7, 2);
        PumpUntil(() => clientMsgs.Count == 1, host, client);
        Assert.IsInstanceOfType(clientMsgs[0], typeof(SessionControlMessage));

        // A second send now has a route, so it must NOT re-alarm.
        host.Send(7, new SessionControlMessage { FromPeer = 0, Paused = true, GameSpeed = 1f });
        PumpUntil(() => clientMsgs.Count == 2, host, client);
        Assert.AreEqual(1, alarms.Count, "A routed send must not fire the routing alarm.");
    }

    // Regression for the transport thread-leak hardening (2026-07-05): Dispose must JOIN the
    // background accept/read/write loops, not just close the sockets. A leaked live socket thread
    // per disposed transport accumulates across a long test run and races the shared process
    // teardown (a suspect in the intermittent native test-host fault).
    [TestMethod]
    public void TcpLockstepTransport_DisposeJoinsBackgroundLoops()
    {
        int port = FreeTcpPort();
        var host = TcpLockstepTransport.Host(port);
        var client = TcpLockstepTransport.Join("127.0.0.1", port);
        Assert.IsTrue(host.WaitForConnection(TimeSpan.FromSeconds(3)), "Host did not accept client.");

        Task[] BackgroundTasks(TcpLockstepTransport t)
        {
            var tasks = new List<Task>();
            var acceptField = typeof(TcpLockstepTransport).GetField("AcceptTask",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (acceptField?.GetValue(t) is Task accept) tasks.Add(accept);
            var connField = typeof(TcpLockstepTransport).GetField("Connections",
                BindingFlags.NonPublic | BindingFlags.Instance);
            if (connField?.GetValue(t) is System.Collections.IEnumerable conns)
                foreach (object c in conns)
                {
                    if (c.GetType().GetField("ReadTask")?.GetValue(c) is Task rt) tasks.Add(rt);
                    if (c.GetType().GetField("WriteTask")?.GetValue(c) is Task wt) tasks.Add(wt);
                }
            return tasks.ToArray();
        }

        Task[] hostTasks = BackgroundTasks(host);
        Task[] clientTasks = BackgroundTasks(client);
        Assert.IsTrue(hostTasks.Length >= 1 && clientTasks.Length >= 1,
            "Expected live background loops before Dispose.");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        host.Dispose();
        client.Dispose();
        sw.Stop();

        Assert.IsTrue(sw.Elapsed < TimeSpan.FromSeconds(5), $"Dispose should join promptly, took {sw.Elapsed}.");
        foreach (Task t in hostTasks)
            Assert.IsTrue(t.IsCompleted, "Host background loop should be joined (completed) after Dispose.");
        foreach (Task t in clientTasks)
            Assert.IsTrue(t.IsCompleted, "Client background loop should be joined (completed) after Dispose.");
    }

    static void RoundTrip(int toPeer, LockstepMessage message, Action<DecodedLockstepMessage> assert)
        => assert(LockstepMessageCodec.Decode(LockstepMessageCodec.Encode(message, toPeer)));

    static void PumpUntil(Func<bool> done, params ILockstepTransport[] transports)
    {
        DateTime deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        while (!done() && DateTime.UtcNow < deadline)
        {
            foreach (ILockstepTransport transport in transports)
                transport.Poll();
            Thread.Sleep(5);
        }
        Assert.IsTrue(done(), "Timed out waiting for TCP lockstep transport message.");
    }

    static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
