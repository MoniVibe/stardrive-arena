using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;
using Ship_Game.GameScreens.ShipDesign;
using Vector2 = SDGraphics.Vector2;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// FLEET SETUP — the player's FLEET COMPOSER, reached from the STAGES HUB's FLEET button.
///
/// THE GAP IT CLOSES: the hub's old FLEET button only ran a spawn-time formation LAYOUT
/// (Arena.BuildArenaFleet) over whatever single gladiator existed — there was no way to
/// COMPOSE the fleet (pick which OWNED vessels fight together). A playtester reported "i
/// dont seem to be able to set up the fleet." This popup is that missing UI: it lists every
/// owned vessel with an IN-FLEET toggle, so the player can FIELD a fleet of multiple owned
/// vessels (up to the career's current perk-adjusted cap), not just the lone active one.
///
/// Each owned vessel shows its design + Lvl/kills and an [IN FLEET]/[—] marker; the ACTIVE
/// vessel is the FLAGSHIP and is always in the fleet (its toggle is a no-op). Toggling updates
/// <see cref="ArenaCareer.FleetVesselIds"/> via <see cref="ArenaFightScreen.ToggleFleetVessel"/>
/// (which enforces the max-size cap) and saves the career. NEXT FIGHT then spawns ALL fielded
/// vessels in a formation (Arena.SpawnPlayerShips over the fielded fleet).
///
/// A full drag-place formation editor is NOT this pass — the formation is the default
/// AssignPositions layout; a note explains that. Same GameScreen(parent, toPause:parent) +
/// IsPopup popup model as the GARAGE/DEALERSHIP/HUB. Proven headless by ArenaFleetSetup_Headless
/// (toggling composes a multi-vessel fleet that SPAWNS in formation with per-vessel veterancy;
/// the cap holds; FleetVesselIds round-trips through Save/Load; a single-vessel career still
/// fields exactly one ship).
/// </summary>
public sealed class ArenaFleetScreen : GameScreen
{
    readonly ArenaFightScreen Arena;
    UILabel CountLabel;
    ShipInfoOverlayComponent ShipInfoOverlay;

    public ArenaFleetScreen(ArenaFightScreen arena) : base(arena, toPause: arena)
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
            "FLEET SETUP — COMPOSE YOUR SQUAD"));

        Arena.RefreshOwnedVesselVeterancyForUi();
        OwnedVessel[] owned = Arena.OwnedVessels;
        int fielded = Arena.FleetSize;
        int max     = Arena.CurrentMaxFleetSize;

        // Live count line: "N / MAX in fleet". Updates as the player toggles vessels.
        CountLabel = Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 50),
            $"{fielded} / {max} vessels in fleet  ({owned.Length} owned)",
            ArenaTheme.BodyFont, ArenaTheme.TextSecondary));

        // Formation note: this pass uses the default formation (no drag-place editor yet).
        Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 72),
            "The flagship leads; the fleet spawns in a default formation.",
            ArenaTheme.BodySmallFont, ArenaTheme.TextMuted));

        float footerTop = panel.Bottom - 72f;
        var listRect = new RectF(panel.X + 24, panel.Y + 100, panel.W - 48, footerTop - panel.Y - 112);
        var list = Add(new ScrollList<ArenaPopupListItem>(listRect, 34));
        list.ItemPadding = new Vector2(0f, 6f);
        list.EnableItemHighlight = true;
        list.OnClick = item => item.Activate();
        ShipInfoOverlay = Add(new ShipInfoOverlayComponent(this, Arena.UState));
        list.OnHovered = item =>
        {
            if (item?.Payload is OwnedVessel vessel
                && ResourceManager.Ships.GetDesign(vessel.DesignName, out Ship_Game.Ships.IShipDesign design))
            {
                ShipInfoOverlay.ShowToLeftOf(new Vector2(list.X, item.Y), design);
            }
            else
            {
                ShipInfoOverlay.Hide();
            }
        };

        string activeId = Arena.ActiveVesselId;
        foreach (OwnedVessel v in owned)
        {
            if (v == null) continue;
            OwnedVessel vessel = v; // capture for the closure
            bool isFlagship = string.Equals(v.VesselId, activeId, System.StringComparison.Ordinal);
            bool inFleet    = Arena.IsInFleet(v.VesselId);

            string marker = isFlagship ? "[FLAGSHIP]" : (inFleet ? "[IN FLEET]" : "[  --  ]");
            string label  = $"{marker} {Display(v)}  (Lvl {v.Level}, {v.Kills} kills)";
            string tooltip = ArenaDesignTooltipProvider.ForOwnedVessel(v).ToTooltipText();
            list.AddItem(new ArenaPopupListItem(label, () => Toggle_OnClick(vessel),
                tooltip: tooltip, payload: vessel));
        }

        UIList actions = AddList(new Vector2(c.X - 54, footerTop + 14), new Vector2(108, 40));
        actions.Padding = new Vector2(2f, 12f);
        actions.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(actions, "BACK", Back_OnClick);
    }

    static string Display(OwnedVessel v)
        => v.Name.NotEmpty() ? $"{v.Name} ({v.DesignName})" : v.DesignName;

    // IN-FLEET TOGGLE: add/remove the clicked owned vessel from the active fleet (the flagship
    // is permanent; the cap is enforced by ToggleFleetVessel). Rebuild the list so the markers
    // and the count line reflect the new composition.
    void Toggle_OnClick(OwnedVessel vessel)
    {
        if (vessel == null) return;
        bool changed = Arena.ToggleFleetVessel(vessel.VesselId);
        if (changed)
            GameAudio.AcceptClick();
        else
            GameAudio.NegativeClick(); // at the cap (or not togglable) — denied
        LoadContent(); // rebuild so markers + count reflect the new fleet
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
