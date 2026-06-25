using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;
using Vector2 = SDGraphics.Vector2;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// STAGES HUB (MVP) — the between-fights downtime hub for the roguelike ARENA run.
///
/// When the player clears a round, ArenaFightScreen.EnterShop() now PUSHES this hub
/// (instead of toggling the old inline shop panel). The hub is a popup over the paused
/// arena (mirrors GamePlayMenuScreen: `: base(arena, toPause: arena)`, IsPopup=true,
/// CanEscapeFromScreen=true) that gives the run a real "downtime" beat between fights:
///
///   - a round-keyed DIALOGUE line (flavor; data not infrastructure — a hardcoded
///     string[] per round) at the top,
///   - SHOP    -> surfaces the existing inline ShopPanel on the arena screen (the buys
///                are reused as-is, just reached from the hub),
///   - FLEET   -> ArenaFightScreen.BuildArenaFleet(): turns the persistent PlayerShips
///                roster into a real Fleet with assigned formation offsets so it spawns
///                as a formation next round (SPAWN-TIME layout only),
///   - HANGAR  -> reuses the existing Customize-Gladiator ShipDesignScreen flow (a stub
///                for now — same designer the shop's "Customize Gladiator" opens),
///   - NEXT FIGHT -> ExitScreen() + ArenaFightScreen.NextFight() resumes the run.
///
/// The hub holds the run together as a phase: while it's up the arena is in the
/// Shopping phase (sim paused, no win/lose checks). NEXT FIGHT is always available so
/// the run stays completable. Proven headless by the ArenaLiveScreenDrive_Headless
/// extension (hub on the stack after round 1, FLEET builds a real Fleet, NEXT FIGHT
/// advances to round 2 Fighting).
/// </summary>
public sealed class ArenaHubScreen : GameScreen
{
    readonly ArenaFightScreen Arena;
    UILabel DialogueLabel;
    UILabel StatusLabel;

    // Round-keyed DIALOGUE (MVP: data not infrastructure). Index by the round the player
    // is heading INTO next, clamped to the table. A short flavor beat per intermission.
    static readonly string[] RoundDialogue =
    {
        // [0] unused (rounds are 1-based); kept so Round indexes directly.
        "The crowd roars. Catch your breath, gladiator.",
        "First blood drawn. The bookmakers are nervous — good.",
        "They send a pack now. Steel yourself and your wing.",
        "A champion waits beyond. This is the fight they paid to see.",
    };

    public ArenaHubScreen(ArenaFightScreen arena) : base(arena, toPause: arena)
    {
        Arena = arena;
        IsPopup = true;
        CanEscapeFromScreen = !arena.IsIdleHubPhase;
        TransitionOnTime  = 0.2f;
        TransitionOffTime = 0.2f;
    }

