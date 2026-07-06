using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using SDUtils;
using Ship_Game.Audio;
using Ship_Game.Ships;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// SET FLEET — the interim fleet-name picker for the Star Gladiator multiplayer lobby.
///
/// The lobby's "Fleet: X" was a read-only label with the fielded fleet auto-derived from the
/// career roster / starting roster; there was no way to choose it. This popup is that missing
/// picker: it lists the legal design names offered for the active arena mode (Career: the
/// career's OWNED vessels; Sandbox: every legal arena combat-craft design) with an
/// [IN FLEET]/[ -- ] toggle each, so the local player composes the fleet they field.
///
/// It ONLY sets fleet DESIGN NAMES that already ride the lobby's P1 bundle path
/// (<see cref="ArenaMultiplayerLobbyScreen.ApplyPickedFleet"/> normalizes to legal names), so
/// it is deterministic-safe — no wire/hash change. The drag-place formation editor
/// (FleetDesignScreen) is intentionally DEFERRED; this is the interim name/vessel picker.
///
/// Sandbox with a budget CAP shows a running credits line and denies picks that would exceed the
/// cap (using each design's <see cref="IShipDesign.BaseCost"/> as the cost proxy). Same
/// GameScreen(parent, toPause:parent) + IsPopup popup model as the GARAGE/DEALERSHIP/FLEET SETUP.
/// </summary>
public sealed class ArenaFleetPickerScreen : GameScreen
{
    readonly string[] Options;
    readonly int BudgetCredits; // 0 == no cap
    readonly Action<string[]> Commit;
    readonly HashSet<string> Selected;
    UILabel CountLabel;

    public ArenaFleetPickerScreen(GameScreen parent, string[] options, string[] initialSelection,
        int budgetCredits, Action<string[]> commit)
        : base(parent, toPause: null)
    {
        Options = options ?? Array.Empty<string>();
        BudgetCredits = Math.Max(0, budgetCredits);
        Commit = commit;
        Selected = new HashSet<string>(
            (initialSelection ?? Array.Empty<string>()).Where(n => Options.Contains(n, StringComparer.Ordinal)),
            StringComparer.Ordinal);
        IsPopup = true;
        CanEscapeFromScreen = true;
        TransitionOnTime  = 0.2f;
        TransitionOffTime = 0.2f;
    }

    // Headless proof seam: toggle a design's in-fleet membership (budget-aware) and read the result.
    public bool IsSelectedForHeadless(string designName) => Selected.Contains(designName);
    public string[] SelectedForHeadless => OrderedSelection();
    public bool ToggleForHeadless(string designName) => Toggle(designName);
    public void CommitForHeadless() => CommitAndClose();

    public override void LoadContent()
    {
        RemoveAll();

        Vector2 c = ScreenCenter;
        var panel = new RectF(c.X - 250, c.Y - 250, 500, 500);
        Add(ArenaTheme.Panel(panel));

        Add(ArenaTheme.ScreenTitle(new Vector2(panel.X + 24, panel.Y + 18),
            "SET FLEET — CHOOSE YOUR COMBATANTS"));

        CountLabel = Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 50),
            CountText(), ArenaTheme.BodyFont, ArenaTheme.TextSecondary));

        Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 72),
            BudgetCredits > 0
                ? "Toggle designs to field them; picks over the credit cap are denied."
                : "Toggle designs to field them in this duel.",
            ArenaTheme.BodySmallFont, ArenaTheme.TextMuted));

        float footerTop = panel.Bottom - 72f;
        var listRect = new RectF(panel.X + 24, panel.Y + 100, panel.W - 48, footerTop - panel.Y - 112);
        var list = Add(new ScrollList<ArenaPopupListItem>(listRect, 34));
        list.ItemPadding = new Vector2(0f, 6f);
        list.EnableItemHighlight = true;
        list.OnClick = item => item.Activate();

        foreach (string name in Options)
        {
            string designName = name; // capture
            bool inFleet = Selected.Contains(designName);
            string marker = inFleet ? "[IN FLEET]" : "[  --  ]";
            string label = BudgetCredits > 0
                ? $"{marker} {designName}  ({CostOf(designName)} cr)"
                : $"{marker} {designName}";
            list.AddItem(new ArenaPopupListItem(label, () => Toggle_OnClick(designName),
                payload: designName));
        }

        UIList actions = AddList(new Vector2(c.X - 118, footerTop + 14), new Vector2(236, 40));
        actions.Direction = new Vector2(1f, 0f);
        actions.Padding = new Vector2(8f, 12f);
        actions.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPrimaryButton(actions, "DONE", Done_OnClick, 108f).Name = "arena_fleet_pick_done";
        ArenaTheme.AddPillButton(actions, "BACK", Back_OnClick, 108f).Name = "arena_fleet_pick_back";
    }

    string CountText()
    {
        if (BudgetCredits > 0)
            return $"{Selected.Count} selected  |  {SelectedCost()} / {BudgetCredits} cr";
        return $"{Selected.Count} selected  ({Options.Length} available)";
    }

    // Cost currency = IShipDesign.BaseStrength (rounded) — the SAME scalar the authoritative handshake gate
    // sums (ArenaMultiplayerSession.SumBundleCost) and the ONLY empire/pace-independent, deterministic design
    // value. Previously this client guard used BaseCost, which disagreed with the server gate: a fleet the
    // picker deemed affordable could still be REJECTED at the handshake for overspend. Now the friendly
    // client-side guard mirrors the real rejection (custom-fleet program §5.1).
    static int CostOf(string designName)
        => ResourceManager.Ships.GetDesign(designName, out IShipDesign design)
            ? (int)MathF.Round(design.BaseStrength)
            : 0;

    int SelectedCost() => Selected.Sum(CostOf);

    // Add/remove a design from the fielded set. In a budget-capped sandbox, an add that would
    // exceed the cap is denied. Returns whether the membership changed.
    bool Toggle(string designName)
    {
        if (Selected.Contains(designName))
        {
            Selected.Remove(designName);
            return true;
        }
        if (BudgetCredits > 0 && SelectedCost() + CostOf(designName) > BudgetCredits)
            return false; // over the cap
        Selected.Add(designName);
        return true;
    }

    void Toggle_OnClick(string designName)
    {
        if (Toggle(designName))
            GameAudio.AcceptClick();
        else
            GameAudio.NegativeClick(); // over the cap
        LoadContent(); // rebuild so markers + count reflect the new composition
    }

    string[] OrderedSelection()
        => Options.Where(Selected.Contains).ToArray();

    void CommitAndClose()
    {
        Commit?.Invoke(OrderedSelection());
        ExitScreen();
    }

    void Done_OnClick(UIButton button)
    {
        GameAudio.AffirmativeClick();
        CommitAndClose();
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
