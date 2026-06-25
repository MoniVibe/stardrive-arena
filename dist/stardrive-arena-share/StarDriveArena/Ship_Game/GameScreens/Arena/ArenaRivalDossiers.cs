using System;
using System.Collections.Generic;
using System.Globalization;
using SDUtils;

namespace Ship_Game.GameScreens.Arena;

public static class ArenaRivalDossiers
{
    public const int MaxIdLength = 40;
    public const int MaxNameLength = 72;
    public const int MaxEpithetLength = 48;
    public const int MaxPersonaLength = 40;
    public const int MaxOriginLength = 80;
    public const int MaxFleetScaleLength = 16;
    public const int MaxBioLength = 180;
    public const string DefaultCareerSeed = "A12E0D0551D0A11E";

    static readonly string[] Epithets =
    {
        "Iron Halo", "Void Choir", "Last Vector", "Amber Wake", "Black Circuit", "Cold Banner",
        "Nova Debt", "Shard Crown", "Grave Signal", "Red Meridian", "Silent Lance", "Broken Orbit",
    };

    static readonly string[] Personas =
    {
        "Brawler", "Kiter", "Alpha Striker", "Shield Bully", "Carrier Minder", "Duelist",
        "Scrap Baron", "Formation Caller", "Siege Pilot", "Knife-Range Ace",
    };

    static readonly string[] Origins =
    {
        "rim militia", "breaker yard", "merchant escort circuit", "Kulrathi border",
        "Remnant salvage cult", "Terran proving ring", "deep-space convoy line",
        "privateer tribunal", "moon foundry league", "academy disgrace docket",
    };

    static readonly string[] FleetScales = { "solo", "duo", "trio", "squad" };

    public static string NormalizeCareerSeed(string seed)
    {
        string clean = Clean(seed, 32).ToUpperInvariant();
        return clean.NotEmpty() ? clean : DefaultCareerSeed;
    }

    public static void NormalizeDossier(ContenderRecord contender, string careerSeed,
        HashSet<string> activeIds = null)
    {
        if (contender == null)
            return;

        careerSeed = NormalizeCareerSeed(careerSeed);
        contender.Name = Clean(contender.Name.NotEmpty() ? contender.Name : contender.DesignName, MaxNameLength);
        contender.DesignName = Clean(contender.DesignName, MaxNameLength);
        contender.RoleClass = Clean(contender.RoleClass, MaxPersonaLength);
        string basis = $"{careerSeed}|{contender.Name}|{contender.DesignName}|{contender.RoleClass}";
        ulong hash = StableHash(basis);

        contender.ContenderId = Clean(contender.ContenderId, MaxIdLength);
        if (contender.ContenderId.IsEmpty() || activeIds?.Contains(contender.ContenderId) == true)
            contender.ContenderId = UniqueId($"rival-{hash:X12}".ToLowerInvariant(), activeIds);
        else
            activeIds?.Add(contender.ContenderId);

        contender.Epithet = CleanOrPick(contender.Epithet, Epithets, hash, 0, MaxEpithetLength);
        contender.ArenaPersona = CleanOrPick(contender.ArenaPersona, Personas, hash, 11, MaxPersonaLength);
        contender.OriginHook = CleanOrPick(contender.OriginHook, Origins, hash, 23, MaxOriginLength);
        contender.PreferredFleetScale = CleanOrPick(contender.PreferredFleetScale, FleetScales, hash, 31, MaxFleetScaleLength);
        contender.Bio = Clean(contender.Bio, MaxBioLength);
        if (contender.Bio.IsEmpty())
        {
            contender.Bio = Clean(
                $"{contender.Name} is a {contender.ArenaPersona.ToLowerInvariant()} from the " +
                $"{contender.OriginHook}, known as {contender.Epithet}.",
                MaxBioLength);
        }
    }

    public static ContenderRecord[] NormalizeRetired(ContenderRecord[] contenders, string careerSeed)
    {
        if (contenders == null || contenders.Length == 0)
            return Empty<ContenderRecord>.Array;

        var result = new List<ContenderRecord>(contenders.Length);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ContenderRecord contender in contenders)
        {
            if (contender == null || contender.DesignName.IsEmpty())
                continue;
            NormalizeDossier(contender, careerSeed, seenIds);
            result.Add(contender);
        }
        return result.Count > 0 ? result.ToArray() : Empty<ContenderRecord>.Array;
    }

    static string CleanOrPick(string existing, string[] table, ulong hash, int shift, int max)
    {
        string clean = Clean(existing, max);
        if (clean.NotEmpty())
            return clean;
        ulong mixed = hash >> (shift % 32);
        return table[(int)(mixed % (ulong)table.Length)];
    }

    static string UniqueId(string root, HashSet<string> activeIds)
    {
        if (activeIds == null)
            return Clean(root, MaxIdLength);

        string id = Clean(root, MaxIdLength);
        for (int n = 2; id.IsEmpty() || activeIds.Contains(id); ++n)
            id = Clean($"{root}-{n}", MaxIdLength);
        activeIds.Add(id);
        return id;
    }

    public static string Clean(string value, int max)
    {
        string clean = (value ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return clean.Length <= max ? clean : clean.Substring(0, max);
    }

    public static ulong StableHash(string text)
    {
        unchecked
        {
            ulong h = 14695981039346656037ul;
            foreach (char c in text ?? "")
            {
                h ^= c;
                h *= 1099511628211ul;
            }
            return h;
        }
    }

    public static string SeedText(ulong seed)
        => seed.ToString("X16", CultureInfo.InvariantCulture);
}
