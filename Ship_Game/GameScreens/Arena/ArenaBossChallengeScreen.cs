using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using SDUtils;
using Ship_Game.Audio;
using Ship_Game.Ships;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaBossChallengeOption
{
    public readonly string ContenderName;
    public readonly string DesignName;
    public readonly string RoleClass;
    public readonly string ThreatBand;
    public readonly int Rating;
    public readonly int CombatTier;
    public readonly float BaseStrength;
    public readonly float StrengthRatio;
    public readonly bool MirrorsPlayerDesign;

    public ArenaBossChallengeOption(ContenderRecord contender, IShipDesign design,
        float playerStrength, bool mirrorsPlayerDesign = false)
    {
        ContenderName = contender?.Name ?? "";
        DesignName    = design?.Name ?? contender?.DesignName ?? "";
        RoleClass     = contender?.RoleClass.NotEmpty() == true
            ? contender.RoleClass
            : (design?.Role.ToString().ToUpperInvariant() ?? "");
        Rating        = contender?.Rating ?? 0;
        CombatTier    = ArenaFightScreen.CombatTierForDesign(design);
        BaseStrength  = Math.Max(1f, design?.BaseStrength ?? 1f);
        StrengthRatio = BaseStrength / Math.Max(1f, playerStrength);
        ThreatBand    = ThreatBandForRatio(StrengthRatio);
        MirrorsPlayerDesign = mirrorsPlayerDesign;
    }

    public string Signature
        => string.Join("|",
            ThreatBand,
            StrengthRatio.ToString("0.000", CultureInfo.InvariantCulture),
            Rating.ToString(CultureInfo.InvariantCulture),
            ContenderName,
            DesignName);

    public static string ThreatBandForRatio(float ratio)
    {
        if (ratio < 0.75f) return "WARMUP";
        if (ratio < 0.95f) return "EDGE";
        if (ratio < 1.15f) return "EVEN";
        if (ratio < 1.45f) return "THREAT";
        return "BOSS";
    }
}

public static class ArenaBossChallengeOptions
{
    public const int DefaultCount = 6;

    static readonly float[] TargetRatios =
    {
        0.65f, 0.85f, 1.00f, 1.15f, 1.35f, 1.65f,
    };

    public static ArenaBossChallengeOption[] Generate(ArenaCareer career, int careerLevel,
        int count = DefaultCount)
    {
        if (career == null || count <= 0)
            return Empty<ArenaBossChallengeOption>.Array;

        career.NormalizeForPersistence();
        CareerLadder.EnsureContenders(career);

        float playerStrength = ArenaFightOptions.PlayerStrengthForCareer(career, careerLevel);
        var playerDesignNames = new HashSet<string>(
            ArenaFightOptions.PlayerDesignNamesForCareer(career, careerLevel),
            StringComparer.Ordinal);
        var candidates = new List<ArenaBossChallengeOption>();
        foreach (ContenderRecord contender in career.Contenders ?? Empty<ContenderRecord>.Array)
        {
            if (contender == null || contender.Name.IsEmpty() || contender.DesignName.IsEmpty())
                continue;
            if (!ResourceManager.Ships.GetDesign(contender.DesignName, out IShipDesign design)
                || !ArenaFightScreen.IsLegalCombatCraft(design)
                || !ArenaFightScreen.IsDesignAllowedForCareerLevel(design, careerLevel))
                continue;
            candidates.Add(new ArenaBossChallengeOption(contender, design, playerStrength,
                playerDesignNames.Contains(design.Name)));
        }

        List<ArenaBossChallengeOption> primary = candidates
            .Where(o => !o.MirrorsPlayerDesign)
            .ToList();
        if (primary.Count == 0)
            primary = candidates;

        var selected = PickByRelativeTargets(primary, count);
        if (selected.Count < count && primary.Count != candidates.Count)
        {
            var used = new HashSet<string>(selected.Select(o => o.ContenderName), StringComparer.Ordinal);
            List<ArenaBossChallengeOption> mirrorFill = candidates
                .Where(o => o.MirrorsPlayerDesign && !used.Contains(o.ContenderName))
                .ToList();
            selected.AddRange(PickByRelativeTargets(mirrorFill, count - selected.Count));
        }

        return SortForDisplay(selected);
    }

