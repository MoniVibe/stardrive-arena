using System.Globalization;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaConfigScreen : GameScreen
{
    readonly ArenaFightScreen Arena;
    string StatusText = "";

    public ArenaConfigScreen(ArenaFightScreen arena) : base(arena, toPause: arena)
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
        var panel = new RectF(c.X - 300, c.Y - 220, 600, 440);
        Add(ArenaTheme.Panel(panel));

        Add(ArenaTheme.ScreenTitle(new Vector2(panel.X + 24, panel.Y + 18), "CONFIG"));
        Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 52),
            StatusText, ArenaTheme.BodySmallFont, ArenaTheme.TextMuted));

        float y = panel.Y + 92;
        AddToggleRow(panel, y, "Player ship permadeath",
            Arena?.CurrentPlayerShipsPermadeath ?? true,
            _ => TogglePlayerPermadeath());
        y += 70;
        AddToggleRow(panel, y, "Hard loss ends run",
            Arena?.CurrentHardLossEndsRun ?? false,
            _ => ToggleHardLoss());
        y += 70;
        AddChanceRow(panel, y);

        UIList footer = AddList(new Vector2(c.X - 54, panel.Bottom - 62), new Vector2(108, 40));
        footer.Padding = new Vector2(2f, 12f);
        footer.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(footer, "BACK", Back_OnClick);
    }

    void AddToggleRow(RectF panel, float y, string label, bool enabled, System.Action<UIButton> click)
    {
        var row = new RectF(panel.X + 24, y, panel.W - 48, 52);
        Add(ArenaTheme.Card(row));
        Add(ArenaTheme.Body(new Vector2(row.X + 14, row.Y + 15), label));
        UIList buttons = AddList(new Vector2(row.Right - 118, row.Y + 11));
        buttons.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(buttons, enabled ? "ON" : "OFF", click, 94f);
    }

    void AddChanceRow(RectF panel, float y)
    {
        float chance = Arena?.CurrentPermadeathChance ?? 0f;
        var row = new RectF(panel.X + 24, y, panel.W - 48, 52);
        Add(ArenaTheme.Card(row));
        Add(ArenaTheme.Body(new Vector2(row.X + 14, row.Y + 15), "Contender permadeath chance"));
        string text = chance.ToString("P0", CultureInfo.InvariantCulture);
        UIList buttons = AddList(new Vector2(row.Right - 118, row.Y + 11));
        buttons.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(buttons, text, _ => CycleContenderPermadeath(), 94f);
    }

    void TogglePlayerPermadeath()
    {
        bool next = !(Arena?.CurrentPlayerShipsPermadeath ?? true);
        Save(Arena != null && Arena.SetPlayerShipsPermadeath(next));
    }

    void ToggleHardLoss()
    {
        bool next = !(Arena?.CurrentHardLossEndsRun ?? false);
        Save(Arena != null && Arena.SetHardLossEndsRun(next));
    }

    void CycleContenderPermadeath()
    {
        float current = Arena?.CurrentPermadeathChance ?? 0f;
        float next = current < 0.125f ? 0.25f
            : current < 0.375f ? 0.50f
            : current < 0.625f ? 0.75f
            : current < 0.875f ? 1.00f
            : 0.00f;
        Save(Arena != null && Arena.SetContenderPermadeathChance(next));
    }

    void Save(bool success)
    {
        if (success)
        {
            GameAudio.AcceptClick();
            StatusText = "Saved.";
        }
        else
        {
            GameAudio.NegativeClick();
            StatusText = "Save failed.";
        }
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
