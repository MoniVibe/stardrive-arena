using System;
using System.Collections.Generic;

namespace SDLockstep;

/// <summary>
/// In-memory reliable, in-order transport for headless lockstep tests (advisor plan VS8). The first
/// proof uses perfectly reliable ordered delivery; jitter / reorder / duplication / loss can be layered
/// on later as a stress variant. Messages enqueued during a Poll are delivered on the NEXT Poll, so the
/// driver controls the lockstep cadence deterministically.
/// </summary>
public sealed class FakeTransport : ILockstepTransport
{
    readonly Dictionary<int, Action<LockstepMessage>> Receivers = new();
    readonly Dictionary<int, Queue<LockstepMessage>> Inbox = new();

    public void Register(int peerId, Action<LockstepMessage> onReceive)
    {
        Receivers[peerId] = onReceive;
        if (!Inbox.ContainsKey(peerId))
            Inbox[peerId] = new Queue<LockstepMessage>();
    }

    public void Send(int toPeer, LockstepMessage message)
    {
        if (!Inbox.TryGetValue(toPeer, out Queue<LockstepMessage> q))
        {
            q = new Queue<LockstepMessage>();
            Inbox[toPeer] = q;
        }
        q.Enqueue(message);
    }

    public void Poll()
    {
        // Snapshot peer ids so receivers may Send (enqueue) during delivery without mutating iteration.
        var peers = new List<int>(Inbox.Keys);
        foreach (int peer in peers)
        {
            if (!Receivers.TryGetValue(peer, out Action<LockstepMessage> recv)) continue;
            Queue<LockstepMessage> q = Inbox[peer];
            int n = q.Count; // messages enqueued during this drain wait for the next Poll
            for (int i = 0; i < n; ++i)
                recv(q.Dequeue());
        }
    }
}
