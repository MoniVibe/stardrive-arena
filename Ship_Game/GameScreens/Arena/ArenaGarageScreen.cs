using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;
using Ship_Game.GameScreens.Arena;
using Ship_Game.GameScreens.ShipDesign;
using Vector2 = SDGraphics.Vector2;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// PHASE B — GARAGE: the player's OWNED-fleet manager, reached from the STAGES HUB's HANGAR
/// button. It shows the FINITE roster of vessels the career OWNS (each with its design +
/// carried VETERANCY — level/kills), lets the player SELECT which owned vessel is the ACTIVE
/// gladiator the next run fields (Arena.SetActiveOwnedVessel -> sets ActiveVesselId + Save +
/// live respawn), and opens the full ship designer RESTRICTED to what the player owns
/// (Arena.OpenHangar -> the existing Customize-Gladiator designer, which reads the player
/// empire's unlocked hulls — the owned roster's hulls were re-granted on career apply).
///
/// "Field what you OWN": the gladiator is always one of these owned vessels. A popup over the
/// paused arena (same GameScreen(parent, toPause:parent) + IsPopup model as the hub).
///
/// Proven headless by ArenaOwnedInventory_Headless (the GARAGE select changes which design the
/// gladiator spawns as, and the roster + ActiveVesselId persist).
/// </summary>
public sealed class ArenaGarageScreen : GameScreen
{
    public const string RefitButtonTooltip = "Refit: restore destroyed modules for cash.";
    public const int VeteranLevelThreshold = 3;
    public const int VeteranKillThreshold = 5;
    public const int LegendaryLevelThreshold = 8;
    public const int LegendaryKillThreshold = 15;

    readonly ArenaFightScreen Arena;
    ShipInfoOverlayComponent ShipInfoOverlay;

    public ArenaGarageScreen(ArenaFightScreen arena) : base(arena, toPause: arena)
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
            "GARAGE — YOUR OWNED FLEET"));

        Arena.RefreshOwnedVesselVeterancyForUi();
        OwnedVessel[] owned = Arena.OwnedVessels;
        string activeId = Arena.ActiveVesselId;

        Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 50),
            owned.Length == 1 ? "1 vessel owned" : $"{owned.Length} vessels owned",
            ArenaTheme.BodyFont, ArenaTheme.TextSecondary));

        float footerTop = panel.Bottom - 72f;
        var listRect = new RectF(panel.X + 24, panel.Y + 84, panel.W - 48, footerTop - panel.Y - 96);
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

        foreach (OwnedVessel v in owned)
        {
            if (v == null) continue;
            OwnedVessel vessel = v; // capture for the closure
            bool isActive = string.Equals(v.VesselId, activeId, System.StringComparison.Ordinal);
            ArenaVesselActivationResult status = Arena.GetOwnedVesselActivationStatus(v.VesselId);
            int destroyedSlots = Arena.DestroyedModuleSlots(v.VesselId).Length;
            string label = (isActive ? "[ACTIVE] " : "") +
                           $"{Display(v)}  (Lvl {v.Level}, {v.Kills} kills)";
            string tags = StatusTags(v, destroyedSlots);
            if (tags.NotEmpty())
                label += $"  {tags}";
            if (!status.Success)
                label += $" - {status.Message}";
            string tooltip = ArenaDesignTooltipProvider.ForOwnedVessel(v).ToTooltipText();
            if (!status.Success && status.Message.NotEmpty())
                tooltip += $"\n{status.Message}";
            list.AddItem(new ArenaPopupListItem(label, () => Select_OnClick(vessel),
                tooltip: tooltip,
                textColor: status.Success ? ArenaTheme.TextPrimary : ArenaTheme.TextMuted,
                payload: vessel));
        }

        UIList actions = AddList(new Vector2(c.X - 215, footerTop + 14), new Vector2(430, 40));
        actions.Padding = new Vector2(8f, 2f);
        actions.LayoutStyle = ListLayoutStyle.ResizeList;
        actions.Direction = new Vector2(1f, 0f);
        ArenaTheme.AddPillButton(actions, "CUSTOMIZE", Customize_OnClick, 116f);
        UIButton refit = ArenaTheme.AddPillButton(actions, "REFIT", Refit_OnClick, 76f);
        refit.Tooltip = RefitButtonTooltip;
        ArenaTheme.AddPillButton(actions, "ITEMS",     Items_OnClick, 82f);
        ArenaTheme.AddPillButton(actions, "BACK",      Back_OnClick, 82f);
    }

    // ITEMS (ACCESS layer): open the INVENTORY popup — the derived census of every MODULE across
    // the player's owned-vessel designs (UID -> count), so the player can SEE the items on their
    // owned + bought ships. View-only this pass.
    void Items_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenInventory();
        ExitScreen();
    }

    static string Display(OwnedVessel v)
        => v.Name.NotEmpty() ? $"{v.Name} ({v.DesignName})" : v.DesignName;

    public static string StatusTags(OwnedVessel vessel, int destroyedSlots)
    {
        if (vessel == null)
            return "";

        var tags = new System.Collections.Generic.List<string>();
        if (vessel.Level >= LegendaryLevelThreshold || vessel.Kills >= LegendaryKillThreshold)
            tags.Add("[LEGENDARY]");
        else if (vessel.Level >= VeteranLevelThreshold || vessel.Kills >= VeteranKillThreshold)
            tags.Add("[VETERAN]");

        if (vessel.CurrentHullHealth > 0f && vessel.MaxHullHealth > vessel.CurrentHullHealth + 0.5f)
        {
            float fraction = System.Math.Clamp(vessel.CurrentHullHealth / vessel.MaxHullHealth, 0f, 1f);
            int hull = (int)System.Math.Round(
                fraction * 100f);
            tags.Add($"[HULL {hull}%]");
        }

        if (destroyedSlots > 0)
            tags.Add($"[SCAR {destroyedSlots}]");

        return string.Join(" ", tags);
    }

    // SELECT: make the clicked owned vessel the ACTIVE gladiator (sets ActiveVesselId + Save +
    // live respawn), then refresh the list so the [ACTIVE] marker moves.
    void Select_OnClick(OwnedVessel vessel)
    {
        bool selected = vessel != null && Arena.SetActiveOwnedVessel(vessel.VesselId);
        if (selected)
            GameAudio.AcceptClick();
        else
            GameAudio.NegativeClick();
        LoadContent(); // rebuild so the ACTIVE marker reflects the new selection
    }

    // CUSTOMIZE: open the full ship designer restricted to what the player OWNS (the same
    // Customize-Gladiator flow; it reads the player empire's unlocked hulls).
    void Customize_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        Arena.OpenHangar();
    }

    void Refit_OnClick(UIButton button)
    {
        ArenaRefitResult result = Arena.RefitFirstDestroyedModuleForCash(Arena.ActiveVesselId);
        if (result.Success)
            GameAudio.AcceptClick();
        else
            GameAudio.NegativeClick();
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
