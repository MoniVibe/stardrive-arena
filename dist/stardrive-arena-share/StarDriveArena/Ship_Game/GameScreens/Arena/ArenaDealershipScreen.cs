using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;
using Ship_Game.GameScreens.Arena;
using Ship_Game.GameScreens.ShipDesign;
using Ship_Game.Ships;
using Vector2 = SDGraphics.Vector2;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// PHASE B — DEALERSHIP: buy more OWNED vessels with the run's cash, reached from the STAGES
/// HUB's DEALERSHIP button. It lists AFFORDABLE legal combat craft for the current career tier
/// (Arena.DealershipCatalog, cost-gated against the run's Cash), priced per design
/// (Arena.VesselPrice). Buying one runs the REAL buy (Arena.BuyVessel): deduct cash, add a new
/// OwnedVessel (fresh VesselId, 0 veterancy) to the career, unlock its hull on the player
/// empire, and Save — so finite cash yields finite buys and the new vessel joins the GARAGE
/// roster (selectable as the active gladiator).
///
/// "Cash -> a new owned vessel": this is how the player grows the FINITE owned roster. A popup
/// over the paused arena (same GameScreen(parent, toPause:parent) + IsPopup model as the hub).
///
/// Proven headless by ArenaOwnedInventory_Headless (a buy deducts cash, OwnedVessels count +1,
/// and the bought hull is unlocked).
/// </summary>
public sealed class ArenaDealershipScreen : GameScreen
{
    readonly ArenaFightScreen Arena;
    ShipInfoOverlayComponent ShipInfoOverlay;

    public ArenaDealershipScreen(ArenaFightScreen arena) : base(arena, toPause: arena)
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
        var panel = new RectF(c.X - 240, c.Y - 240, 480, 480);
        Add(ArenaTheme.Panel(panel));

        Add(ArenaTheme.ScreenTitle(new Vector2(panel.X + 24, panel.Y + 18),
            "DEALERSHIP — BUY A VESSEL"));

        Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 50),
            $"Cash: ${Arena.CurrentCash}", ArenaTheme.BodyFont, ArenaTheme.TextSecondary));

        // AFFORDABLE catalog: tier-legal combat craft the player can pay for, cheapest first.
        IShipDesign[] catalog = ArenaFightScreen.DealershipCatalog(
            Arena.ArenaPlayerEmpire, Arena.CurrentCash, affordableOnly: true, Arena.CurrentCareerLevel);

        float footerTop = panel.Bottom - 72f;
        var listRect = new RectF(panel.X + 24, panel.Y + 84, panel.W - 48, footerTop - panel.Y - 96);
        var list = Add(new ScrollList<ArenaPopupListItem>(listRect, 34));
        list.ItemPadding = new Vector2(0f, 6f);
        list.EnableItemHighlight = true;
        list.OnClick = item => item.Activate();
        ShipInfoOverlay = Add(new ShipInfoOverlayComponent(this, Arena.UState));
        list.OnHovered = item =>
        {
            if (item?.Payload is IShipDesign design)
                ShipInfoOverlay.ShowToLeftOf(new Vector2(list.X, item.Y), design);
            else
                ShipInfoOverlay.Hide();
        };

        if (catalog.Length == 0)
        {
            list.AddItem(new ArenaPopupListItem("Nothing affordable - win more fights for cash.",
                textColor: ArenaTheme.Red));
        }
        else
        {
            for (int i = 0; i < catalog.Length; ++i)
            {
                IShipDesign design = catalog[i]; // capture for the closure
                int price = ArenaFightScreen.VesselPrice(design, Arena.ArenaPlayerEmpire);
                string label = $"{design.Name}  (${price}, {design.Role})";
                string tooltip = ArenaDesignTooltipProvider.ForDesign(design).ToTooltipText();
                list.AddItem(new ArenaPopupListItem(label, () => Buy_OnClick(design),
                    tooltip: tooltip, payload: design));
            }
        }

        UIList actions = AddList(new Vector2(c.X - 54, footerTop + 14), new Vector2(108, 40));
        actions.Padding = new Vector2(2f, 12f);
        actions.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(actions, "BACK", Back_OnClick);
    }

    // BUY: run the REAL buy (deduct cash, add owned vessel, unlock hull, Save), then refresh so
    // the cash + affordable list reflect the purchase.
    void Buy_OnClick(IShipDesign design)
    {
        if (design == null)
            return;
        OwnedVessel bought = Arena.BuyVessel(design.Name);
        if (bought != null)
            GameAudio.AcceptClick();
        else
            GameAudio.NegativeClick();
        LoadContent(); // rebuild: cash dropped, the next-affordable tier shows
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
