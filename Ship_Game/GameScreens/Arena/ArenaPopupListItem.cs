using System;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using SDUtils;
using Ship_Game.Graphics;
using Vector2 = SDGraphics.Vector2;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaPopupListItem : ScrollListItem<ArenaPopupListItem>
{
    readonly string Text;
    readonly string TooltipText;
    readonly Action Click;
    readonly Font Font;
    readonly Color TextColor;
    readonly bool IsAction;

    public override int ItemHeight => 34;
    public string LabelText => Text;
    public object Payload { get; }

    public ArenaPopupListItem(string text, Action click = null, string tooltip = null,
        Font font = null, Color? textColor = null, object payload = null)
    {
        Text = text ?? "";
        TooltipText = tooltip ?? "";
        Click = click;
        Font = font ?? ArenaTheme.BodySmallFont;
        TextColor = textColor ?? ArenaTheme.TextPrimary;
        IsAction = click != null;
        Payload = payload;
    }

    public void Activate() => Click?.Invoke();

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        var r = new RectF(X, Y, Width, Height - 2);
        Color fill = IsAction
            ? (Hovered ? ArenaTheme.ButtonHover : ArenaTheme.ButtonFill)
            : ArenaTheme.SurfaceSelected;
        batch.FillRectangle(r, fill);
        batch.DrawRectangle(r, Hovered ? ArenaTheme.BorderStrong : ArenaTheme.BorderSubtle);

        string text = FitText(Font, Text, r.W - 20f);
        var pos = new Vector2(r.X + 10f, r.Y + (r.H - Font.LineSpacing) / 2f);
        batch.DrawString(Font, text, pos, IsAction && Hovered ? ArenaTheme.TextPrimary : TextColor);

        if (Hovered && TooltipText.NotEmpty())
            ToolTip.CreateTooltip(TooltipText);
    }

    static string FitText(Font font, string text, float maxWidth)
    {
        if (text.IsEmpty() || font.MeasureString(text).X <= maxWidth)
            return text;

        const string ellipsis = "...";
        int keep = text.Length;
        while (keep > 0)
        {
            string candidate = text.Substring(0, keep).TrimEnd() + ellipsis;
            if (font.MeasureString(candidate).X <= maxWidth)
                return candidate;
            --keep;
        }
        return ellipsis;
    }
}
