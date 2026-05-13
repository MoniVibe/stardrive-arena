using System;
using System.Diagnostics;
using Microsoft.Xna.Framework.Graphics;
using Color = Microsoft.Xna.Framework.Color;
using SDGraphics;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.GameScreens.MainMenu
{
    // Main-menu popup that explains BlackBox and links to Ko-fi.
    // Uses the standard PopupWindow chrome (same as InGameWiki) — title bar
    // with built-in Close X, middle blurb, body paragraph, and a single
    // affirmative button that opens the URL in the default browser via
    // shell-exec.
    public sealed class SupportBlackbox : PopupWindow
    {
        const string KofiUrl = "https://ko-fi.com/teamstardrive";

        const string BodyText =
            "StarDrive BlackBox is a community-driven revival of StarDrive, " +
            "rebuilt and maintained by a small volunteer team in their spare " +
            "time.\n\n" +
            "If you've enjoyed playing and want to support continued " +
            "development, you can contribute via Ko-fi. Every bit helps keep " +
            "the lights on.";

        string WrappedBody;

        public SupportBlackbox(GameScreen parent) : base(parent, width: 520, height: 360)
        {
            IsPopup           = true;
            TransitionOnTime  = 0.25f;
            TransitionOffTime = 0.25f;

            TitleText  = "Support BlackBox";
            MiddleText = "Help keep StarDrive BlackBox alive";
        }

        public override void LoadContent()
        {
            base.LoadContent();

            // Wrap to the popup body width minus a small inset on each side.
            float wrapWidth = BottomBigFill.Width - 24f;
            WrappedBody = Fonts.Arial14Bold.ParseText(BodyText, wrapWidth);

            // Bottom-center the affirmative button inside the body area.
            // Military style is texture-driven, ~168px wide.
            float btnX = BottomBigFill.CenterX() - 84;
            float btnY = BottomBigFill.Bottom - 30;
            Button(ButtonStyle.Military, btnX, btnY, "Support", _ => OpenKofi());
        }

        public override void Draw(SpriteBatch batch, DrawTimes elapsed)
        {
            ScreenManager.FadeBackBufferToBlack(TransitionAlpha * 2 / 3);
            base.Draw(batch, elapsed);

            batch.SafeBegin();
            batch.DrawString(Fonts.Arial14Bold, WrappedBody, BodyTextStart, Color.White);
            batch.SafeEnd();
        }

        static void OpenKofi()
        {
            try
            {
                Process.Start(new ProcessStartInfo(KofiUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Warning($"SupportBlackbox: failed to open '{KofiUrl}': {ex.Message}");
            }
        }
    }
}
