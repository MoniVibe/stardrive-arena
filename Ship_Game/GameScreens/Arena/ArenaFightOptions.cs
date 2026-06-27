using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using SDUtils;
using Ship_Game.Ships;

namespace Ship_Game.GameScreens.Arena;

public enum FightRiskTier
{
    Safe,
    Standard,
    Risky,
    Elite,
}

public enum FightDifficultyTier
{
    Trivial,
    Easy,
    Normal,
    Hard,
    Wildcard,
}

public enum FightOptionType
{
    Normal,
    Boss,
    LeagueBout,
    ContenderChallenge,
}

public enum ArenaLootKind
{
    ResearchUnlock,
    ChassisUnlock,
    Perk,
    BonusCash,
}

public readonly struct LootReward
{
    public readonly ArenaLootKind Kind;
    public readonly string ModuleUid;
    public readonly int Count;
    public readonly string ChassisStyle;
    public readonly string HullName;
    public readonly string PerkId;
    public readonly int Cash;

    LootReward(ArenaLootKind kind, string moduleUid, int count, string chassisStyle,
        string hullName, string perkId, int cash)
    {
        Kind = kind;
        ModuleUid = moduleUid ?? "";
        Count = Math.Max(0, count);
        ChassisStyle = chassisStyle ?? "";
        HullName = hullName ?? "";
        PerkId = perkId ?? "";
        Cash = Math.Max(0, cash);
    }

    public static LootReward Research(string moduleUid)
        => new(ArenaLootKind.ResearchUnlock, moduleUid, 1, "", "", "", 0);

    public static LootReward Chassis(string style, string hullName)
        => new(ArenaLootKind.ChassisUnlock, "", 0, style, hullName, "", 0);

    public static LootReward Perk(string perkId)
        => new(ArenaLootKind.Perk, "", 0, "", "", perkId, 0);

    public static LootReward BonusCash(int cash)
        => new(ArenaLootKind.BonusCash, "", 0, "", "", "", cash);

    public int ExpectedValue => Kind switch
    {
        ArenaLootKind.ResearchUnlock => 750,
        ArenaLootKind.ChassisUnlock => 250,
        ArenaLootKind.Perk          => 500,
        ArenaLootKind.BonusCash     => Cash,
        _                           => 0,
    };

    public string Signature => Kind switch
    {
        ArenaLootKind.ResearchUnlock => $"research:{ModuleUid}",
        ArenaLootKind.ChassisUnlock => $"chassis:{ChassisStyle}:{HullName}",
        ArenaLootKind.Perk          => $"perk:{PerkId}",
        ArenaLootKind.BonusCash     => $"cash:{Cash}",
        _                           => Kind.ToString(),
    };
}

public sealed class FightOption
{
    public readonly string OptionId;
    public readonly FightOptionType FightType;
    public readonly FightRiskTier RiskTier;
    public readonly string EscortDesignName;
    public readonly string BossDesignName;
    public readonly int EnemyCount;
    public readonly int BossCount;
    public readonly int MaxEnemyTier;
    public readonly ArenaFightModifier Modifier;
    public readonly ArenaBossEncounter BossEncounter;
    public readonly int RewardCash;
    public readonly int RewardFame;
    public readonly LootReward[] PreviewLoot;
    public readonly float DifficultyScore;
    public readonly FightDifficultyTier DifficultyTier;
    public readonly float StrengthRatio;
    public readonly float EstimatedWinRate;
    public readonly int ExpectedRewardValue;
    public readonly bool IsSpecialContract;
    public readonly string ContractName;

    public FightOption(string optionId, FightOptionType fightType, FightRiskTier riskTier,
        FightDifficultyTier difficultyTier, string escortDesignName,
        string bossDesignName, int enemyCount, int bossCount, int maxEnemyTier,
        ArenaFightModifier modifier, ArenaBossEncounter bossEncounter, int rewardCash,
        int rewardFame, LootReward[] previewLoot, float difficultyScore, float strengthRatio,
        float estimatedWinRate, int expectedRewardValue, bool isSpecialContract = false,
        string contractName = "")
    {
        OptionId = optionId ?? "";
        FightType = fightType;
        RiskTier = riskTier;
        DifficultyTier = difficultyTier;
        EscortDesignName = escortDesignName ?? "";
        BossDesignName = bossDesignName ?? "";
        EnemyCount = Math.Max(1, enemyCount);
        BossCount = Math.Clamp(bossCount, 0, EnemyCount);
        MaxEnemyTier = Math.Clamp(maxEnemyTier, ArenaFightScreen.MinCombatTier, ArenaFightScreen.MaxCombatTier);
        Modifier = modifier;
        BossEncounter = bossEncounter;
        RewardCash = Math.Max(0, rewardCash);
        RewardFame = Math.Max(0, rewardFame);
        PreviewLoot = previewLoot ?? Array.Empty<LootReward>();
        DifficultyScore = Math.Max(1f, difficultyScore);
        StrengthRatio = Math.Max(0.01f, strengthRatio);
        EstimatedWinRate = Math.Clamp(estimatedWinRate, 0.01f, 0.99f);
        ExpectedRewardValue = Math.Max(RewardCash, expectedRewardValue);
        IsSpecialContract = isSpecialContract;
        ContractName = contractName ?? "";
    }

