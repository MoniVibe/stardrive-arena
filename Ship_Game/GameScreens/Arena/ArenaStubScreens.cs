using System;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using SDUtils;
using Ship_Game.Audio;
using Vector2 = SDGraphics.Vector2;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaBettingScreen : GameScreen
{
    readonly ArenaFightScreen Arena;
    UILabel StatusLabel;
    string StatusText = "";

    public ArenaBettingScreen(ArenaFightScreen arena) : base(arena, toPause: arena)
    {
        Arena = arena;
        IsPopup = true;
        CanEscapeFromScreen = true;
        TransitionOnTime  = 0.2f;
        TransitionOffTime = 0.2f;
    }

    public override void LoadContent()
    {
        RemoveAll();
        Vector2 c = ScreenCenter;
        var panel = new RectF(c.X - 340, c.Y - 250, 680, 500);
        Add(ArenaTheme.Panel(panel));
        Add(ArenaTheme.ScreenTitle(new Vector2(panel.X + 24, panel.Y + 20), "BETTING BOARD"));
        Add(ArenaTheme.Body(new Vector2(panel.X + 24, panel.Y + 58),
            $"Fixed stake: ${ArenaBetting.DefaultStake}. Favorites pay less; underdogs pay more."));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 24, panel.Y + 100, 150, 54),
            "CASH", $"${Arena.CurrentCash}", ArenaTheme.Amber));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 188, panel.Y + 100, 150, 54),
            "CONTRACTS", Arena.HasQueuedFightOption ? "QUEUED" : "OPEN", ArenaTheme.Cyan));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 352, panel.Y + 100, 150, 54),
            "OPEN SLIP", Arena.HasPendingBet ? "YES" : "NO", Arena.HasPendingBet ? ArenaTheme.Green : ArenaTheme.TextMuted));

        ArenaSettledBet latest = Arena.LatestSettledBet;
        string status = StatusText.NotEmpty() ? StatusText : PayoffText(latest);
        Color statusColor = StatusText.NotEmpty()
            ? ArenaTheme.TextMuted
            : latest?.Won == true ? ArenaTheme.Green : ArenaTheme.TextMuted;
        StatusLabel = Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 166),
            status, ArenaTheme.BodySmallFont, statusColor));

        float footerTop = panel.Bottom - 72f;
        var list = Add(new ScrollList<ArenaPopupListItem>(
            new RectF(panel.X + 24, panel.Y + 196, panel.W - 48, footerTop - panel.Y - 208), 34));
        list.ItemPadding = new Vector2(0f, 6f);
        list.EnableItemHighlight = true;
        list.OnClick = item => item.Activate();

        AddHistoryRows(list);

        if (Arena.PendingBet != null)
        {
            ArenaBetSlip slip = Arena.PendingBet;
            string matchup = ArenaBetQuote.FormatMatchup(slip.PickName, slip.PickDesignName,
                slip.OpponentName, slip.OpponentDesignName);
            string label = $"OPEN: {matchup}  ${slip.Stake} -> ${slip.Payout} ({slip.Odds:0.00}x)";
            list.AddItem(new ArenaPopupListItem(label, font: ArenaTheme.BodyFont,
                textColor: ArenaTheme.Green, tooltip: slip.Description));
        }
        else
        {
            AddQuoteRows(list);
        }

        UIList footer = AddList(new Vector2(c.X - 90, footerTop + 14), new Vector2(180, 40));
        footer.Padding = new Vector2(2f, 12f);
        footer.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(footer, "BACK", Back_OnClick);
    }

    void AddQuoteRows(ScrollList<ArenaPopupListItem> list)
    {
        list.AddItem(new ArenaPopupListItem("NEXT FIGHT", font: ArenaTheme.LabelFont,
            textColor: ArenaTheme.Amber));
        ArenaBetQuote[] fightQuotes = Arena.CurrentFightOptionBetQuotes(ArenaBetting.DefaultStake);
        if (fightQuotes.Length == 0)
        {
            list.AddItem(new ArenaPopupListItem("No fight-option slips available.",
                font: ArenaTheme.BodyFont, textColor: ArenaTheme.TextMuted));
        }
        else
        {
            foreach (ArenaBetQuote quote in fightQuotes)
                AddQuoteRow(list, quote, () => PlaceFightBet(quote));
        }

        list.AddItem(new ArenaPopupListItem("CONTENDER DUEL", font: ArenaTheme.LabelFont,
            textColor: ArenaTheme.Amber));
        ArenaBetQuote[] contenderQuotes = Arena.CurrentContenderBetQuotes(ArenaBetting.DefaultStake);
        if (contenderQuotes.Length == 0)
        {
            list.AddItem(new ArenaPopupListItem("No contender duel slips available.",
                font: ArenaTheme.BodyFont, textColor: ArenaTheme.TextMuted));
        }
        else
        {
            foreach (ArenaBetQuote quote in contenderQuotes)
                AddQuoteRow(list, quote, () => PlaceContenderBet(quote));
        }
    }

    void AddHistoryRows(ScrollList<ArenaPopupListItem> list)
    {
        list.AddItem(new ArenaPopupListItem("SETTLED HISTORY", font: ArenaTheme.LabelFont,
            textColor: ArenaTheme.Amber));
        ArenaSettledBet[] history = Arena.SettledBets
            .OrderByDescending(b => b?.Order ?? 0)
            .ToArray();
        if (history.Length == 0)
        {
            list.AddItem(new ArenaPopupListItem("No settled bets yet.",
                font: ArenaTheme.BodyFont, textColor: ArenaTheme.TextMuted));
            return;
        }

        foreach (ArenaSettledBet bet in history)
        {
            string outcome = bet.Won ? $"WON ${bet.Payout}" : "LOST";
            string label = $"{outcome}  ${bet.Stake} @ {bet.Odds:0.00}x  {bet.Matchup}";
            string tooltip = $"{bet.Description}; winner {bet.WinnerName}; cash ${bet.CashBefore} -> ${bet.CashAfter}; id {bet.BetId}";
            list.AddItem(new ArenaPopupListItem(label, font: ArenaTheme.BodySmallFont,
                textColor: bet.Won ? ArenaTheme.Green : ArenaTheme.Red, tooltip: tooltip,
                payload: bet));
        }
    }

    static string PayoffText(ArenaSettledBet bet)
    {
        if (bet == null)
            return "";
        string outcome = bet.Won ? $"WON ${bet.Payout}" : "LOST";
        return $"Last bet {outcome}: stake ${bet.Stake} at {bet.Odds:0.00}x.";
    }

    static void AddQuoteRow(ScrollList<ArenaPopupListItem> list, ArenaBetQuote quote, System.Action click)
    {
        string label = $"{quote.MatchupLabel}  ${quote.Stake} -> ${quote.Payout}  {quote.Odds:0.00}x";
        string tooltip = $"{quote.Description}; estimated win: {quote.WinProbability:P0}; " +
                         $"strength ratio {quote.StrengthRatio:0.00}; id {quote.BetId}";
        list.AddItem(new ArenaPopupListItem(label, click, tooltip: tooltip));
    }

    void PlaceFightBet(ArenaBetQuote quote)
    {
        ArenaBetResult result = Arena.PlaceFightOptionBet(quote.OptionId, quote.Stake);
        if (result.Success) GameAudio.AcceptClick(); else GameAudio.NegativeClick();
        StatusText = result.Message;
        LoadContent();
    }

    void PlaceContenderBet(ArenaBetQuote quote)
    {
        ArenaBetResult result = Arena.PlaceContenderBet(quote.PickName, quote.Stake);
        if (result.Success) GameAudio.AcceptClick(); else GameAudio.NegativeClick();
        StatusText = result.Message;
        LoadContent();
    }

    void Back_OnClick(UIButton button)
    {
        GameAudio.AffirmativeClick();
        ExitScreen();
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        ScreenManager.FadeBackBufferToBlack(TransitionAlpha * 2 / 3);
        batch.SafeBegin();
        base.Draw(batch, elapsed);
        batch.SafeEnd();
    }
}

