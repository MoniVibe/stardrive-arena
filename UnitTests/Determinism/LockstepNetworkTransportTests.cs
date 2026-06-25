using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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
            SettingsHash = "arena-phase1",
            BuildHash = "env-plus-settings",
            BuildSummary = "Jupiter 045; CA; settings arena-phase1",
            HostFleet = "Fang Strafer\u001fFang Strafer",
            JoinFleet = "Vulcan Scout",
        }, decoded =>
        {
            var msg = (SessionStartMessage)decoded.Message;
            Assert.AreEqual(0, decoded.ToPeer);
            Assert.AreEqual(0x5EED, msg.MatchSeed);
            Assert.AreEqual(0xA12EA000u, msg.RngSeed);
            Assert.AreEqual(3, msg.InputDelay);
            Assert.AreEqual("arena-phase1", msg.SettingsHash);
            Assert.AreEqual("env-plus-settings", msg.BuildHash);
            Assert.AreEqual("Jupiter 045; CA; settings arena-phase1", msg.BuildSummary);
            Assert.AreEqual("Fang Strafer\u001fFang Strafer", msg.HostFleet);
            Assert.AreEqual("Vulcan Scout", msg.JoinFleet);
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