    public bool IsElite => RiskTier == FightRiskTier.Elite;
    public bool HasBoss => BossEncounter.Active || BossCount > 0 || BossDesignName.NotEmpty();
    public int LootRolls => PreviewLoot.Length;
    public int PreviewCashTotal => RewardCash + PreviewLoot.Sum(r => r.Kind == ArenaLootKind.BonusCash ? r.Cash : 0);

    public string Signature
        => $"{FightType}|{RiskTier}|{DifficultyTier}|{EscortDesignName}|{BossDesignName}|{EnemyCount}|{BossCount}|"
         + $"{Modifier.Kind}|{RewardCash}|{RewardFame}|{StrengthRatio:0.000}|{IsSpecialContract}|{ContractName}|"
         + string.Join(",", PreviewLoot.Select(r => r.Signature));
}

public sealed class ArenaFightOptionsReport
{
    public readonly FightOption[] Options;
    public readonly string Json;

    public ArenaFightOptionsReport(FightOption[] options, string json)
    {
        Options = options ?? Array.Empty<FightOption>();
        Json = json ?? "";
    }
}

public static class ArenaFightOptions
{
    public const ulong DefaultSeed = 0xA12E_F16A_0F71_0001ul;
    public const int SpecialContractFame = 60;
    public const int FullSlateFame = 100;

