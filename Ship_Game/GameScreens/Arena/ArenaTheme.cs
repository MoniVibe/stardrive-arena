using System;
using Microsoft.Xna.Framework;
using SDGraphics;
using Ship_Game.Graphics;
using Vector2 = SDGraphics.Vector2;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// Shared Star Gladiator visual tokens, copied from the Figma "UI God Page" token board.
/// Font mappings use StarDrive's existing content fonts: Orbitron -> Pirulen,
/// Rajdhani -> Arial, JetBrains Mono -> Visitor/Consolas.
/// </summary>
public static class ArenaTheme
{
    public static readonly Color VoidBlack = new(5, 7, 11);
    public static readonly Color Graphite = new(12, 16, 22);
    public static readonly Color SurfacePanel = new(20, 26, 34, 245);
    public static readonly Color SurfaceRaised = new(30, 38, 48, 238);
    public static readonly Color SurfaceSelected = new(31, 42, 54, 245);
    public static readonly Color Ash = new(91, 102, 117);
    public static readonly Color TextPrimary = new(234, 241, 251);
    public static readonly Color TextSecondary = new(202, 213, 228);
    public static readonly Color TextMuted = new(148, 160, 178);
    public static readonly Color Amber = new(216, 163, 58);
    public static readonly Color AmberBright = new(240, 180, 41);
    public static readonly Color Cyan = new(52, 214, 232);
    public static readonly Color Blue = new(59, 130, 246);
    public static readonly Color Violet = new(139, 92, 246);
    public static readonly Color Magenta = new(232, 54, 158);
    public static readonly Color Orange = new(249, 115, 22);
    public static readonly Color Red = new(239, 68, 68);
    public static readonly Color Green = new(52, 211, 153);
    public static readonly Color BorderSubtle = new(216, 163, 58, 38);
    public static readonly Color BorderStrong = new(216, 163, 58, 120);
    public static readonly Color ButtonFill = new(31, 42, 54, 245);
    public static readonly Color ButtonHover = new(52, 64, 78, 250);

    public static Color RiskSafe => Green;
    public static Color RiskStandard => Cyan;
    public static Color RiskRisky => Orange;
    public static Color RiskElite => AmberBright;
    public static Color Tier1 => Cyan;
    public static Color Tier2 => Blue;
    public static Color Tier3 => AmberBright;

    public static Font ArenaTitleFont => Fonts.Pirulen20;
    public static Font BossTitleFont => Fonts.Pirulen20;
    public static Font ScreenTitleFont => Fonts.Pirulen16;
    public static Font PanelTitleFont => Fonts.Pirulen12;
    public static Font BodyFont => Fonts.Arial14Bold;
    public static Font BodySmallFont => Fonts.Arial12;
    public static Font LabelFont => Fonts.Arial12Bold;
    public static Font MonoFont => Fonts.Visitor10;
    public static Font NumberLargeFont => Fonts.Consolas18;
    public static Font NumberMediumFont => Fonts.Consolas18;

    public static Menu2 Panel(in RectF rect)
        => new(rect, SurfacePanel) { Border = BorderSubtle };

    public static UIPanel Card(in RectF rect)
        => new(rect, SurfaceRaised) { Border = BorderSubtle };

    public static UILabel ScreenTitle(Vector2 pos, string text)
        => new(pos, text, ScreenTitleFont, TextPrimary) { DropShadow = true };

    public static UILabel ArenaTitle(Vector2 pos, string text)
        => new(pos, text, ArenaTitleFont, TextPrimary) { DropShadow = true };

    public static UILabel SectionHeader(Vector2 pos, string text)
        => new(pos, text, PanelTitleFont, Amber);

    public static UILabel Body(Vector2 pos, string text)
        => new(pos, text, BodyFont, TextSecondary);

    public static UILabel Small(Vector2 pos, string text)
        => new(pos, text, BodySmallFont, TextMuted);

    public static UILabel Label(Vector2 pos, string text)
        => new(pos, text, LabelFont, TextMuted);

    public static UIButton PillButton(string text, System.Action<UIButton> click, float width = 156f, float height = 30f)
    {
        UIButton button = new(PillStyle(), new Vector2(width, height), text)
        {
            Font = LabelFont,
            OnClick = click,
            TextShadows = false,
        };
        return button;
    }

    public static UIButton PrimaryPillButton(string text, System.Action<UIButton> click, float width = 156f, float height = 30f)
    {
        UIButton button = new(PrimaryStyle(), new Vector2(width, height), text)
        {
            Font = LabelFont,
            OnClick = click,
            TextShadows = false,
        };
        return button;
    }

    public static UIButton AddPillButton(UIList list, string text, System.Action<UIButton> click, float width = 180f, float height = 30f)
        => list.Add(PillButton(text, click, width, height));

    public static UIButton AddPrimaryButton(UIList list, string text, System.Action<UIButton> click, float width = 180f, float height = 30f)
        => list.Add(PrimaryPillButton(text, click, width, height));

    public static UIPanel StatChip(in RectF rect, string label, string value, Color? accent = null)
    {
        Color chipAccent = accent ?? Amber;
        UIPanel chip = Card(rect);
        chip.Border = chipAccent;
        chip.Add(new UILabel(new Vector2(rect.X + 10, rect.Y + 6), label, LabelFont, TextMuted));
        chip.Add(new UILabel(new Vector2(rect.X + 10, rect.Y + 20), value, NumberMediumFont, TextPrimary));
        return chip;
    }

    public static UIPanel StatChip(in RectF rect, string label, Func<string> value, Color? accent = null)
    {
        Color chipAccent = accent ?? Amber;
        UIPanel chip = Card(rect);
        chip.Border = chipAccent;
        chip.Add(new UILabel(new Vector2(rect.X + 10, rect.Y + 6), label, LabelFont, TextMuted));
        chip.Add(new UILabel(new Vector2(rect.X + 10, rect.Y + 20),
            value?.Invoke() ?? "", NumberMediumFont, TextPrimary)
        {
            DynamicText = _ => value?.Invoke() ?? "",
        });
        return chip;
    }

    public static Color RiskColor(FightRiskTier risk) => risk switch
    {
        FightRiskTier.Safe     => RiskSafe,
        FightRiskTier.Standard => RiskStandard,
        FightRiskTier.Risky    => RiskRisky,
        FightRiskTier.Elite    => RiskElite,
        _                      => TextSecondary,
    };

    static UIButton.StyleTextures PillStyle() => new()
    {
        DefaultColor = ButtonFill,
        HoverColor = ButtonHover,
        PressColor = Amber,
        DefaultTextColor = TextPrimary,
        HoverTextColor = TextPrimary,
        PressTextColor = VoidBlack,
        DrawBackground = true,
    };

    static UIButton.StyleTextures PrimaryStyle() => new()
    {
        DefaultColor = Amber,
        HoverColor = AmberBright,
        PressColor = Cyan,
        DefaultTextColor = VoidBlack,
        HoverTextColor = VoidBlack,
        PressTextColor = VoidBlack,
        DrawBackground = true,
    };
}
