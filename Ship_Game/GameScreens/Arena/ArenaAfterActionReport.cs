using System;
using System.Collections.Generic;
using System.Linq;
using Ship_Game.Ships;

namespace Ship_Game.GameScreens.Arena;

// ADDENDUM 3 — AFTER-ACTION REPORT. A pure read-out of the transient per-ship stat counters
// (Ship.ArenaDamageDealt / Ship.ArenaDamageAbsorbed accumulated at the deterministic damage sites, and the
// existing Ship.Kills) gathered at the Resolve phase. It NEVER feeds back into the sim and is NOT part of any
// checksum/fingerprint. Because the counters increment at deterministic sites inside the shared lockstep sim,
// two peers gather byte-identical reports for free — the report is verified identical in the headless proof.
//
// Determinism note: totals are rounded to whole numbers for display and reconciliation so tiny float drift in
// the LAST-mile display never matters; the underlying counters are the exact deterministic floats.

public readonly struct ArenaShipStatRow
{
    public readonly int ShipId;        // stable sim id (canonical sort key)
    public readonly string DesignName; // the design name (deterministic identity)
    public readonly string DisplayName;// VanityName-or-design (what the player sees)
    public readonly bool Survived;     // alive at match end
    public readonly int Kills;         // Ship.Kills (already tracked)
    public readonly float DamageDealt;
    public readonly float DamageTaken;
    public readonly float DamageAbsorbed;

    public ArenaShipStatRow(int shipId, string designName, string displayName, bool survived,
        int kills, float damageDealt, float damageTaken, float damageAbsorbed)
    {
        ShipId = shipId;
        DesignName = designName ?? "";
        DisplayName = displayName ?? "";
        Survived = survived;
        Kills = kills;
        DamageDealt = damageDealt;
        DamageTaken = damageTaken;
        DamageAbsorbed = damageAbsorbed;
    }

    public static ArenaShipStatRow From(Ship s)
        => new(s.Id, s.Name ?? "", s.ShipName ?? "", s.Active && !s.Dying,
               s.Kills, s.ArenaDamageDealt, s.ArenaDamageTaken, s.ArenaDamageAbsorbed);
}

public sealed class ArenaAfterActionSide
{
    public string Label = "";                 // "Host" / "Join"
    public int StartCount;                     // ships fielded
    public int Survivors;                      // ships alive at end
    public int Kills;                          // total kills by this side
    public float DamageDealt;                  // total damage this side landed on the enemy
    public float DamageTaken;                  // total damage landed on this side
    public float DamageAbsorbed;               // total damage this side's defenses negated
    public ArenaShipStatRow[] Ships = Array.Empty<ArenaShipStatRow>();

    // Highlight ships (may be default/empty when the side fielded nothing).
    public ArenaShipStatRow TopDamageDealer;
    public ArenaShipStatRow TopDamageAbsorber;
    public ArenaShipStatRow TopKiller;
    public bool HasShips => Ships.Length > 0;
}

public sealed class ArenaAfterActionReport
{
    public ArenaAfterActionSide Host = new();
    public ArenaAfterActionSide Join = new();

    // The single-line canonical signature used by the headless proof to assert both peers agree byte-for-byte
    // and that the counters reconcile. Whole-number totals so float last-mile drift is display-immaterial.
    public string Signature()
    {
        static string Side(ArenaAfterActionSide s)
        {
            string rows = string.Join(";", s.Ships
                .OrderBy(r => r.ShipId)
                .Select(r => $"{r.ShipId}:{(r.Survived ? 1 : 0)}:{r.Kills}"
                             + $":{(long)Math.Round(r.DamageDealt)}:{(long)Math.Round(r.DamageTaken)}"
                             + $":{(long)Math.Round(r.DamageAbsorbed)}"));
            return $"n={s.StartCount},surv={s.Survivors},k={s.Kills}"
                   + $",dd={(long)Math.Round(s.DamageDealt)},dt={(long)Math.Round(s.DamageTaken)}"
                   + $",da={(long)Math.Round(s.DamageAbsorbed)}[{rows}]";
        }
        return $"HOST({Side(Host)})JOIN({Side(Join)})";
    }

    // Sum of all damage dealt across both sides (attacker view).
    public float TotalDamageDealt => Host.DamageDealt + Join.DamageDealt;

    // Sum of all damage taken across both sides (defender view). Equals TotalDamageDealt by construction
    // (dealt and taken are credited at the SAME site with the SAME amount) — the reconciliation invariant.
    public float TotalDamageTaken => Host.DamageTaken + Join.DamageTaken;

    // Sum of all damage absorbed across both sides (defender view).
    public float TotalDamageAbsorbed => Host.DamageAbsorbed + Join.DamageAbsorbed;

    // Gather the report from the two managed ship lists. Ships may be null/removed; skip those safely.
    // Includes ships whether alive or dead so damage from ships that later died still counts.
    public static ArenaAfterActionReport Gather(IReadOnlyList<Ship> hostShips, IReadOnlyList<Ship> joinShips)
    {
        return new ArenaAfterActionReport
        {
            Host = BuildSide("Host", hostShips),
            Join = BuildSide("Join", joinShips),
        };
    }

    static ArenaAfterActionSide BuildSide(string label, IReadOnlyList<Ship> ships)
    {
        var side = new ArenaAfterActionSide { Label = label };
        if (ships == null)
            return side;

        // Canonical ship order: by stable Id, so the gathered report is identical on both peers.
        ArenaShipStatRow[] rows = ships
            .Where(s => s != null)
            .Select(ArenaShipStatRow.From)
            .OrderBy(r => r.ShipId)
            .ToArray();

        side.Ships = rows;
        side.StartCount = rows.Length;
        side.Survivors = rows.Count(r => r.Survived);
        side.Kills = rows.Sum(r => r.Kills);
        side.DamageDealt = rows.Sum(r => r.DamageDealt);
        side.DamageTaken = rows.Sum(r => r.DamageTaken);
        side.DamageAbsorbed = rows.Sum(r => r.DamageAbsorbed);

        if (rows.Length > 0)
        {
            // Deterministic tie-break: highest metric, then lowest ship id.
            side.TopDamageDealer   = rows.OrderByDescending(r => r.DamageDealt).ThenBy(r => r.ShipId).First();
            side.TopDamageAbsorber = rows.OrderByDescending(r => r.DamageAbsorbed).ThenBy(r => r.ShipId).First();
            side.TopKiller         = rows.OrderByDescending(r => r.Kills).ThenBy(r => r.ShipId).First();
        }
        return side;
    }
}