    public static FightOption[] GenerateFightOptions(ArenaCareer career, ulong seed)
    {
        int level = Math.Max(0, career?.CareerLevel ?? 0);
        int maxTier = ArenaFightScreen.MaxAllowedCombatTierForCareerLevel(level);
        int fame = career?.Fame ?? 0;
        FightDifficultyTier[] slate = ApplyFightChoiceMeta(SlateForFame(fame, maxTier),
            ArenaPerks.ExtraFightChoices(career?.Perks), fame, maxTier);
        int contractIndex = SpecialContractIndex(slate, fame);

        var options = new FightOption[slate.Length];
        var usedEnemyDesigns = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < slate.Length; ++i)
        {
            options[i] = BuildOption(career, seed, slate[i], i, maxTier, i == contractIndex,
                usedEnemyDesigns);
            if (options[i].EscortDesignName.NotEmpty())
                usedEnemyDesigns.Add(options[i].EscortDesignName);
            if (options[i].BossDesignName.NotEmpty())
                usedEnemyDesigns.Add(options[i].BossDesignName);
        }
        InjectReckoningOption(career, seed, maxTier, options);
        return options;
    }

    static void InjectReckoningOption(ArenaCareer career, ulong seed, int maxTier, FightOption[] options)
    {
        if (career == null || options == null || options.Length == 0)
            return;

        ContenderRecord nemesis = career.PinnedNemeses()
            .FirstOrDefault(c => c?.DesignName.NotEmpty() == true
                              && ResourceManager.Ships.GetDesign(c.DesignName, out IShipDesign d)
                              && ArenaFightScreen.IsLegalCombatCraft(d)
                              && ArenaFightScreen.IsDesignAllowedForCareerLevel(d, Math.Max(0, career.CareerLevel)));
        if (nemesis == null || !ResourceManager.Ships.GetDesign(nemesis.DesignName, out IShipDesign design))
            return;

        int index = Array.FindIndex(options, o => o != null && o.DifficultyTier != FightDifficultyTier.Trivial);
        if (index < 0)
            index = 0;
        options[index] = BuildReckoningOption(career, seed, maxTier, nemesis, design);
    }

    static FightOption BuildReckoningOption(ArenaCareer career, ulong seed, int maxTier,
        ContenderRecord nemesis, IShipDesign design)
    {
        int level = Math.Max(0, career?.CareerLevel ?? 0);
        float playerStrength = PlayerStrengthForCareer(career, level);
        var modifier = new ArenaFightModifier(ArenaFightModifierKind.EliteVanguard,
            "Reckoning Grudge", 0, 0, 1.08f, 1.08f);
        float enemyStrength = Math.Max(1f, design.BaseStrength) * modifier.EnemyStrengthMultiplier;
        float strengthRatio = enemyStrength / Math.Max(1f, playerStrength);
        float winRate = playerStrength / Math.Max(1f, playerStrength + enemyStrength);
        int cash = RewardCashForEnemyStrength(career, enemyStrength, specialContract: true);
        int fame = RewardFameForDifficulty(FightDifficultyTier.Hard, strengthRatio, specialContract: true);
        LootReward[] loot = PreviewLootForOption(FightDifficultyTier.Hard, FightRiskTier.Risky,
            design, null, cash, seed ^ ArenaRivalDossiers.StableHash(nemesis.ContenderId), specialContract: true);
        int expected = cash + loot.Sum(r => r.ExpectedValue);
        string contractName = $"Reckoning: {nemesis.Name}";
        string id = $"reckoning-{nemesis.ContenderId}-{seed:x16}";
        return new FightOption(id, FightOptionType.ContenderChallenge, FightRiskTier.Risky,
            FightDifficultyTier.Hard, design.Name, "", 1, 0, maxTier, modifier,
            ArenaBossEncounter.None, cash, fame, loot, enemyStrength, strengthRatio,
            winRate, expected, isSpecialContract: true, contractName);
    }

    public static FightDifficultyTier[] SlateForFame(int fame, int maxTier = ArenaFightScreen.MaxCombatTier)
    {
        fame = Math.Max(0, fame);
        var slate = new List<FightDifficultyTier>
        {
            FightDifficultyTier.Trivial,
            FightDifficultyTier.Easy,
            FightDifficultyTier.Easy,
            FightDifficultyTier.Normal,
            FightDifficultyTier.Normal,
            FightDifficultyTier.Hard,
        };

        if (fame >= 60) slate.Add(FightDifficultyTier.Hard);
        if (fame >= 80) slate.Add(FightDifficultyTier.Hard);

        // Tier-3 careers keep access to the boss payoff route even at low fame; below tier 3,
        // wildcards stay locked until fame makes the public slate large enough.
        if (maxTier >= ArenaFightScreen.MaxCombatTier || fame >= 80)
            slate.Add(FightDifficultyTier.Wildcard);

        if (fame >= FullSlateFame)
        {
            slate.Clear();
            slate.Add(FightDifficultyTier.Trivial);
            slate.Add(FightDifficultyTier.Easy);
            slate.Add(FightDifficultyTier.Easy);
            slate.Add(FightDifficultyTier.Normal);
            slate.Add(FightDifficultyTier.Normal);
            slate.Add(FightDifficultyTier.Hard);
            slate.Add(FightDifficultyTier.Hard);
            slate.Add(FightDifficultyTier.Hard);
            slate.Add(FightDifficultyTier.Wildcard);
            slate.Add(FightDifficultyTier.Wildcard);
        }

        if (fame > FullSlateFame)
        {
            int extraWildcards = Math.Min(3, (fame - FullSlateFame) / 50);
            for (int i = 0; i < extraWildcards; ++i)
                slate.Add(FightDifficultyTier.Wildcard);
        }

        return slate.ToArray();
    }

    static FightDifficultyTier[] ApplyFightChoiceMeta(FightDifficultyTier[] slate, int extraChoices,
        int fame, int maxTier)
    {
        if (extraChoices <= 0)
            return slate ?? Array.Empty<FightDifficultyTier>();

        var list = new List<FightDifficultyTier>(slate ?? Array.Empty<FightDifficultyTier>());
        for (int i = 0; i < extraChoices; ++i)
        {
            FightDifficultyTier extra = fame >= 80 ? FightDifficultyTier.Hard
                : fame >= 40 ? FightDifficultyTier.Normal
                : FightDifficultyTier.Easy;
            if (maxTier >= ArenaFightScreen.MaxCombatTier && i == extraChoices - 1 && fame >= 80)
                extra = FightDifficultyTier.Wildcard;
            list.Add(extra);
        }

        return list.OrderBy(DifficultyRank).ToArray();
    }

    static int SpecialContractIndex(FightDifficultyTier[] slate, int fame)
    {
        if (fame < SpecialContractFame || slate == null || slate.Length == 0)
            return -1;

        int hard = Array.FindIndex(slate, d => d == FightDifficultyTier.Hard);
        if (hard >= 0)
            return hard;
        int normal = Array.FindIndex(slate, d => d == FightDifficultyTier.Normal);
        return normal >= 0 ? normal : slate.Length - 1;
    }

    public static ulong SeedForCareer(ArenaCareer career, int nextRound)
    {
        int level = Math.Max(0, career?.CareerLevel ?? 0);
        int cash = Math.Max(0, career?.Cash ?? 0);
        int fame = Math.Max(0, career?.Fame ?? 0);
        int perks = career?.Perks?.Length ?? 0;
        int researchedKinds = career?.ResearchedModules?.Length ?? 0;
        ulong x = DefaultSeed;
        x ^= ((ulong)(uint)level << 40);
        x ^= ((ulong)(uint)fame << 16);
        x ^= ((ulong)(uint)cash << 8);
        x ^= ((ulong)(uint)Math.Max(0, nextRound) << 24);
        x ^= ((ulong)(uint)perks << 4);
        x ^= (uint)researchedKinds;
        return Mix(x);
    }

    static FightOption BuildOption(ArenaCareer career, ulong seed, FightDifficultyTier difficulty,
        int optionIndex, int maxTier, bool specialContract, HashSet<string> usedEnemyDesigns)
    {
        int level = Math.Max(0, career?.CareerLevel ?? 0);
        FightRiskTier risk = RiskForDifficulty(difficulty, seed, optionIndex, maxTier);
        int riskRank = RiskRank(risk);
        int difficultyRank = DifficultyRank(difficulty);
        ulong optionSeed = Mix(seed ^ ((ulong)(uint)riskRank << 32)
                                    ^ ((ulong)(uint)difficultyRank << 44)
                                    ^ (uint)optionIndex);

        FightOptionType type = PickType(career, risk, difficulty, optionSeed, maxTier);
        ArenaFightModifier modifier = ModifierForDifficulty(difficulty, risk, type);
        float playerStrength = PlayerStrengthForCareer(career, level);
        int playerFieldedCount = PlayerFieldedCountForCareer(career, level);
        float targetRatio = StrengthRatioForDifficulty(difficulty, risk, optionSeed);
        int count = EnemyCountForPlayerFleet(difficulty, risk, playerFieldedCount, type, optionSeed);
        float targetPerShipStrength = TargetPerEnemyShipStrength(playerStrength, targetRatio, count, modifier);

        ArenaBossEncounter bossEncounter = type == FightOptionType.Boss && risk == FightRiskTier.Elite
            ? ArenaFightScreen.PickEliteBossEncounter(null, level)
            : ArenaBossEncounter.None;

        IShipDesign escort = type == FightOptionType.Boss && risk == FightRiskTier.Elite
            ? null
            : PickScaledEnemy(career, risk, type, level, maxTier, optionSeed, targetPerShipStrength,
                usedEnemyDesigns);
        IShipDesign bossDesign = bossEncounter.Active && ResourceManager.Ships.GetDesign(bossEncounter.DesignName, out IShipDesign boss)
            ? boss
            : (type == FightOptionType.Boss && maxTier >= 3 ? ArenaFightScreen.PickEnemyBoss(null, level) : null);

        if (escort == null)
            escort = PickScaledEnemy(career, risk, FightOptionType.Normal, level, maxTier,
                         optionSeed ^ 0xA11CEul, targetPerShipStrength, usedEnemyDesigns)
                  ?? ArenaFightScreen.PickEnemyEscort(null, 1, level);

        int bossCount = risk == FightRiskTier.Elite ? 1 : (bossDesign != null && type == FightOptionType.Boss ? 1 : 0);
        if (risk != FightRiskTier.Elite && bossCount >= count)
            bossCount = Math.Max(0, count - 1);

        float enemyStrength = EnemyStrength(escort, bossDesign, count, bossCount, modifier, bossEncounter);
        float difficultyScore = enemyStrength;
        float strengthRatio = difficultyScore / Math.Max(1f, playerStrength);
        float winRate = playerStrength / Math.Max(1f, playerStrength + difficultyScore);

        int cash = RewardCashForEnemyStrength(career, enemyStrength, specialContract);
        int fame = RewardFameForDifficulty(difficulty, strengthRatio, specialContract);
        LootReward[] loot = PreviewLootForOption(difficulty, risk, escort, bossDesign, cash, optionSeed, specialContract);
        int expected = cash + loot.Sum(r => r.ExpectedValue);
        string contractName = specialContract ? ContractNameFor(difficulty, optionSeed) : "";
        string id = $"{type}-{difficulty}-{risk}-{optionSeed:x16}";
        string bossName = bossEncounter.Active ? bossEncounter.DesignName : bossDesign?.Name;

        return new FightOption(id, type, risk, difficulty, escort?.Name, bossName, count, bossCount, maxTier,
            modifier, bossEncounter, cash, fame, loot, difficultyScore, strengthRatio,
            winRate, expected, specialContract, contractName);
    }

    static FightRiskTier RiskForDifficulty(FightDifficultyTier difficulty, ulong seed, int optionIndex, int maxTier)
    {
        return difficulty switch
        {
            FightDifficultyTier.Trivial => FightRiskTier.Safe,
            FightDifficultyTier.Easy    => FightRiskTier.Safe,
            FightDifficultyTier.Normal  => FightRiskTier.Standard,
            FightDifficultyTier.Hard    => FightRiskTier.Risky,
            FightDifficultyTier.Wildcard => WildcardRisk(seed, optionIndex, maxTier),
            _ => FightRiskTier.Standard,
        };
    }

    static FightRiskTier WildcardRisk(ulong seed, int optionIndex, int maxTier)
    {
        if (maxTier >= ArenaFightScreen.MaxCombatTier && optionIndex % 3 == 0)
            return FightRiskTier.Elite;

        FightRiskTier[] risks = maxTier >= ArenaFightScreen.MaxCombatTier
            ? new[] { FightRiskTier.Safe, FightRiskTier.Standard, FightRiskTier.Risky, FightRiskTier.Elite }
            : new[] { FightRiskTier.Safe, FightRiskTier.Standard, FightRiskTier.Risky };
        return risks[(int)(Mix(seed ^ ((ulong)(uint)optionIndex << 20) ^ 0xA12E_711Dul) % (ulong)risks.Length)];
    }

    static FightOptionType PickType(ArenaCareer career, FightRiskTier risk, FightDifficultyTier difficulty,
        ulong optionSeed, int maxTier)
    {
        if (risk == FightRiskTier.Elite && maxTier >= 3)
            return FightOptionType.Boss;
        if (difficulty == FightDifficultyTier.Trivial || risk == FightRiskTier.Safe || maxTier <= 1)
            return FightOptionType.Normal;

        var types = new List<FightOptionType> { FightOptionType.Normal };
        if (career?.Contenders != null && career.Contenders.Length > 0)
            types.Add(FightOptionType.ContenderChallenge);
        if (maxTier >= 2)
            types.Add(FightOptionType.LeagueBout);
        if (maxTier >= 3 && risk == FightRiskTier.Risky)
            types.Add(FightOptionType.Boss);

        return types[(int)(Mix(optionSeed ^ 0xC0A7C4ul) % (ulong)types.Count)];
    }

    static int RoundHintForRisk(FightRiskTier risk) => risk switch
    {
        FightRiskTier.Safe     => 1,
        FightRiskTier.Standard => 2,
        FightRiskTier.Risky    => ArenaFightScreen.TotalRounds,
        FightRiskTier.Elite    => ArenaFightScreen.BossEncounterRound,
        _                      => 1,
    };

    static ArenaFightModifier ModifierForDifficulty(FightDifficultyTier difficulty, FightRiskTier risk,
        FightOptionType type)
    {
        if (difficulty == FightDifficultyTier.Trivial)
            return new ArenaFightModifier(ArenaFightModifierKind.SafetyHandicap,
                "Safety Contract", 0, 0, 0.65f, 0.55f);
        if (difficulty == FightDifficultyTier.Easy)
            return new ArenaFightModifier(ArenaFightModifierKind.SafetyHandicap,
                "Easy Contract", 0, 0, 0.85f, 0.80f);
        if (type == FightOptionType.LeagueBout)
            return new ArenaFightModifier(ArenaFightModifierKind.Reinforced,
                "League Rules", 0, 0, 1.06f, 1.04f);
        if (type == FightOptionType.ContenderChallenge)
            return new ArenaFightModifier(ArenaFightModifierKind.EliteVanguard,
                "Rival Challenge", 0, 0, 1.10f, 1.06f);

        return risk switch
    {
        FightRiskTier.Safe => ArenaFightModifier.None,
        FightRiskTier.Standard => new ArenaFightModifier(ArenaFightModifierKind.Reinforced,
            "Standard Contract", 0, 0, 1.04f, 1.03f),
        FightRiskTier.Risky => new ArenaFightModifier(ArenaFightModifierKind.Swarm,
            "Risky Contract", 1, 0, 1.08f, 1.08f),
        FightRiskTier.Elite => new ArenaFightModifier(ArenaFightModifierKind.EliteVanguard,
            "Elite Bounty", 0, 0, 1.12f, 1.12f),
        _ => ArenaFightModifier.None,
    };
    }

    static int EnemyCountForPlayerFleet(FightDifficultyTier difficulty, FightRiskTier risk,
        int playerFieldedCount, FightOptionType type, ulong seed)
    {
        int playerCount = Math.Max(1, playerFieldedCount);
        float ratio = StrengthRatioForDifficulty(difficulty, risk, seed);
        int count = (int)Math.Round(playerCount * ratio);
        if (type == FightOptionType.Boss && risk == FightRiskTier.Elite)
            count = 1;
        return Math.Clamp(count, 1, ArenaPerks.MaxFleetSizeCap);
    }

    public static float StrengthRatioForDifficulty(FightDifficultyTier difficulty, FightRiskTier risk, ulong seed)
    {
        if (difficulty == FightDifficultyTier.Wildcard)
        {
            float roll = (Mix(seed ^ 0xD1FF1Cul) & 0xffff) / 65535f;
            return 0.50f + 1.50f * roll;
        }

        return difficulty switch
        {
            FightDifficultyTier.Trivial => 0.55f,
            FightDifficultyTier.Easy    => 0.80f,
            FightDifficultyTier.Normal  => 1.00f,
            FightDifficultyTier.Hard    => 1.35f,
            _ => risk == FightRiskTier.Elite ? 1.75f : 1.00f,
        };
    }

    static float TargetPerEnemyShipStrength(float playerStrength, float targetRatio, int enemyCount,
        ArenaFightModifier modifier)
    {
        float multiplier = Math.Max(0.1f, modifier.EnemyStrengthMultiplier);
        return Math.Max(1f, playerStrength * targetRatio / Math.Max(1, enemyCount) / multiplier);
    }

    static IShipDesign PickScaledEnemy(ArenaCareer career, FightRiskTier risk, FightOptionType type,
        int careerLevel, int maxTier, ulong seed, float targetBaseStrength = -1f,
        HashSet<string> usedEnemyDesigns = null)
    {
        HashSet<string> playerDesignNames = PlayerDesignNameSetForCareer(career, careerLevel);

        if (type == FightOptionType.ContenderChallenge)
        {
            ContenderRecord[] contenders = career?.Contenders ?? Array.Empty<ContenderRecord>();
            ScaledEnemyCandidate[] contenderDesigns = contenders
                .Where(c => c != null && c.DesignName.NotEmpty())
                .Select(c => ResourceManager.Ships.GetDesign(c.DesignName, out IShipDesign d) ? d : null)
                .Where(d => d != null && ArenaFightScreen.IsLegalCombatCraft(d)
                         && ArenaFightScreen.IsDesignAllowedForCareerLevel(d, careerLevel))
                .Select(d => new ScaledEnemyCandidate(d, ArenaFightScreen.CombatTierForDesign(d),
                    playerDesignNames.Contains(d.Name)))
                .ToArray();
            if (contenderDesigns.Length > 0)
                return PickFromCandidates(
                    AvoidUsedIfPossible(AvoidMirrorsIfPossible(contenderDesigns), usedEnemyDesigns),
                    risk, seed, targetBaseStrength);
        }

        int minTier = risk == FightRiskTier.Safe ? 1 : Math.Max(1, maxTier - 1);
        if (type == FightOptionType.LeagueBout)
            minTier = Math.Max(1, maxTier - 1);
        ScaledEnemyCandidate[] designs = ResourceManager.Ships.Designs
            .Where(d => ArenaFightScreen.IsLegalCombatCraft(d)
                     && ArenaFightScreen.IsDesignAllowedForCareerLevel(d, careerLevel))
            .Select(d => new ScaledEnemyCandidate(d, ArenaFightScreen.CombatTierForDesign(d),
                playerDesignNames.Contains(d.Name)))
            .Where(x => x.Tier >= minTier && x.Tier <= maxTier)
            .ToArray();
        if (designs.Length == 0)
            return ArenaFightScreen.PickEnemyEscort(null, RoundHintForRisk(risk), careerLevel);

        designs = AvoidMirrorsIfPossible(designs);
        designs = AvoidUsedIfPossible(designs, usedEnemyDesigns);
        return PickFromCandidates(designs, risk, seed, targetBaseStrength);
    }

    static IShipDesign PickFromCandidates(ScaledEnemyCandidate[] designs, FightRiskTier risk, ulong seed,
        float targetBaseStrength)
    {
        if (designs == null || designs.Length == 0)
            return null;

        if (targetBaseStrength > 0f)
            return PickNearTargetVariant(designs, targetBaseStrength, seed);

        ScaledEnemyCandidate[] ordered = designs
            .OrderBy(x => x.Tier)
            .ThenBy(x => x.Design.BaseStrength)
            .ThenBy(x => x.Design.Name, StringComparer.Ordinal)
            .ToArray();

        int bandStart = risk == FightRiskTier.Safe ? 0 : ordered.Length / 4;
        int bandEnd = risk == FightRiskTier.Safe ? Math.Max(1, ordered.Length / 4)
                    : risk == FightRiskTier.Risky ? ordered.Length
                    : Math.Max(bandStart + 1, ordered.Length * 3 / 4);
        int span = Math.Max(1, bandEnd - bandStart);
        return ordered[bandStart + (int)(Mix(seed ^ 0xB0A7ul) % (ulong)span)].Design;
    }

    static IShipDesign PickNearTargetVariant(ScaledEnemyCandidate[] designs, float targetBaseStrength, ulong seed)
    {
        ScaledEnemyCandidate[] ordered = designs
            .OrderBy(x => Math.Abs(x.Design.BaseStrength - targetBaseStrength))
            .ThenBy(x => x.Tier)
            .ThenBy(x => x.Design.BaseStrength)
            .ThenBy(x => x.Design.Name, StringComparer.Ordinal)
            .ToArray();
        if (ordered.Length == 0)
            return null;

        float bestDelta = Math.Abs(ordered[0].Design.BaseStrength - targetBaseStrength);
        float tolerance = Math.Max(25f, targetBaseStrength * 0.18f);
        ScaledEnemyCandidate[] near = ordered
            .Where(x => Math.Abs(x.Design.BaseStrength - targetBaseStrength) <= bestDelta + tolerance)
            .Take(Math.Min(5, ordered.Length))
            .ToArray();
        if (near.Length == 0)
            near = ordered.Take(Math.Min(5, ordered.Length)).ToArray();

        return near[(int)(Mix(seed ^ 0xA12E_A11E_551Cul) % (ulong)near.Length)].Design;
    }

    static ScaledEnemyCandidate[] AvoidMirrorsIfPossible(ScaledEnemyCandidate[] designs)
    {
        ScaledEnemyCandidate[] nonMirrors = designs
            .Where(d => d != null && !d.MirrorsPlayerDesign)
            .ToArray();
        return nonMirrors.Length > 0 ? nonMirrors : designs;
    }

    static ScaledEnemyCandidate[] AvoidUsedIfPossible(ScaledEnemyCandidate[] designs, HashSet<string> usedDesigns)
    {
        if (usedDesigns == null || usedDesigns.Count == 0)
            return designs;
        ScaledEnemyCandidate[] unused = designs
            .Where(d => d?.Design?.Name.NotEmpty() == true && !usedDesigns.Contains(d.Design.Name))
            .ToArray();
        return unused.Length > 0 ? unused : designs;
    }

    public static string[] PlayerDesignNamesForCareer(ArenaCareer career, int careerLevel)
        => PlayerDesignNameSetForCareer(career, careerLevel)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

    static HashSet<string> PlayerDesignNameSetForCareer(ArenaCareer career, int careerLevel)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (OwnedVessel vessel in career?.FieldedFleetVessels() ?? Empty<OwnedVessel>.Array)
        {
            if (vessel?.DesignName.NotEmpty() == true
                && ResourceManager.Ships.GetDesign(vessel.DesignName, out IShipDesign design)
                && ArenaFightScreen.IsLegalCombatCraft(design)
                && ArenaFightScreen.IsDesignAllowedForCareerLevel(design, careerLevel))
            {
                names.Add(design.Name);
            }
        }

        if (names.Count == 0)
        {
            IShipDesign fallback = ArenaFightScreen.AutoPickPlayerWarship(null, careerLevel);
            if (fallback?.Name.NotEmpty() == true)
                names.Add(fallback.Name);
        }
        return names;
    }

    sealed class ScaledEnemyCandidate
    {
        public readonly IShipDesign Design;
        public readonly int Tier;
        public readonly bool MirrorsPlayerDesign;

        public ScaledEnemyCandidate(IShipDesign design, int tier, bool mirrorsPlayerDesign)
        {
            Design = design;
            Tier = tier;
            MirrorsPlayerDesign = mirrorsPlayerDesign;
        }
    }

    public static int RewardCashForEnemyStrength(ArenaCareer career, float enemyTotalStrength,
        bool specialContract = false)
    {
        int baseCash = ArenaPerks.CashPerClear(ArenaFightScreen.CashPerClear, career?.Perks);
        float strength = Math.Max(1f, enemyTotalStrength);
        double softCurve = Math.Sqrt(strength / 600.0);
        double cappedCurve = Math.Min(24.0, softCurve);
        int cash = RoundCash(baseCash * (0.35 + cappedCurve));
        if (specialContract)
            cash += Math.Max(100, cash / 2);

        int cap = Math.Max(baseCash, baseCash * (specialContract ? 90 : 60));
        int floor = Math.Max(50, baseCash / 4);
        return Math.Clamp(RoundCash(cash), floor, cap);
    }

    static int RoundCash(double value)
        => (int)(Math.Ceiling(Math.Max(0.0, value) / 25.0) * 25.0);

    static int RewardFameForDifficulty(FightDifficultyTier difficulty, float strengthRatio, bool specialContract)
    {
        int baseFame = difficulty switch
        {
            FightDifficultyTier.Trivial => 1,
            FightDifficultyTier.Easy    => 2,
            FightDifficultyTier.Normal  => 4,
            FightDifficultyTier.Hard    => 8,
            FightDifficultyTier.Wildcard => 10,
            _ => 3,
        };
        int upsetBonus = strengthRatio > 1f ? (int)Math.Ceiling((strengthRatio - 1f) * 8f) : 0;
        int contractBonus = specialContract ? 4 : 0;
        return Math.Max(0, baseFame + upsetBonus + contractBonus);
    }

    public static int FameLossPenalty(FightOption option)
        => option == null ? 0 : Math.Max(0, option.RewardFame / 4);

    static LootReward[] PreviewLootForOption(FightDifficultyTier difficulty, FightRiskTier risk,
        IShipDesign escort, IShipDesign boss, int rewardCash, ulong seed, bool specialContract)
    {
        IShipDesign lootDesign = boss ?? escort;
        string style = lootDesign?.BaseHull?.Style ?? "";
        string hull = lootDesign?.BaseHull?.HullName ?? "";
        string perkId = PickPerk(seed);

        var rewards = new List<LootReward>(4);
        rewards.Add(LootReward.BonusCash(Math.Max(25, rewardCash / 4)));

        if (difficulty == FightDifficultyTier.Easy)
        {
            rewards.Add(LootReward.BonusCash(Math.Max(20, rewardCash / 6)));
        }
        else if (difficulty == FightDifficultyTier.Normal)
        {
            rewards.Add(LootReward.BonusCash(Math.Max(25, rewardCash / 5)));
        }
        else if (difficulty == FightDifficultyTier.Hard)
        {
            AddChassisOrCash(rewards, style, hull, rewardCash / 3);
            rewards.Add(LootReward.BonusCash(Math.Max(50, rewardCash / 4)));
        }
        else if (difficulty == FightDifficultyTier.Wildcard || risk == FightRiskTier.Elite)
        {
            AddChassisOrCash(rewards, style, hull, rewardCash / 3);
            if (perkId.NotEmpty())
                rewards.Add(LootReward.Perk(perkId));
            else
                rewards.Add(LootReward.BonusCash(Math.Max(100, rewardCash / 2)));
            rewards.Add(LootReward.BonusCash(Math.Max(100, rewardCash / 3)));
        }

        if (specialContract)
        {
            rewards.Add(LootReward.BonusCash(Math.Max(75, rewardCash / 5)));
        }

        return rewards.ToArray();
    }

    static void AddChassisOrCash(List<LootReward> rewards, string style, string hull, int cashFallback)
    {
        if (style.NotEmpty() && hull.NotEmpty())
            rewards.Add(LootReward.Chassis(style, hull));
        else
            rewards.Add(LootReward.BonusCash(Math.Max(75, cashFallback)));
    }

    static string ContractNameFor(FightDifficultyTier difficulty, ulong seed)
    {
        string[] names =
        {
            "Sponsor Trial",
            "Crowd Favorite Purse",
            "Black-Ring Contract",
            "Wildcard Syndicate Drop",
        };
        string name = names[(int)(Mix(seed ^ 0xC047AC7ul) % (ulong)names.Length)];
        return $"{name}: {difficulty}";
    }

    public static int DifficultyRank(FightDifficultyTier difficulty) => difficulty switch
    {
        FightDifficultyTier.Trivial => 0,
        FightDifficultyTier.Easy    => 1,
        FightDifficultyTier.Normal  => 2,
        FightDifficultyTier.Hard    => 3,
        FightDifficultyTier.Wildcard => 4,
        _                      => 1,
    };

    static string PickModuleUid(IShipDesign design, ulong seed)
    {
        string[] uids = design?.UniqueModuleUIDs;
        if (uids == null || uids.Length == 0)
            return "";
        string[] sorted = uids.Where(u => u.NotEmpty()).Distinct(StringComparer.Ordinal).OrderBy(u => u, StringComparer.Ordinal).ToArray();
        if (sorted.Length == 0)
            return "";
        return sorted[(int)(Mix(seed) % (ulong)sorted.Length)];
    }

    static string PickPerk(ulong seed)
    {
        ArenaPerkDefinition[] catalog = ArenaPerks.Catalog;
        if (catalog == null || catalog.Length == 0)
            return "";
        return catalog[(int)(Mix(seed ^ 0x9e37_79b9_7f4a_7c15ul) % (ulong)catalog.Length)].Id;
    }

    static int PlayerFieldedCountForCareer(ArenaCareer career, int careerLevel)
    {
        int count = 0;
        foreach (OwnedVessel vessel in career?.FieldedFleetVessels() ?? Empty<OwnedVessel>.Array)
        {
            if (vessel == null || vessel.DesignName.IsEmpty())
                continue;
            if (!ResourceManager.Ships.GetDesign(vessel.DesignName, out IShipDesign design)
                || !ArenaFightScreen.IsLegalCombatCraft(design)
                || !ArenaFightScreen.IsDesignAllowedForCareerLevel(design, careerLevel))
                continue;
            ++count;
        }
        return Math.Max(1, count);
    }

    public static float PlayerStrengthForCareer(ArenaCareer career, int careerLevel)
    {
        float fielded = 0f;
        foreach (OwnedVessel vessel in career?.FieldedFleetVessels() ?? Empty<OwnedVessel>.Array)
        {
            if (vessel == null || vessel.DesignName.IsEmpty())
                continue;
            if (!ResourceManager.Ships.GetDesign(vessel.DesignName, out IShipDesign design)
                || !ArenaFightScreen.IsLegalCombatCraft(design)
                || !ArenaFightScreen.IsDesignAllowedForCareerLevel(design, careerLevel))
                continue;
            fielded += Math.Max(1f, design.BaseStrength);
        }
        if (fielded > 0f)
            return Math.Max(100f, fielded);

        IShipDesign player = ArenaFightScreen.AutoPickPlayerWarship(null, careerLevel);
        return Math.Max(100f, player?.BaseStrength ?? 100f);
    }

    static float EnemyStrength(IShipDesign escort, IShipDesign boss, int count, int bossCount,
        ArenaFightModifier modifier, ArenaBossEncounter encounter)
    {
        int escorts = Math.Max(0, count - bossCount);
        float strength = Math.Max(1f, escort?.BaseStrength ?? 1f) * escorts;
        float bossStrength = Math.Max(1f, boss?.BaseStrength ?? escort?.BaseStrength ?? 1f);
        if (encounter.Active)
            bossStrength *= encounter.StrengthMultiplier;
        strength += bossStrength * bossCount;
        strength *= Math.Max(0.1f, modifier.EnemyStrengthMultiplier);
        return Math.Max(1f, strength);
    }

    public static int RiskRank(FightRiskTier risk) => risk switch
    {
        FightRiskTier.Safe     => 0,
        FightRiskTier.Standard => 1,
        FightRiskTier.Risky    => 2,
        FightRiskTier.Elite    => 3,
        _                      => 0,
    };

    static ulong Mix(ulong x)
    {
        x ^= x >> 30;
        x *= 0xbf58476d1ce4e5b9UL;
        x ^= x >> 27;
        x *= 0x94d049bb133111ebUL;
        x ^= x >> 31;
        return x;
    }

    public static ArenaFightOptionsReport WriteReport(ArenaCareer career, ulong seed, string path)
    {
        FightOption[] options = GenerateFightOptions(career, seed);
        string json = ToJson(options);
        if (path.NotEmpty())
        {
            string dir = Path.GetDirectoryName(path);
            if (dir.NotEmpty())
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, json);
        }
        return new ArenaFightOptionsReport(options, json);
    }

    public static string ToJson(FightOption[] options)
    {
        options ??= Array.Empty<FightOption>();
        var lines = new List<string>
        {
            "{",
            "  \"experiment\": \"arena fight options risk reward loot\",",
            "  \"options\": [",
        };

        for (int i = 0; i < options.Length; ++i)
        {
            FightOption o = options[i];
            lines.Add("    {");
            lines.Add($"      \"type\": \"{Escape(o.FightType.ToString())}\",");
            lines.Add($"      \"risk\": \"{Escape(o.RiskTier.ToString())}\",");
            lines.Add($"      \"difficulty\": \"{Escape(o.DifficultyTier.ToString())}\",");
            lines.Add($"      \"contract\": {(o.IsSpecialContract ? "true" : "false")},");
            lines.Add($"      \"contractName\": \"{Escape(o.ContractName)}\",");
            lines.Add($"      \"id\": \"{Escape(o.OptionId)}\",");
            lines.Add($"      \"escort\": \"{Escape(o.EscortDesignName)}\",");
            lines.Add($"      \"boss\": \"{Escape(o.BossDesignName)}\",");
            lines.Add($"      \"enemyCount\": {o.EnemyCount.ToString(CultureInfo.InvariantCulture)},");
            lines.Add($"      \"bossCount\": {o.BossCount.ToString(CultureInfo.InvariantCulture)},");
            lines.Add($"      \"maxEnemyTier\": {o.MaxEnemyTier.ToString(CultureInfo.InvariantCulture)},");
            lines.Add($"      \"modifier\": \"{Escape(o.Modifier.Name)}\",");
            lines.Add($"      \"estimatedWinRate\": {o.EstimatedWinRate.ToString("0.000", CultureInfo.InvariantCulture)},");
            lines.Add($"      \"strengthRatio\": {o.StrengthRatio.ToString("0.000", CultureInfo.InvariantCulture)},");
            lines.Add($"      \"expectedRewardValue\": {o.ExpectedRewardValue.ToString(CultureInfo.InvariantCulture)},");
            lines.Add($"      \"cash\": {o.RewardCash.ToString(CultureInfo.InvariantCulture)},");
            lines.Add($"      \"fame\": {o.RewardFame.ToString(CultureInfo.InvariantCulture)},");
            lines.Add("      \"loot\": [");
            for (int j = 0; j < o.PreviewLoot.Length; ++j)
            {
                LootReward r = o.PreviewLoot[j];
                string comma = j == o.PreviewLoot.Length - 1 ? "" : ",";
                lines.Add($"        \"{Escape(r.Signature)}\"{comma}");
            }
            lines.Add("      ]");
            lines.Add(i == options.Length - 1 ? "    }" : "    },");
        }

        lines.Add("  ]");
        lines.Add("}");
        return string.Join(Environment.NewLine, lines);
    }

    static string Escape(string text) => (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
}