public sealed class ArenaPilotSoulScreen : GameScreen
{
    readonly ArenaFightScreen Arena;

    public ArenaPilotSoulScreen(ArenaFightScreen arena) : base(arena, toPause: arena)
    {
        Arena = arena;
        IsPopup = true;
        CanEscapeFromScreen = true;
        TransitionOnTime  = 0.2f;
        TransitionOffTime = 0.2f;
    }

    public override void LoadContent()
    {
        RemoveAll();
        Vector2 c = ScreenCenter;
        var panel = new RectF(c.X - 340, c.Y - 250, 680, 500);
        Add(ArenaTheme.Panel(panel));
        Add(ArenaTheme.ScreenTitle(new Vector2(panel.X + 24, panel.Y + 18), "PILOT DOSSIER"));

        int wins = CareerWins();
        int losses = CareerLosses();
        Add(ArenaTheme.StatChip(new RectF(panel.X + 24, panel.Y + 60, 150, 54),
            "CAREER LEVEL", Arena.CurrentCareerLevel.ToString(), ArenaTheme.Cyan));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 188, panel.Y + 60, 150, 54),
            "BATTLES", (wins + losses).ToString(), ArenaTheme.Amber));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 352, panel.Y + 60, 150, 54),
            "W / L", $"{wins}/{losses}", ArenaTheme.Green));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 516, panel.Y + 60, 118, 54),
            "FLEET", $"{Arena.FleetSize}/{Arena.CurrentMaxFleetSize}", ArenaTheme.Magenta));

        float footerTop = panel.Bottom - 72f;
        var list = Add(new ScrollList<ArenaPopupListItem>(
            new RectF(panel.X + 24, panel.Y + 136, panel.W - 48, footerTop - panel.Y - 148), 34));
        list.ItemPadding = new Vector2(0f, 6f);
        list.EnableItemHighlight = true;

        AddCaptainRows(list);
        AddFleetRows(list);
        AddPerkRows(list);

        UIList footer = AddList(new Vector2(c.X - 90, footerTop + 14), new Vector2(180, 40));
        footer.Padding = new Vector2(2f, 12f);
        footer.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(footer, "BACK", Back_OnClick);
    }

    int CareerWins()
    {
        int wins = Arena.Chronicle.Count(e => string.Equals(e?.Kind, "win", StringComparison.Ordinal));
        return Math.Max(wins, Arena.CurrentCareerLevel);
    }

    int CareerLosses()
        => Arena.Chronicle.Count(e => string.Equals(e?.Kind, "loss", StringComparison.Ordinal));

    void AddCaptainRows(ScrollList<ArenaPopupListItem> list)
    {
        list.AddItem(new ArenaPopupListItem("CAPTAINS", font: ArenaTheme.LabelFont,
            textColor: ArenaTheme.Amber));
        ArenaCaptain[] captains = Arena.Captains
            .OrderByDescending(c => c?.Alive == true)
            .ThenByDescending(c => c?.Level ?? 0)
            .ThenBy(c => c?.Name, StringComparer.Ordinal)
            .ToArray();
        if (captains.Length == 0)
        {
            list.AddItem(new ArenaPopupListItem("No captain records.",
                font: ArenaTheme.BodyFont, textColor: ArenaTheme.TextMuted));
            return;
        }

        foreach (ArenaCaptain captain in captains)
        {
            string state = captain.Alive ? "ACTIVE" : "KIA";
            string label = $"{state}  {captain.Name}  L{captain.Level}  {captain.Kills} kills  {captain.Epithet}";
            string tooltip = captain.Alive
                ? $"Survived hull losses: {captain.SurvivedHullLosses}."
                : $"Killed by {captain.Killer}: {captain.DeathCause}.";
            list.AddItem(new ArenaPopupListItem(label, font: ArenaTheme.BodySmallFont,
                textColor: captain.Alive ? ArenaTheme.TextPrimary : ArenaTheme.TextMuted,
                tooltip: tooltip, payload: captain));
        }
    }

    void AddFleetRows(ScrollList<ArenaPopupListItem> list)
    {
        list.AddItem(new ArenaPopupListItem("CURRENT FLEET", font: ArenaTheme.LabelFont,
            textColor: ArenaTheme.Amber));
        OwnedVessel[] fleet = Arena.FieldedVessels;
        if (fleet.Length == 0)
        {
            list.AddItem(new ArenaPopupListItem("No fielded vessels.",
                font: ArenaTheme.BodyFont, textColor: ArenaTheme.TextMuted));
            return;
        }

        foreach (OwnedVessel vessel in fleet)
        {
            string name = vessel.Name.NotEmpty() ? vessel.Name : vessel.DesignName;
            string label = $"{name} ({vessel.DesignName})  L{vessel.Level}  {vessel.Kills} kills";
            string tooltip = $"Vessel id {vessel.VesselId}; captain id {vessel.CaptainId}.";
            list.AddItem(new ArenaPopupListItem(label, font: ArenaTheme.BodySmallFont,
                tooltip: tooltip, payload: vessel));
        }
    }

    void AddPerkRows(ScrollList<ArenaPopupListItem> list)
    {
        list.AddItem(new ArenaPopupListItem("PERKS", font: ArenaTheme.LabelFont,
            textColor: ArenaTheme.Amber));
        string[] perks = Arena.CurrentPerks ?? Array.Empty<string>();
        if (perks.Length == 0)
        {
            list.AddItem(new ArenaPopupListItem("No perks held.",
                font: ArenaTheme.BodyFont, textColor: ArenaTheme.TextMuted));
            return;
        }

        foreach (string perkId in perks)
        {
            string label = ArenaPerks.TryGet(perkId, out ArenaPerkDefinition perk)
                ? $"{perk.Name}  ({perk.Description})"
                : perkId;
            list.AddItem(new ArenaPopupListItem(label, font: ArenaTheme.BodySmallFont,
                textColor: ArenaTheme.Green));
        }
    }

    void Back_OnClick(UIButton button)
    {
        GameAudio.AffirmativeClick();
        ExitScreen();
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        ScreenManager.FadeBackBufferToBlack(TransitionAlpha * 2 / 3);
        batch.SafeBegin();
        base.Draw(batch, elapsed);
        batch.SafeEnd();
    }
}

