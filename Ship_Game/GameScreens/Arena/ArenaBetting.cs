using System;
using System.Collections.Generic;
using System.Linq;
using SDUtils;
using Ship_Game.Data.Serialization;
using Ship_Game.Ships;

namespace Ship_Game.GameScreens.Arena;

[StarDataType]
public sealed class ArenaBetSlip
{
    [StarData] public string Kind = "";
    [StarData] public string BetId = "";
    [StarData] public string OptionId = "";
    [StarData] public string PickName = "";
    [StarData] public string OpponentName = "";
    [StarData] public string PickDesignName = "";
    [StarData] public string OpponentDesignName = "";
    [StarData] public int Stake;
    [StarData] public float Odds;
    [StarData] public int Payout;
    [StarData] public float StrengthRatio;
    [StarData] public string Description = "";

    [StarDataConstructor] public ArenaBetSlip() { }
}

public sealed class ArenaBetQuote
{
    public readonly string Kind;
    public readonly string BetId;
    public readonly string OptionId;
    public readonly string PickName;
    public readonly string OpponentName;
    public readonly string PickDesignName;
    public readonly string OpponentDesignName;
    public readonly int Stake;
    public readonly float Odds;
    public readonly int Payout;
    public readonly float WinProbability;
    public readonly float StrengthRatio;
    public readonly string Description;
    public string MatchupLabel => FormatMatchup(PickName, PickDesignName, OpponentName, OpponentDesignName);

    public ArenaBetQuote(string kind, string betId, string optionId, string pickName,
        string opponentName, string pickDesignName, string opponentDesignName, int stake,
        float odds, float winProbability, float strengthRatio, string description)
    {
        Kind = kind ?? "";
        BetId = betId ?? "";
        OptionId = optionId ?? "";
        PickName = pickName ?? "";
        OpponentName = opponentName ?? "";
        PickDesignName = pickDesignName ?? "";
        OpponentDesignName = opponentDesignName ?? "";
        Stake = Math.Max(0, stake);
        Odds = Math.Max(1f, odds);
        Payout = ArenaBetting.PayoutForStake(Stake, Odds);
        WinProbability = Math.Clamp(winProbability, 0.01f, 0.99f);
        StrengthRatio = Math.Max(0.01f, strengthRatio);
        Description = description ?? "";
    }

    public ArenaBetSlip ToSlip() => new()
    {
        Kind = Kind,
        BetId = BetId,
        OptionId = OptionId,
        PickName = PickName,
        OpponentName = OpponentName,
        PickDesignName = PickDesignName,
        OpponentDesignName = OpponentDesignName,
        Stake = Stake,
        Odds = Odds,
        Payout = Payout,
        StrengthRatio = StrengthRatio,
        Description = Description,
    };

    public string Signature
        => $"{Kind}|{BetId}|{OptionId}|{PickName}|{OpponentName}|{PickDesignName}|{OpponentDesignName}|{Stake}|{Odds:0.000}|"
         + $"{Payout}|{WinProbability:0.000}|{StrengthRatio:0.000}";

    public static string FormatMatchup(string pickName, string pickDesignName,
        string opponentName, string opponentDesignName)
    {
        string leftName = pickName.NotEmpty() ? pickName : "PLAYER";
        string rightName = opponentName.NotEmpty() ? opponentName : "OPPONENT";
        string leftDesign = pickDesignName.NotEmpty() ? $" ({pickDesignName})" : "";
        string rightDesign = opponentDesignName.NotEmpty() ? $" ({opponentDesignName})" : "";
        return $"{leftName}{leftDesign} vs {rightName}{rightDesign}";
    }
}

public readonly struct ArenaBetResult
{
    public readonly bool Success;
    public readonly bool Won;
    public readonly string Message;
    public readonly int CashBefore;
    public readonly int CashAfter;
    public readonly int Stake;
    public readonly int Payout;
    public readonly float Odds;
    public readonly string BetId;
    public readonly string WinnerName;

    public ArenaBetResult(bool success, bool won, string message, int cashBefore,
        int cashAfter, int stake = 0, int payout = 0, float odds = 0f,
        string betId = "", string winnerName = "")
    {
        Success = success;
        Won = won;
        Message = message ?? "";
        CashBefore = cashBefore;
        CashAfter = cashAfter;
        Stake = Math.Max(0, stake);
        Payout = Math.Max(0, payout);
        Odds = Math.Max(0f, odds);
        BetId = betId ?? "";
        WinnerName = winnerName ?? "";
    }
}

