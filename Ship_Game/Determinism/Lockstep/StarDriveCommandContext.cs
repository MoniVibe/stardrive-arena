using System;

namespace Ship_Game.Determinism.Lockstep;

/// <summary>
/// Guards authoritative command application (advisor plan RC3). While a <see cref="SDLockstep.CommandFrame"/>
/// is being applied to the simulation, <see cref="IsApplyingAuthoritativeCommand"/> is true. Direct
/// player/UI mutation entry points (e.g. ShipMoveCommands.OrderMoveTo, ShipAI.Goals.AddShipGoal) can
/// assert against this in the deterministic/lockstep profile to catch any path that bypasses the
/// authoritative command stream — without rewriting those god-class APIs broadly.
/// </summary>
public static class StarDriveCommandContext
{
    [ThreadStatic] public static bool IsApplyingAuthoritativeCommand;
    [ThreadStatic] public static uint CurrentTick;
    [ThreadStatic] public static int CurrentPlayerId;

    /// <summary>RAII scope: <c>using (StarDriveCommandContext.Enter(tick, playerId)) { ...apply... }</c>.</summary>
    public static Scope Enter(uint tick, int playerId) => new(tick, playerId);

    public readonly struct Scope : IDisposable
    {
        public Scope(uint tick, int playerId)
        {
            IsApplyingAuthoritativeCommand = true;
            CurrentTick = tick;
            CurrentPlayerId = playerId;
        }

        public void Dispose()
        {
            IsApplyingAuthoritativeCommand = false;
            CurrentPlayerId = 0;
        }
    }
}