public sealed class ArenaMemorialScreen : GameScreen
{
    readonly ArenaFightScreen Arena;

    public ArenaMemorialScreen(ArenaFightScreen arena) : base(arena, toPause: arena)
    {
        Arena = arena;
        IsPopup = true;
        CanEscapeFromScreen = true;
        TransitionOnTime  = 0.2f;
        TransitionOffTime = 0.2f;
    }

    public override void LoadContent()
    {
        RemoveAll();
        Vector2 c = ScreenCenter;
        var panel = new RectF(c.X - 340, c.Y - 250, 680, 500);
        Add(ArenaTheme.Panel(panel));
        Add(ArenaTheme.ScreenTitle(new Vector2(panel.X + 24, panel.Y + 18), "MEMORIAL WALL"));
        ArenaMemorialRecord[] memorials = Arena.Memorials
            .OrderByDescending(m => m?.Order ?? 0)
            .ThenBy(m => m?.Name, StringComparer.Ordinal)
            .ToArray();
        Add(ArenaTheme.StatChip(new RectF(panel.X + 24, panel.Y + 60, 150, 54),
            "MEMORIALS", memorials.Length.ToString(), ArenaTheme.Amber));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 188, panel.Y + 60, 150, 54),
            "PERMADEATH", Arena.CurrentPermadeathChance.ToString("P0"), ArenaTheme.Red));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 352, panel.Y + 60, 150, 54),
            "TEAMS", Arena.CurrentTeamCount.ToString(), ArenaTheme.Magenta));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 516, panel.Y + 60, 118, 54),
            "CAPTAINS", Arena.Captains.Length.ToString(), ArenaTheme.Cyan));

        float footerTop = panel.Bottom - 72f;
        var list = Add(new ScrollList<ArenaPopupListItem>(
            new RectF(panel.X + 24, panel.Y + 136, panel.W - 48, footerTop - panel.Y - 148), 34));
        list.ItemPadding = new Vector2(0f, 6f);
        list.EnableItemHighlight = true;

        if (memorials.Length == 0)
        {
            list.AddItem(new ArenaPopupListItem("The memorial wall is empty.",
                font: ArenaTheme.BodyFont, textColor: ArenaTheme.TextMuted));
        }
        else
        {
            foreach (ArenaMemorialRecord memorial in memorials)
                AddMemorialRow(list, memorial);
        }

        UIList footer = AddList(new Vector2(c.X - 90, footerTop + 14), new Vector2(180, 40));
        footer.Padding = new Vector2(2f, 12f);
        footer.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(footer, "BACK", Back_OnClick);
    }

    static void AddMemorialRow(ScrollList<ArenaPopupListItem> list, ArenaMemorialRecord memorial)
    {
        string name = memorial.Name.NotEmpty() ? memorial.Name : memorial.DesignName;
        string design = memorial.DesignName.NotEmpty() ? $" ({memorial.DesignName})" : "";
        string when = WhenText(memorial);
        string label = $"{memorial.Kind.ToUpperInvariant()}  {name}{design}  L{memorial.Level}  {memorial.Kills} kills  {when}";
        string tooltip = $"Killed by {memorial.Killer}; {memorial.Cause}; seed {memorial.FightSeed}.";
        list.AddItem(new ArenaPopupListItem(label, font: ArenaTheme.BodySmallFont,
            textColor: memorial.Kind == "Captain" ? ArenaTheme.Cyan : ArenaTheme.TextPrimary,
            tooltip: tooltip, payload: memorial));
    }

    static string WhenText(ArenaMemorialRecord memorial)
    {
        if (memorial.RoundAtDeath > 0 || memorial.FameAtDeath > 0)
            return $"Round {memorial.RoundAtDeath}, Fame {memorial.FameAtDeath}";
        return $"Record {memorial.Order}";
    }

    void Back_OnClick(UIButton button)
    {
        GameAudio.AffirmativeClick();
        ExitScreen();
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        ScreenManager.FadeBackBufferToBlack(TransitionAlpha * 2 / 3);
        batch.SafeBegin();
        base.Draw(batch, elapsed);
        batch.SafeEnd();
    }
}

