using Ship_Game.Ships;
using Ship_Game.Universe;

namespace Ship_Game.Determinism.Lockstep;

/// <summary>
/// Maps SimCommand stable ids to StarDrive entities (advisor plan RC3). Uses StarDrive's existing
/// deterministic ids: Empire.Id (1-indexed list position) and Ship.Id (monotonic CreateId). Lookups
/// are by id (GetEmpireById / FindShip), never by enumeration order, so resolution is deterministic.
/// </summary>
public sealed class StarDriveEntityResolver
{
    readonly UniverseState UState;

    public StarDriveEntityResolver(UniverseState universe) => UState = universe;

    public Empire ResolveEmpire(int playerId) => UState.GetEmpireById(playerId);

    public Ship ResolveShip(int shipId) => UState.Objects.FindShip(shipId);

    public bool Owns(Empire empire, Ship ship) => empire != null && ship != null && ship.Loyalty == empire;
}