public static class ArenaBetting
{
    public const string FightOptionKind = "fight-option";
    public const string ContenderDuelKind = "contender-duel";
    public const int DefaultStake = 100;

    public static ArenaBetSlip NormalizePendingBet(ArenaBetSlip slip)
    {
        if (slip == null || slip.Kind != FightOptionKind || slip.BetId.IsEmpty()
            || slip.OptionId.IsEmpty() || slip.Stake <= 0 || slip.Payout <= 0
            || slip.Odds < 1f)
        {
            return null;
        }

        slip.PickName ??= "";
        slip.OpponentName ??= "";
        slip.PickDesignName ??= "";
        slip.OpponentDesignName ??= "";
        slip.Description ??= "";
        slip.StrengthRatio = Math.Max(0.01f, slip.StrengthRatio);
        return slip;
    }

    public static ArenaBetQuote QuoteFightOption(FightOption option, int stake = DefaultStake)
        => QuoteFightOption(option, null, stake);

    public static ArenaBetQuote QuoteFightOption(FightOption option, ArenaCareer career,
        int stake = DefaultStake)
    {
        OwnedVessel active = career?.ActiveVessel ?? career?.FirstVessel;
        string pickName = active?.Name.NotEmpty() == true ? active.Name : "PLAYER";
        string pickDesignName = active?.DesignName ?? "";
        return QuoteFightOption(option, stake, pickName, pickDesignName);
    }

    static ArenaBetQuote QuoteFightOption(FightOption option, int stake,
        string pickName, string pickDesignName)
    {
        if (option == null)
            return null;

        float probability = Math.Clamp(option.EstimatedWinRate, 0.01f, 0.99f);
        float odds = OddsForWinProbability(probability);
        string opponentDesign = option.RiskTier == FightRiskTier.Elite
            ? option.BossDesignName
            : option.EscortDesignName;
        string opponentName = option.ContractName.NotEmpty()
            ? option.ContractName
            : $"{option.DifficultyTier} {option.RiskTier}";
        string description = $"{option.DifficultyTier} {option.RiskTier} contract, " +
                             $"{probability:P0} estimated win chance";
        return new ArenaBetQuote(FightOptionKind, $"fight:{option.OptionId}", option.OptionId,
            pickName, opponentName, pickDesignName, opponentDesign, stake, odds, probability,
            option.StrengthRatio, description);
    }

    public static ArenaBetQuote[] ContenderDuelQuotes(ArenaCareer career, int stake, ulong seed)
    {
        if (!TryPickContenderPair(career, seed, out ContenderRecord a, out ContenderRecord b,
                out IShipDesign designA, out IShipDesign designB))
        {
            return Array.Empty<ArenaBetQuote>();
        }

        return new[]
        {
            QuoteContender(a, b, designA, designB, stake, seed),
            QuoteContender(b, a, designB, designA, stake, seed),
        };
    }

    public static ArenaBetResult PlaceFightOptionBet(ArenaCareer career, FightOption option,
        int stake = DefaultStake)
    {
        if (career == null)
            return Fail("No career.", 0);
        if (career.PendingBet != null)
            return Fail("A betting slip is already open.", career.Cash);
        if (option == null)
            return Fail("No fight option selected.", career.Cash);
        if (stake <= 0)
            return Fail("Stake must be positive.", career.Cash);
        if (career.Cash < stake)
            return Fail($"Need ${stake} to place this bet.", career.Cash);

        ArenaBetQuote quote = QuoteFightOption(option, career, stake);
        if (quote == null)
            return Fail("Could not price this fight option.", career.Cash);

        int before = career.Cash;
        career.Cash -= stake;
        career.PendingBet = quote.ToSlip();
        return new ArenaBetResult(true, false,
            $"Placed ${stake} on {quote.Description} at {quote.Odds:0.00}x.",
            before, career.Cash, stake, 0, quote.Odds, quote.BetId);
    }

