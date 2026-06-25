using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;
using Vector2 = SDGraphics.Vector2;

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

        StatusLabel = Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 166),
            StatusText, ArenaTheme.BodySmallFont, ArenaTheme.TextMuted));

        float footerTop = panel.Bottom - 72f;
        var list = Add(new ScrollList<ArenaPopupListItem>(
            new RectF(panel.X + 24, panel.Y + 196, panel.W - 48, footerTop - panel.Y - 208), 34));
        list.ItemPadding = new Vector2(0f, 6f);
        list.EnableItemHighlight = true;
        list.OnClick = item => item.Activate();

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
        var panel = new RectF(c.X - 280, c.Y - 180, 560, 360);
        Add(ArenaTheme.Panel(panel));
        Add(ArenaTheme.ScreenTitle(new Vector2(panel.X + 24, panel.Y + 20), "PILOT SOUL"));
        Add(ArenaTheme.Body(new Vector2(panel.X + 24, panel.Y + 58),
            "No pilot dossier is bound to this career."));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 24, panel.Y + 104, 150, 54),
            "CAREER LEVEL", Arena.CurrentCareerLevel.ToString(), ArenaTheme.Cyan));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 188, panel.Y + 104, 150, 54),
            "PERKS", Arena.CurrentPerkCount.ToString(), ArenaTheme.Amber));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 352, panel.Y + 104, 150, 54),
            "RESEARCHED", Arena.CurrentResearchedModuleCount.ToString(), ArenaTheme.Green));

        UIList footer = AddList(new Vector2(c.X - 90, panel.Bottom - 58));
        footer.Padding = new Vector2(2f, 12f);
        footer.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(footer, "BACK", Back_OnClick);
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
        var panel = new RectF(c.X - 270, c.Y - 175, 540, 350);
        Add(ArenaTheme.Panel(panel));
        Add(ArenaTheme.ScreenTitle(new Vector2(panel.X + 24, panel.Y + 20), "MEMORIAL"));
        Add(ArenaTheme.Body(new Vector2(panel.X + 24, panel.Y + 58),
            "The memorial wall is empty."));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 24, panel.Y + 104, 150, 54),
            "PERMADEATH", Arena.CurrentPermadeathChance.ToString("P0"), ArenaTheme.Red));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 188, panel.Y + 104, 150, 54),
            "TEAMS", Arena.CurrentTeamCount.ToString(), ArenaTheme.Magenta));

        UIList footer = AddList(new Vector2(c.X - 90, panel.Bottom - 58));
        footer.Padding = new Vector2(2f, 12f);
        footer.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(footer, "BACK", Back_OnClick);
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