    static List<ArenaBossChallengeOption> PickByRelativeTargets(List<ArenaBossChallengeOption> candidates, int count)
    {
        if (candidates == null || candidates.Count == 0 || count <= 0)
            return new List<ArenaBossChallengeOption>();

        if (candidates.Count <= count)
            return SortForDisplay(candidates).ToList();

        var selected = new List<ArenaBossChallengeOption>(count);
        var used = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < count; ++i)
        {
            float target = i < TargetRatios.Length
                ? TargetRatios[i]
                : TargetRatios[TargetRatios.Length - 1] + (i - TargetRatios.Length + 1) * 0.25f;
            ArenaBossChallengeOption pick = candidates
                .Where(o => !used.Contains(o.ContenderName))
                .OrderBy(o => Math.Abs(o.StrengthRatio - target))
                .ThenByDescending(o => o.Rating)
                .ThenBy(o => o.ContenderName, StringComparer.Ordinal)
                .FirstOrDefault();
            if (pick == null)
                break;
            selected.Add(pick);
            used.Add(pick.ContenderName);
        }

        return selected;
    }

    static ArenaBossChallengeOption[] SortForDisplay(IEnumerable<ArenaBossChallengeOption> options)
        => (options ?? Empty<ArenaBossChallengeOption>.Array)
            .OrderBy(o => o.StrengthRatio)
            .ThenByDescending(o => o.Rating)
            .ThenBy(o => o.ContenderName, StringComparer.Ordinal)
            .ToArray();
}

public sealed class ArenaBossChallengeScreen : GameScreen
{
    readonly ArenaFightScreen Arena;
    ArenaBossChallengeOption[] Options;

    public ArenaBossChallengeScreen(ArenaFightScreen arena) : base(arena, toPause: arena)
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
        Options = Arena?.GenerateCurrentBossChallengeOptions() ?? Empty<ArenaBossChallengeOption>.Array;

        Vector2 c = ScreenCenter;
        var panel = new RectF(c.X - 340, c.Y - 250, 680, 500);
        Add(ArenaTheme.Panel(panel));

        Add(ArenaTheme.ScreenTitle(new Vector2(panel.X + 24, panel.Y + 18),
            "BOSS CHALLENGES"));
        Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 50),
            "Pick a ladder contender scaled against your current fleet.",
            ArenaTheme.BodyFont, ArenaTheme.TextSecondary));

        float footerTop = panel.Bottom - 72f;
        var listRect = new RectF(panel.X + 24, panel.Y + 86, panel.W - 48, footerTop - panel.Y - 98);
        var list = Add(new ScrollList<ArenaPopupListItem>(listRect, 34));
        list.ItemPadding = new Vector2(0f, 6f);
        list.EnableItemHighlight = true;
        list.OnClick = item => item.Activate();

        if (Options.Length == 0)
        {
            list.AddItem(new ArenaPopupListItem("No ladder contenders are legal at this career tier.",
                font: ArenaTheme.BodyFont, textColor: ArenaTheme.TextMuted));
        }
        else
        {
            foreach (ArenaBossChallengeOption option in Options)
                AddOptionRow(list, option);
        }

        UIList actions = AddList(new Vector2(c.X - 48, footerTop + 14), new Vector2(96, 40));
        actions.Padding = new Vector2(8f, 2f);
        actions.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(actions, "BACK", Back_OnClick, 90f);
    }

    void AddOptionRow(ScrollList<ArenaPopupListItem> list, ArenaBossChallengeOption option)
    {
        string ratio = option.StrengthRatio.ToString("0.00", CultureInfo.InvariantCulture);
        string label = $"{option.ThreatBand,-6}  {ratio}x  T{option.CombatTier}  {option.RoleClass,-10}  " +
                       $"{option.ContenderName}  R{option.Rating}";
        string tooltip = $"{option.DesignName}; base strength {option.BaseStrength:0}; " +
                         $"{option.StrengthRatio:0.00}x current fielded strength.";
        list.AddItem(new ArenaPopupListItem(label, () => Pick(option), tooltip,
            textColor: TextColorForThreat(option.ThreatBand), payload: option));
    }

    static Microsoft.Xna.Framework.Color TextColorForThreat(string band)
    {
        if (string.Equals(band, "WARMUP", StringComparison.Ordinal)) return ArenaTheme.RiskSafe;
        if (string.Equals(band, "EDGE", StringComparison.Ordinal)) return ArenaTheme.TextSecondary;
        if (string.Equals(band, "EVEN", StringComparison.Ordinal)) return ArenaTheme.RiskStandard;
        if (string.Equals(band, "THREAT", StringComparison.Ordinal)) return ArenaTheme.RiskRisky;
        return ArenaTheme.RiskElite;
    }

    void Pick(ArenaBossChallengeOption option)
    {
        if (Arena != null && option != null && Arena.ChallengeContender(option.ContenderName))
        {
            GameAudio.AffirmativeClick();
            Arena.StartBout();
        }
        else
        {
            GameAudio.NegativeClick();
        }
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
