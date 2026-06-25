using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.GameScreens.Arena;

public enum ArenaCareerMenuMode
{
    Main,
    NewGame,
    Load,
}

public sealed class ArenaCareerMenuScreen : GameScreen
{
    ArenaCareerMenuMode Mode;
    string StatusText = "";
    int PendingOverwriteSlot;
    ArenaStartArchetype PendingOverwriteArchetype;
    bool NavigatingToArena;

    public ArenaCareerMenuScreen(ArenaCareerMenuMode mode = ArenaCareerMenuMode.Main)
        : base(null, toPause: null)
    {
        Mode = mode;
        CanEscapeFromScreen = true;
        TransitionOnTime  = 0.2f;
        TransitionOffTime = 0.2f;
    }

    public override void LoadContent()
    {
        RemoveAll();

        Vector2 c = ScreenCenter;
        var panel = new RectF(c.X - 390, c.Y - 270, 780, 540);
        Add(ArenaTheme.Panel(panel));

        Add(ArenaTheme.ArenaTitle(new Vector2(panel.X + 24, panel.Y + 28), "STAR GLADIATOR"));
        Add(ArenaTheme.Body(new Vector2(panel.X + 24, panel.Y + 68),
            "Choose a career slot, then launch to the control deck."));

        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 106), ModeTitle));
        Add(new UILabel(new Vector2(panel.X + 170, panel.Y + 106),
            StatusText, ArenaTheme.BodySmallFont, ArenaTheme.TextMuted));

        switch (Mode)
        {
            case ArenaCareerMenuMode.NewGame:
                BuildNewGame(panel);
                break;
            case ArenaCareerMenuMode.Load:
                BuildLoad(panel);
                break;
            default:
                BuildMain(panel);
                break;
        }
    }

    string ModeTitle => Mode switch
    {
        ArenaCareerMenuMode.NewGame => "NEW GAME",
        ArenaCareerMenuMode.Load    => "LOAD",
        _                           => "CAREER SLOTS",
    };

    void BuildMain(RectF panel)
    {
        CareerSlotMetadata recent = CareerManager.MostRecentSlot();
        UIList actions = AddList(new Vector2(panel.X + 24, panel.Y + 146));
        actions.Direction = new Vector2(1f, 0f);
        actions.Padding = new Vector2(8f, 8f);
        actions.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPrimaryButton(actions, recent.Exists ? $"CONTINUE SLOT {recent.Slot}" : "CONTINUE",
            _ => Continue(), 190f).Name = "arena_continue";
        ArenaTheme.AddPillButton(actions, "NEW GAME", _ => SetMode(ArenaCareerMenuMode.NewGame), 150f).Name = "arena_new_game";
        ArenaTheme.AddPillButton(actions, "MULTIPLAYER", _ => OpenMultiplayerLobby(), 150f).Name = "arena_multiplayer";
        ArenaTheme.AddPillButton(actions, "BACK", _ => ExitScreen(), 100f).Name = "arena_back";

        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 206), "SLOTS"));
        DrawSlotRows(panel, panel.Y + 238, loadMode: true, newGameMode: false);
    }

    void BuildLoad(RectF panel)
    {
        DrawSlotRows(panel, panel.Y + 146, loadMode: true, newGameMode: false);
        DrawBackRow(panel);
    }

    void BuildNewGame(RectF panel)
    {
        Add(ArenaTheme.Small(new Vector2(panel.X + 24, panel.Y + 136),
            "ACE: one strong ship. WINGMATES: three light ships. SWARM: four fielded light ships."));
        DrawSlotRows(panel, panel.Y + 166, loadMode: false, newGameMode: true);
        DrawBackRow(panel);
    }

    void DrawSlotRows(RectF panel, float top, bool loadMode, bool newGameMode)
    {
        for (int slot = CareerManager.MinSlot; slot <= CareerManager.MaxSlot; ++slot)
        {
            int slotIndex = slot;
            CareerSlotMetadata meta = CareerManager.GetSlotMetadata(slot);
            float y = top + (slot - CareerManager.MinSlot) * 96f;
            var row = new RectF(panel.X + 24, y, panel.W - 48, 76);
            Add(ArenaTheme.Card(row));
            Add(ArenaTheme.SectionHeader(new Vector2(row.X + 14, row.Y + 12), $"SLOT {slot}"));
            Add(new UILabel(new Vector2(row.X + 14, row.Y + 38),
                meta.Exists ? meta.Summary : "Empty slot",
                ArenaTheme.BodySmallFont,
                meta.Exists ? ArenaTheme.TextSecondary : ArenaTheme.TextMuted));

            UIList buttons = AddList(new Vector2(row.Right - (newGameMode ? 350 : 132), row.Y + 22));
            buttons.Direction = new Vector2(1f, 0f);
            buttons.Padding = new Vector2(6f, 6f);
            buttons.LayoutStyle = ListLayoutStyle.ResizeList;

            if (newGameMode)
            {
                ArenaTheme.AddPillButton(buttons, "ACE", _ => TryNew(slotIndex, ArenaStartArchetype.Ace), 82f);
                ArenaTheme.AddPillButton(buttons, "WINGMATES", _ => TryNew(slotIndex, ArenaStartArchetype.Wingmates), 120f);
                ArenaTheme.AddPillButton(buttons, "SWARM", _ => TryNew(slotIndex, ArenaStartArchetype.Swarm), 94f);
            }
            else if (loadMode)
            {
                ArenaTheme.AddPillButton(buttons, "LOAD", _ => LoadSlot(slotIndex), 96f);
            }
        }
    }

    void DrawBackRow(RectF panel)
    {
        UIList footer = AddList(new Vector2(panel.X + 24, panel.Bottom - 58));
        footer.Direction = new Vector2(1f, 0f);
        footer.Padding = new Vector2(8f, 8f);
        footer.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(footer, "BACK", _ => SetMode(ArenaCareerMenuMode.Main), 110f);
        ArenaTheme.AddPillButton(footer, "EXIT", _ => ExitScreen(), 96f);
    }

    void Continue()
    {
        CareerSlotMetadata recent = CareerManager.MostRecentSlot();
        if (!recent.Exists)
        {
            GameAudio.NegativeClick();
            StatusText = "No saved career yet. Start a new game.";
            LoadContent();
            return;
        }
        LoadSlot(recent.Slot);
    }

    void LoadSlot(int slot)
    {
        if (!CareerManager.IsSlotOccupied(slot))
        {
            GameAudio.NegativeClick();
            StatusText = $"Slot {slot} is empty.";
            LoadContent();
            return;
        }

        GameAudio.AffirmativeClick();
        OpenCareerSlot(slot);
    }

    void TryNew(int slot, ArenaStartArchetype archetype)
    {
        bool confirm = PendingOverwriteSlot == slot && PendingOverwriteArchetype == archetype;
        ArenaNewCareerResult result = CareerManager.TryCreateNewSlot(slot, archetype, confirm);
        if (result.Success)
        {
            GameAudio.AffirmativeClick();
            OpenCareerSlot(slot);
            return;
        }

        GameAudio.NegativeClick();
        PendingOverwriteSlot = slot;
        PendingOverwriteArchetype = archetype;
        StatusText = result.Message;
        LoadContent();
    }

    void SetMode(ArenaCareerMenuMode mode)
    {
        GameAudio.AcceptClick();
        Mode = mode;
        StatusText = "";
        PendingOverwriteSlot = 0;
        LoadContent();
    }

    void OpenCareerSlot(int slot)
    {
        NavigatingToArena = true;
        ScreenManager.GoToScreen(ArenaFightScreen.CreateForCareerSlot(slot, startAtHub: true),
            clear3DObjects: true);
    }

    void OpenMultiplayerLobby()
    {
        NavigatingToArena = true;
        ScreenManager.GoToScreen(new ArenaMultiplayerLobbyScreen(), clear3DObjects: true);
    }

    public override void ExitScreen()
    {
        if (NavigatingToArena)
        {
            base.ExitScreen();
            return;
        }

        base.ExitScreen();
        ScreenManager.GoToScreen(new Ship_Game.GameScreens.MainMenu.MainMenuScreen(), clear3DObjects: true);
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        ScreenManager.FadeBackBufferToBlack(TransitionAlpha * 2 / 3);
        batch.SafeBegin();
        base.Draw(batch, elapsed);
        batch.SafeEnd();
    }
}