    public override void LoadContent()
    {
        RemoveAll();

        Vector2 c = ScreenCenter;
        var panel = new RectF(c.X - 440, c.Y - 300, 880, 600);
        Add(ArenaTheme.Panel(panel));

        UIPanel badge = Add(ArenaTheme.Card(new RectF(panel.X + 24, panel.Y + 20, 34, 22)));
        badge.Color = ArenaTheme.Amber;
        badge.Border = ArenaTheme.Amber;
        UILabel v0 = badge.Add(new UILabel(new Vector2(panel.X + 34, panel.Y + 25),
            "v0", ArenaTheme.LabelFont, ArenaTheme.VoidBlack));
        v0.TextAlign = TextAlign.Center;

        Add(ArenaTheme.Label(new Vector2(panel.X + 68, panel.Y + 25),
            "UI GOD PAGE · SOURCE OF TRUTH"));

        Add(ArenaTheme.ArenaTitle(new Vector2(panel.X + 24, panel.Y + 58),
            "STAR GLADIATOR"));

        Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 100),
            Fonts.Arial12.ParseText(
                "The control deck of an illegal orbital coliseum — steel, glass, holographic telemetry, betting slips, pilot dossiers and tactical battle reports. Build a champion, climb the gladiator ladder, bet on headless arena fights, and read the report that teaches your next move.",
                panel.W - 48),
            ArenaTheme.BodySmallFont, ArenaTheme.TextSecondary));

        UIList nav = AddList(new Vector2(panel.X + 24, panel.Y + 166));
        nav.Direction = new Vector2(1f, 0f);
        nav.Padding = new Vector2(8f, 2f);
        nav.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(nav, "DESIGNER", Designer_OnClick, 100f, 30f);
        ArenaTheme.AddPrimaryButton(nav, "BOUT", Bout_OnClick, 82f, 30f);
        ArenaTheme.AddPillButton(nav, "BET", Bet_OnClick, 70f, 30f);
        ArenaTheme.AddPillButton(nav, "SHOP", ShopMarket_OnClick, 78f, 30f);
        ArenaTheme.AddPillButton(nav, "CLIMB", Climb_OnClick, 82f, 30f);
        ArenaTheme.AddPillButton(nav, "BOSS", Boss_OnClick, 78f, 30f);

        // Round-keyed DIALOGUE line at the top — a short flavor beat (the dialogue MVP).
        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 226), "INTERMISSION"));
        DialogueLabel = Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 250),
            DialogueForRound(Arena.HubRound), ArenaTheme.BodyFont, ArenaTheme.TextSecondary));

        Add(ArenaTheme.StatChip(new RectF(panel.X + 24, panel.Y + 292, 150, 54),
            "CASH", () => CashDisplayText, ArenaTheme.Amber));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 188, panel.Y + 292, 150, 54),
            "CAREER LEVEL", () => Arena.CurrentCareerLevel.ToString(), ArenaTheme.Cyan));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 352, panel.Y + 292, 150, 54),
            "FIELD TIER", () => Arena.CurrentAllowedCombatTier.ToString(), TierColor(Arena.CurrentAllowedCombatTier)));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 516, panel.Y + 292, 150, 54),
            "FLEET", () => $"{Arena.FleetSize}/{Arena.CurrentMaxFleetSize}", ArenaTheme.Green));

        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 372), "CAREER"));
        StatusLabel = Add(new UILabel(new Vector2(panel.X + 118, panel.Y + 372),
            "", ArenaTheme.BodySmallFont, ArenaTheme.TextMuted));

        UIList careerButtons = AddList(new Vector2(panel.X + 24, panel.Y + 402));
        careerButtons.Direction = new Vector2(1f, 0f);
        careerButtons.Padding = new Vector2(8f, 8f);
        careerButtons.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPrimaryButton(careerButtons, "SAVE", Save_OnClick, 84f);
        ArenaTheme.AddPillButton(careerButtons, "CONFIG", Config_OnClick, 92f);
        ArenaTheme.AddPillButton(careerButtons, "NEW GAME", NewGame_OnClick, 108f);
        ArenaTheme.AddPillButton(careerButtons, "LOAD", Load_OnClick, 80f);
        ArenaTheme.AddPillButton(careerButtons, "EXIT", Exit_OnClick, 80f);

        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 444), "RUN UTILITIES"));
        UIList buttons = AddList(new Vector2(panel.X + 24, panel.Y + 474));
        buttons.Direction = new Vector2(1f, 0f);
        buttons.Padding = new Vector2(8f, 8f);
        buttons.LayoutStyle = ListLayoutStyle.ResizeList;

        ArenaTheme.AddPillButton(buttons, "REPAIR ALL", RepairAll_OnClick, 108f);
        ArenaTheme.AddPillButton(buttons, "FLEET",      Fleet_OnClick, 90f);
        ArenaTheme.AddPillButton(buttons, "DEALERSHIP", Dealership_OnClick, 120f);
        ArenaTheme.AddPillButton(buttons, "HANGAR",     Hangar_OnClick, 96f);
        ArenaTheme.AddPillButton(buttons, "ITEMS",      Items_OnClick, 88f);

        UIList buttons2 = AddList(new Vector2(panel.X + 24, panel.Y + 514));
        buttons2.Direction = new Vector2(1f, 0f);
        buttons2.Padding = new Vector2(8f, 8f);
        buttons2.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(buttons2, "LADDER",     Ladder_OnClick, 96f);
        ArenaTheme.AddPillButton(buttons2, "FIGHT OPTIONS", FightOptions_OnClick, 132f);
        if (Arena.HasPendingBossReward)
            ArenaTheme.AddPillButton(buttons2, "BOSS REWARD", BossReward_OnClick, 118f);

        UIList footer = AddList(new Vector2(panel.X + 24, panel.Bottom - 58));
        footer.Direction = new Vector2(1f, 0f);
        footer.Padding = new Vector2(8f, 2f);
        footer.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPrimaryButton(footer, "NEXT FIGHT", NextFight_OnClick, 130f);
    }

    // The flavor line for the round the player is about to fight (clamped to the table).
    static string DialogueForRound(int round)
    {
        if (round < 0) round = 0;
        if (round >= RoundDialogue.Length) round = RoundDialogue.Length - 1;
        return RoundDialogue[round];
    }

    static Color TierColor(int tier) => tier switch
    {
        1 => ArenaTheme.Tier1,
        2 => ArenaTheme.Tier2,
        3 => ArenaTheme.Tier3,
        _ => ArenaTheme.TextSecondary,
    };

    public string CashDisplayText => $"${Arena.CurrentCash}";

    void Save_OnClick(UIButton button)
    {
        bool saved = Arena.ManualSaveCareer();
        if (saved)
        {
            GameAudio.AcceptClick();
            if (StatusLabel != null) StatusLabel.Text = "Saved.";
        }
        else
        {
            GameAudio.NegativeClick();
            if (StatusLabel != null) StatusLabel.Text = "Save failed.";
        }
    }

    void Config_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenConfig();
    }

    void NewGame_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenCareerNewGame();
    }

    void Load_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenCareerLoad();
    }

    void Exit_OnClick(UIButton button)
    {
        GameAudio.AffirmativeClick();
        Arena.ExitToMainMenu();
    }

    void Designer_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenGarage();
    }

    void Bout_OnClick(UIButton button)
    {
        GameAudio.AffirmativeClick();
        if (Arena.StartBout())
            base.ExitScreen();
        else
            ExitScreen();
    }

    void Bet_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenBetting();
    }

    void ShopMarket_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenModuleShop();
    }

    void Climb_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenLeaderboard();
    }

    void Boss_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenBossChallenges();
    }

    // REPAIR ALL: repair surviving player ships in-place and stay on this hub. This replaces
    // the old path that exposed the legacy inline "round cleared" intermission panel.
    void RepairAll_OnClick(UIButton button)
    {
        if (Arena.IsIdleHubPhase)
        {
            GameAudio.NegativeClick();
            return;
        }

        ArenaRepairResult result = Arena.RepairAllFromHub();
        if (result.Success)
        {
            GameAudio.AcceptClick();
            if (StatusLabel != null) StatusLabel.Text = result.Message;
        }
        else
        {
            GameAudio.NegativeClick();
            if (StatusLabel != null) StatusLabel.Text = result.Message;
        }
    }

    // FLEET: open the FLEET SETUP composer (ArenaFleetScreen) — pick which OWNED vessels FIELD
    // together as a fleet (toggle in/out, capped). This REPLACES the old bare BuildArenaFleet()
    // call (which only laid out the single gladiator and gave no composition UI) — the "set up
    // the fleet" UI the playtester reported missing. The actual spawn-time formation layout
    // still runs inside SpawnPlayerShips/BuildArenaFleet over the composed fleet next round.
    void Fleet_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenFleet();
    }

    // DEALERSHIP (Phase B): open the dealership to BUY more owned vessels with the run's cash.
    void Dealership_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenDealership();
    }

    // HANGAR (Phase B): open the GARAGE — manage the OWNED fleet, pick the active gladiator,
    // and open the owned-restricted designer.
    void Hangar_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenGarage();
    }

    // ITEMS (ACCESS layer): open the INVENTORY popup — a derived census of every MODULE across
    // the player's owned-vessel designs (UID -> total count), so the player can SEE/access the
    // items on their owned + bought ships. The playtester's "i dont seem to have access to the
    // items..." gap. (View-only this pass; designer-consumes-owned + strip/transfer are deferred.)
    void Items_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenInventory();
    }

    // LADDER (Phase C): open the persistent contender leaderboard. Selecting a contender queues
    // that design as the next fight's enemy.
    void Ladder_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenLeaderboard();
    }

    void FightOptions_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenFightOptions();
    }

    void BossReward_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenBossReward();
    }

    // NEXT FIGHT: close the hub and resume the run. ExitScreen() now resumes the run itself
    // (see below), so just dismiss — NextFight() inside ExitScreen advances the round.
    void NextFight_OnClick(UIButton button)
    {
        GameAudio.AffirmativeClick();
        if (Arena.StartBout())
            base.ExitScreen();
        else
            ExitScreen();
    }

    // ANY hub dismissal RESUMES THE RUN — no soft-lock. The hub is pushed while the arena sits
    // in RunPhase.Shopping (sim paused). If the player dismisses the hub via ESCAPE or right-click
    // (GameScreen.HandleInput -> ExitScreen) instead of pressing NEXT FIGHT, the old code just
    // removed the popup, stranding the run paused in Shopping with no way back. Routing EVERY
    // exit through Arena.NextFight() (which is idempotent — it early-returns unless the run is in
    // Shopping) guarantees the run is always resumable: Escape/right-click now advance to the next
    // fight exactly like the button. The NEXT FIGHT button also goes through here, so resume fires
    // once and only once.
    public override void ExitScreen()
    {
        if (Arena != null && !Arena.IsIdleHubPhase)
            Arena.NextFight();
        base.ExitScreen();
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        ScreenManager.FadeBackBufferToBlack(TransitionAlpha * 2 / 3);
        batch.SafeBegin();
        base.Draw(batch, elapsed);
        batch.SafeEnd();
    }
}