public sealed class ArenaLeagueSeasonScreen : GameScreen
{
    readonly ArenaFightScreen Arena;
    ArenaBigLeagueReport Report;
    string StatusText = "";

    public ArenaLeagueSeasonScreen(ArenaFightScreen arena) : base(arena, toPause: arena)
    {
        Arena = arena;
        IsPopup = true;
        CanEscapeFromScreen = true;
        TransitionOnTime  = 0.2f;
        TransitionOffTime = 0.2f;
    }

    public override void LoadContent()
    {
        RemoveAll();
        Report ??= RunSeason();

        Vector2 c = ScreenCenter;
        var panel = new RectF(c.X - 360, c.Y - 270, 720, 540);
        Add(ArenaTheme.Panel(panel));
        Add(ArenaTheme.ScreenTitle(new Vector2(panel.X + 24, panel.Y + 18),
            "LEAGUE SEASON"));

        Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 50),
            StatusText, ArenaTheme.BodySmallFont, ArenaTheme.TextSecondary));

        Add(ArenaTheme.StatChip(new RectF(panel.X + 24, panel.Y + 82, 150, 54),
            "BRACKET", Report != null ? $"{Report.TeamSize}v{Report.TeamSize}" : "-", ArenaTheme.Cyan));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 188, panel.Y + 82, 150, 54),
            "TEAMS", Report != null ? Report.TeamCount.ToString() : "0", ArenaTheme.Amber));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 352, panel.Y + 82, 150, 54),
            "MATCHES", Report != null ? Report.Matches.Length.ToString() : "0", ArenaTheme.Green));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 516, panel.Y + 82, 150, 54),
            "PERMADEATH", Arena.CurrentPermadeathChance.ToString("P0"), ArenaTheme.Red));

        float footerTop = panel.Bottom - 72f;
        var list = Add(new ScrollList<ArenaPopupListItem>(
            new RectF(panel.X + 24, panel.Y + 158, panel.W - 48, footerTop - panel.Y - 170), 34));
        list.ItemPadding = new Vector2(0f, 6f);
        list.EnableItemHighlight = true;

        AddStandingsRows(list);
        AddRecentMatchRows(list);
        AddSeasonNotes(list);

        UIList footer = AddList(new Vector2(c.X - 170, footerTop + 14), new Vector2(340, 40));
        footer.Direction = new Vector2(1f, 0f);
        footer.Padding = new Vector2(8f, 2f);
        footer.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(footer, "RUN AGAIN", RunAgain_OnClick, 116f);
        ArenaTheme.AddPillButton(footer, "LADDER", Ladder_OnClick, 92f);
        ArenaTheme.AddPillButton(footer, "BACK", Back_OnClick, 82f);
    }

    ArenaBigLeagueReport RunSeason()
    {
        try
        {
            ArenaBigLeagueReport report = Arena?.RunLeagueSeasonAndApplyForUi();
            StatusText = report?.Verdict ?? "No league report.";
            return report;
        }
        catch (Exception e)
        {
            StatusText = $"League season unavailable: {e.Message}";
            return null;
        }
    }

    void AddStandingsRows(ScrollList<ArenaPopupListItem> list)
    {
        list.AddItem(new ArenaPopupListItem("STANDINGS", font: ArenaTheme.LabelFont,
            textColor: ArenaTheme.Amber));
        ArenaBigLeagueStanding[] standings = Report?.Standings ?? Array.Empty<ArenaBigLeagueStanding>();
        if (standings.Length == 0)
        {
            list.AddItem(new ArenaPopupListItem("No standings available.",
                font: ArenaTheme.BodyFont, textColor: ArenaTheme.TextMuted));
            return;
        }

        for (int i = 0; i < standings.Length; ++i)
        {
            ArenaBigLeagueStanding standing = standings[i];
            string label = $"{i + 1,2}. {standing.TeamName}  {standing.Wins}-{standing.Losses}  {standing.Games} games";
            list.AddItem(new ArenaPopupListItem(label, font: ArenaTheme.BodySmallFont,
                textColor: i == 0 ? ArenaTheme.Green : ArenaTheme.TextPrimary));
        }
    }

    void AddRecentMatchRows(ScrollList<ArenaPopupListItem> list)
    {
        list.AddItem(new ArenaPopupListItem("RECENT RESULTS", font: ArenaTheme.LabelFont,
            textColor: ArenaTheme.Amber));
        ArenaBigLeagueMatchResult[] matches = (Report?.Matches ?? Array.Empty<ArenaBigLeagueMatchResult>())
            .OrderByDescending(m => m.MatchIndex)
            .Take(5)
            .ToArray();
        if (matches.Length == 0)
        {
            list.AddItem(new ArenaPopupListItem("No match results.",
                font: ArenaTheme.BodyFont, textColor: ArenaTheme.TextMuted));
            return;
        }

        foreach (ArenaBigLeagueMatchResult match in matches)
        {
            string label = $"{match.TeamA} {match.WinsA}-{match.WinsB} {match.TeamB}  winner {match.WinnerTeamName}";
            string tooltip = $"Survivors {match.SurvivorsA}/{match.SurvivorsB}; ticks {match.TicksSimulated}.";
            list.AddItem(new ArenaPopupListItem(label, font: ArenaTheme.BodySmallFont,
                tooltip: tooltip, payload: match));
        }
    }

    void AddSeasonNotes(ScrollList<ArenaPopupListItem> list)
    {
        list.AddItem(new ArenaPopupListItem("SEASON NOTES", font: ArenaTheme.LabelFont,
            textColor: ArenaTheme.Amber));
        string next = NextMatchupText();
        list.AddItem(new ArenaPopupListItem($"NEXT MATCHUP  {next}", font: ArenaTheme.BodySmallFont,
            textColor: ArenaTheme.Cyan));
        string permadeath = Arena.CurrentPermadeathChance > 0f
            ? $"PERMADEATH  {Arena.CurrentPermadeathChance.ToString("P0", CultureInfo.InvariantCulture)} chance is armed for team duels."
            : "PERMADEATH  No contender deaths recorded for this season.";
        list.AddItem(new ArenaPopupListItem(permadeath, font: ArenaTheme.BodySmallFont,
            textColor: Arena.CurrentPermadeathChance > 0f ? ArenaTheme.Red : ArenaTheme.TextMuted));
    }

    string NextMatchupText()
    {
        ArenaBigLeagueStanding[] standings = Report?.Standings ?? Array.Empty<ArenaBigLeagueStanding>();
        if (standings.Length >= 2)
            return $"{standings[0].TeamName} vs {standings[1].TeamName}";
        ArenaTeam[] teams = Arena.Teams
            .Where(t => t != null)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Take(2)
            .ToArray();
        return teams.Length >= 2 ? $"{teams[0].Name} vs {teams[1].Name}" : "Awaiting teams";
    }

    void RunAgain_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Report = null;
        LoadContent();
    }

    void Ladder_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenLeaderboard();
    }

    void Back_OnClick(UIButton button)
    {
        GameAudio.AffirmativeClick();
        ExitScreen();
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        ScreenManager.FadeBackBufferToBlack(TransitionAlpha * 2 / 3);
        batch.SafeBegin();
        base.Draw(batch, elapsed);
        batch.SafeEnd();
    }
}
