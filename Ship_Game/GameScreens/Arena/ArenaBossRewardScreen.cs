using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;
using Vector2 = SDGraphics.Vector2;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaBossRewardScreen : GameScreen
{
    readonly ArenaFightScreen Arena;
    readonly ArenaPerkDefinition[] Choices;

    public ArenaBossRewardScreen(ArenaFightScreen arena, ArenaPerkDefinition[] choices) : base(arena, toPause: arena)
    {
        Arena = arena;
        Choices = choices ?? System.Array.Empty<ArenaPerkDefinition>();
        IsPopup = true;
        CanEscapeFromScreen = true;
        TransitionOnTime  = 0.2f;
        TransitionOffTime = 0.2f;
    }

    public override void LoadContent()
    {
        RemoveAll();

        Vector2 c = ScreenCenter;
        var panel = new RectF(c.X - 260, c.Y - 180, 520, 360);
        Add(ArenaTheme.Panel(panel));

        Add(ArenaTheme.ScreenTitle(new Vector2(panel.X + 24, panel.Y + 18),
            "BOSS REWARD"));
        Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 54),
            "Choose one persistent Arena perk.", ArenaTheme.BodyFont, ArenaTheme.TextSecondary));

        UIList list = AddList(new Vector2(panel.X + 24, panel.Y + 92),
            new Vector2(panel.W - 48, panel.H - 150));
        list.Padding = new Vector2(2f, 10f);
        list.LayoutStyle = ListLayoutStyle.ResizeList;

        foreach (ArenaPerkDefinition perk in Choices)
        {
            ArenaPerkDefinition captured = perk;
            ArenaTheme.AddPillButton(list, $"{perk.Name}: {perk.Description}",
                _ => Pick(captured), panel.W - 56, 34f);
        }

        UIList footer = AddList(new Vector2(c.X - 90, panel.Bottom - 54));
        footer.Padding = new Vector2(2f, 12f);
        footer.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(footer, "BACK", Back_OnClick);
    }

    void Pick(ArenaPerkDefinition perk)
    {
        if (Arena != null && Arena.GrantPerk(perk.Id))
            GameAudio.AcceptClick();
        else
            GameAudio.NegativeClick();
        ExitScreen();
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
