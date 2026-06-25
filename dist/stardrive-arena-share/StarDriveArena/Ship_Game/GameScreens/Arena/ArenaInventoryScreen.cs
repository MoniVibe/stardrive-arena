using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;
using Vector2 = SDGraphics.Vector2;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// ITEMS / INVENTORY — the ACCESS/VIEW layer for the arena CAREER's researched MODULES.
///
/// The shop now sells permanent research unlocks. This popup lists those researched modules,
/// which are designable without finite item counts. Old finite salvage saves migrate into the
/// researched set during career normalization.
///
/// Same GameScreen(parent, toPause:parent) + IsPopup popup model as the GARAGE/FLEET/DEALERSHIP.
/// Proven headless by ArenaOwnedItems_Headless.
/// </summary>
public sealed class ArenaInventoryScreen : GameScreen
{
    readonly ArenaFightScreen Arena;

    public ArenaInventoryScreen(ArenaFightScreen arena) : base(arena, toPause: arena)
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
        var panel = new RectF(c.X - 250, c.Y - 250, 500, 500);
        Add(ArenaTheme.Panel(panel));

        Add(ArenaTheme.ScreenTitle(new Vector2(panel.X + 24, panel.Y + 18),
            "ITEMS - RESEARCHED MODULES"));

        ArenaCareer.ModuleCensusEntry[] census = Arena.GetOwnedModuleCensus();
        OwnedVessel[] owned = Arena.OwnedVessels;

        // Summary line: distinct modules + total module count across the owned fleet.
        int totalCount = 0;
        foreach (ArenaCareer.ModuleCensusEntry e in census)
            totalCount += e.Count;

        Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 50),
            $"{census.Length} researched modules  ({owned.Length} vessels owned)",
            ArenaTheme.BodyFont, ArenaTheme.TextSecondary));

        Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 72),
            "Researched modules are permanently unlocked for custom designs.",
            ArenaTheme.BodySmallFont, ArenaTheme.TextMuted));

        float footerTop = panel.Bottom - 72f;
        var listRect = new RectF(panel.X + 24, panel.Y + 100, panel.W - 48, footerTop - panel.Y - 112);
        var list = Add(new ScrollList<ArenaPopupListItem>(listRect, 34));
        list.ItemPadding = new Vector2(0f, 6f);
        list.EnableItemHighlight = true;
        list.OnClick = item => item.Activate();

        if (census.Length == 0)
        {
            list.AddItem(new ArenaPopupListItem("No module research completed yet.",
                textColor: ArenaTheme.TextMuted));
        }
        else
        {
            foreach (ArenaCareer.ModuleCensusEntry e in census)
            {
                list.AddItem(new ArenaPopupListItem($"{e.DisplayName}  [RESEARCHED]",
                    tooltip: e.ModuleUid));
            }
        }

        UIList actions = AddList(new Vector2(c.X - 54, footerTop + 14), new Vector2(108, 40));
        actions.Padding = new Vector2(2f, 12f);
        actions.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(actions, "BACK", Back_OnClick);
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