    public static ArenaBetResult ResolvePendingFightOptionBet(ArenaCareer career, bool playerWon)
    {
        ArenaBetSlip slip = NormalizePendingBet(career?.PendingBet);
        if (career == null || slip == null)
            return Fail("No open fight-option bet.", career?.Cash ?? 0);

        int before = career.Cash;
        int payout = playerWon ? slip.Payout : 0;
        if (payout > 0)
            career.Cash += payout;
        career.PendingBet = null;
        string message = playerWon
            ? $"Bet won: paid ${payout} at {slip.Odds:0.00}x."
            : $"Bet lost: stake ${slip.Stake} is gone.";
        return new ArenaBetResult(true, playerWon, message, before, career.Cash,
            slip.Stake, payout, slip.Odds, slip.BetId, playerWon ? slip.PickName : slip.OpponentName);
    }

    public static ArenaBetResult PlaceContenderDuelBet(ArenaCareer career, string contenderName,
        int stake, ulong seed)
    {
        if (career == null)
            return Fail("No career.", 0);
        if (career.PendingBet != null)
            return Fail("Resolve the open fight-option bet first.", career.Cash);
        if (stake <= 0)
            return Fail("Stake must be positive.", career.Cash);
        if (career.Cash < stake)
            return Fail($"Need ${stake} to place this bet.", career.Cash);

        ArenaBetQuote quote = ContenderDuelQuotes(career, stake, seed)
            .FirstOrDefault(q => string.Equals(q.PickName, contenderName, StringComparison.Ordinal));
        if (quote == null)
            return Fail("No deterministic contender duel is available.", career.Cash);

        int before = career.Cash;
        career.Cash -= stake;
        DuelResult duel = CareerLadder.SimulateDuel(quote.PickDesignName,
            quote.OpponentDesignName, seed);
        bool won = string.Equals(duel.WinnerDesignName, quote.PickDesignName, StringComparison.Ordinal);
        int payout = won ? quote.Payout : 0;
        if (payout > 0)
            career.Cash += payout;
        string winner = won ? quote.PickName : quote.OpponentName;
        string message = won
            ? $"Bet won: {quote.PickName} paid ${payout}."
            : $"Bet lost: {quote.OpponentName} won.";
        return new ArenaBetResult(true, won, message, before, career.Cash,
            stake, payout, quote.Odds, quote.BetId, winner);
    }

    public static float OddsForWinProbability(float winProbability)
    {
        float p = Math.Clamp(winProbability, 0.01f, 0.99f);
        return Math.Clamp(1f / p, 1.05f, 8f);
    }

    public static int PayoutForStake(int stake, float odds)
        => Math.Max(0, (int)Math.Round(Math.Max(0, stake) * Math.Max(1f, odds),
            MidpointRounding.AwayFromZero));

    static ArenaBetQuote QuoteContender(ContenderRecord pick, ContenderRecord opponent,
        IShipDesign pickDesign, IShipDesign opponentDesign, int stake, ulong seed)
    {
        float pickStrength = Math.Max(1f, pickDesign?.BaseStrength ?? 1f);
        float opponentStrength = Math.Max(1f, opponentDesign?.BaseStrength ?? 1f);
        float probability = Math.Clamp(pickStrength / (pickStrength + opponentStrength), 0.01f, 0.99f);
        float odds = OddsForWinProbability(probability);
        float strengthRatio = opponentStrength / Math.Max(1f, pickStrength);
        string id = $"duel:{seed:x16}:{pick?.Name}:{opponent?.Name}";
        string description = $"{pick?.Name} over {opponent?.Name}";
        return new ArenaBetQuote(ContenderDuelKind, id, "", pick?.Name, opponent?.Name,
            pickDesign?.Name, opponentDesign?.Name, stake, odds, probability,
            strengthRatio, description);
    }

    static bool TryPickContenderPair(ArenaCareer career, ulong seed, out ContenderRecord a,
        out ContenderRecord b, out IShipDesign designA, out IShipDesign designB)
        => CareerLadder.TryPickComparableContenderPair(career, seed, out a, out b, out designA, out designB);

    static ArenaBetResult Fail(string message, int cash)
        => new(false, false, message, cash, cash);

}
