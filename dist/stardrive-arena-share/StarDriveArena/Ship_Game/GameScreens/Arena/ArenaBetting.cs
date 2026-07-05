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
    public readonly string Kind;
    public readonly string MatchupLabel;

    public ArenaBetResult(bool success, bool won, string message, int cashBefore,
        int cashAfter, int stake = 0, int payout = 0, float odds = 0f,
        string betId = "", string winnerName = "", string kind = "",
        string matchupLabel = "")
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
        Kind = kind ?? "";
        MatchupLabel = matchupLabel ?? "";
    }
}

[StarDataType]
public sealed class ArenaSettledBet
{
    [StarData] public string Kind = "";
    [StarData] public string BetId = "";
    [StarData] public int Order;
    [StarData] public string Matchup = "";
    [StarData] public string PickName = "";
    [StarData] public string OpponentName = "";
    [StarData] public string PickDesignName = "";
    [StarData] public string OpponentDesignName = "";
    [StarData] public string Description = "";
    [StarData] public bool Won;
    [StarData] public string WinnerName = "";
    [StarData] public int Stake;
    [StarData] public float Odds;
    [StarData] public int Payout;
    [StarData] public int CashBefore;
    [StarData] public int CashAfter;

    [StarDataConstructor] public ArenaSettledBet() { }

    public static ArenaSettledBet FromSlip(ArenaBetSlip slip, bool won, int cashBefore,
        int cashAfter, string winnerName)
    {
        if (slip == null)
            return null;

        return new ArenaSettledBet
        {
            Kind = slip.Kind,
            BetId = slip.BetId,
            Matchup = ArenaBetQuote.FormatMatchup(slip.PickName, slip.PickDesignName,
                slip.OpponentName, slip.OpponentDesignName),
            PickName = slip.PickName,
            OpponentName = slip.OpponentName,
            PickDesignName = slip.PickDesignName,
            OpponentDesignName = slip.OpponentDesignName,
            Description = slip.Description,
            Won = won,
            WinnerName = winnerName ?? "",
            Stake = slip.Stake,
            Odds = slip.Odds,
            Payout = won ? slip.Payout : 0,
            CashBefore = cashBefore,
            CashAfter = cashAfter,
        };
    }

    public static ArenaSettledBet FromQuote(ArenaBetQuote quote, bool won, int cashBefore,
        int cashAfter, string winnerName)
    {
        if (quote == null)
            return null;

        return new ArenaSettledBet
        {
            Kind = quote.Kind,
            BetId = quote.BetId,
            Matchup = quote.MatchupLabel,
            PickName = quote.PickName,
            OpponentName = quote.OpponentName,
            PickDesignName = quote.PickDesignName,
            OpponentDesignName = quote.OpponentDesignName,
            Description = quote.Description,
            Won = won,
            WinnerName = winnerName ?? "",
            Stake = quote.Stake,
            Odds = quote.Odds,
            Payout = won ? quote.Payout : 0,
            CashBefore = cashBefore,
            CashAfter = cashAfter,
        };
    }
}

public static class ArenaBetting
{
    public const string FightOptionKind = "fight-option";
    public const string ContenderDuelKind = "contender-duel";
    public const int DefaultStake = 100;
    public const int SettledBetHistoryLimit = 5;

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

    public static ArenaSettledBet[] NormalizeSettledBets(ArenaSettledBet[] bets)
    {
        if (bets == null || bets.Length == 0)
            return Empty<ArenaSettledBet>.Array;

        var result = new List<ArenaSettledBet>(bets.Length);
        int nextOrder = 1;
        foreach (ArenaSettledBet bet in bets)
        {
            if (bet == null || bet.Stake <= 0)
                continue;

            bet.Kind = CleanBetText(bet.Kind, 32);
            if (bet.Kind.IsEmpty())
                bet.Kind = FightOptionKind;
            bet.BetId = CleanBetText(bet.BetId, 120);
            bet.PickName = CleanBetText(bet.PickName, 80);
            bet.OpponentName = CleanBetText(bet.OpponentName, 80);
            bet.PickDesignName = CleanBetText(bet.PickDesignName, 80);
            bet.OpponentDesignName = CleanBetText(bet.OpponentDesignName, 80);
            bet.Description = CleanBetText(bet.Description, 160);
            bet.WinnerName = CleanBetText(bet.WinnerName, 80);
            bet.Matchup = CleanBetText(bet.Matchup, 180);
            if (bet.Matchup.IsEmpty())
                bet.Matchup = ArenaBetQuote.FormatMatchup(bet.PickName, bet.PickDesignName,
                    bet.OpponentName, bet.OpponentDesignName);
            if (bet.Order <= 0)
                bet.Order = nextOrder;
            nextOrder = Math.Max(nextOrder, bet.Order + 1);
            bet.Stake = Math.Max(0, bet.Stake);
            bet.Odds = Math.Max(1f, bet.Odds);
            bet.Payout = bet.Won ? Math.Max(0, bet.Payout) : 0;
            result.Add(bet);
        }

        return result
            .OrderByDescending(b => b.Order)
            .ThenByDescending(b => b.BetId, StringComparer.Ordinal)
            .Take(SettledBetHistoryLimit)
            .OrderBy(b => b.Order)
            .ThenBy(b => b.BetId, StringComparer.Ordinal)
            .ToArray();
    }

    public static void AppendSettledBet(ArenaCareer career, ArenaSettledBet bet)
    {
        if (career == null || bet == null || bet.Stake <= 0)
            return;

        ArenaSettledBet[] history = NormalizeSettledBets(career.SettledBets);
        bet.Order = history.Length == 0 ? 1 : history.Max(b => b?.Order ?? 0) + 1;
        var list = history.ToList();
        list.Add(bet);
        career.SettledBets = NormalizeSettledBets(list.ToArray());
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
        string winner = playerWon ? slip.PickName : slip.OpponentName;
        AppendSettledBet(career, ArenaSettledBet.FromSlip(slip, playerWon, before,
            career.Cash, winner));
        return new ArenaBetResult(true, playerWon, message, before, career.Cash,
            slip.Stake, payout, slip.Odds, slip.BetId, winner, slip.Kind,
            ArenaBetQuote.FormatMatchup(slip.PickName, slip.PickDesignName,
                slip.OpponentName, slip.OpponentDesignName));
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
        AppendSettledBet(career, ArenaSettledBet.FromQuote(quote, won, before,
            career.Cash, winner));
        return new ArenaBetResult(true, won, message, before, career.Cash,
            stake, payout, quote.Odds, quote.BetId, winner, quote.Kind,
            quote.MatchupLabel);
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

    static string CleanBetText(string text, int max)
    {
        if (text == null)
            return "";
        text = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= max ? text : text.Substring(0, max);
    }

}
