using System;
using Ship_Game.Data.Serialization;
using Ship_Game.Utils;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.Ships.AI;

[StarDataType]
public readonly struct WayPoint
{
    [StarData] public readonly Vector2 Position;
    [StarData] public readonly Vector2 Direction; // direction we should be facing at the way point
    // True for pathfinder detour waypoints (gravity-well swing-bys). Fleets reform and
    // drop out of warp at user waypoints, but pass straight through detours to keep warp
    // engaged — matching single-ship behavior. Defaults false (old saves => user waypoint).
    [StarData] public readonly bool IsDetour;
    public WayPoint(Vector2 pos, Vector2 dir, bool isDetour = false)
    {
        Position = pos;
        Direction = dir;
        IsDetour = isDetour;
    }
}

public sealed class WayPoints : IDisposable
{
    readonly SafeQueue<WayPoint> ActiveWayPoints = new();
    public int Count => ActiveWayPoints.Count;

    public void Clear()
    {
        ActiveWayPoints.Clear();
    }
    public WayPoint Dequeue()
    {
        return ActiveWayPoints.Dequeue();
    }
    public void Enqueue(WayPoint point)
    {
        ActiveWayPoints.Enqueue(point);
    }
    public WayPoint[] EnqueueAndToArray(WayPoint point)
    {
        lock (ActiveWayPoints.Locker)
        {
            ActiveWayPoints.Enqueue(point);
            return ActiveWayPoints.ToArray();
        }
    }

    // Atomically enqueues detour waypoints + a final waypoint, then returns the
    // snapshot. Used by ShipAI.AddWayPoint when GravityWellRouter has produced
    // intermediate waypoints to route around enemy gravity wells.
    public WayPoint[] EnqueueRangeAndToArray(Vector2[] detourPositions, WayPoint final)
    {
        lock (ActiveWayPoints.Locker)
        {
            if (detourPositions != null)
            {
                for (int i = 0; i < detourPositions.Length; i++)
                {
                    Vector2 next = i + 1 < detourPositions.Length ? detourPositions[i + 1] : final.Position;
                    Vector2 dir = (next - detourPositions[i]).Normalized();
                    ActiveWayPoints.Enqueue(new WayPoint(detourPositions[i], dir, isDetour: true));
                }
            }
            ActiveWayPoints.Enqueue(final);
            return ActiveWayPoints.ToArray();
        }
    }
    public WayPoint ElementAt(int element)
    {
        return ActiveWayPoints.ElementAt(element);
    }
    public void Set(WayPoint[] wayPoints)
    {
        for (int i = 0; i < wayPoints.Length; i++)
            ActiveWayPoints.Enqueue(wayPoints[i]);
    }
    public WayPoint[] ToArray()
    {
        return ActiveWayPoints.ToArray();
    }

    public void Dispose()
    {
        ActiveWayPoints.Dispose();
    }
}
