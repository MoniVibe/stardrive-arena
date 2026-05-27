using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game;
using XnaColor = Microsoft.Xna.Framework.Color;

namespace UnitTests.Graphics;

/// <summary>
/// Regression guard for the FillRectangle(RectF)/Draw(Texture2D, RectF, ...)
/// path. A pre-fix bug had `static readonly XnaRect? NullRectangle = new();`
/// resolve to HasValue=true with a (0,0,0,0) Rectangle (target-typed new on
/// Nullable<Rectangle> constructs the underlying type), which fed a 0-sized
/// srcRect to SpriteBatch.Draw and divided by zero in the scale math.
/// Symptom: every batch.FillRectangle(RectF) call rendered nothing.
///
/// This test paints a known rect through both the FillRectangle(RectF) and
/// Draw(Texture2D, RectF) overloads and asserts pixels actually got written.
/// If a future change reintroduces a zero source rectangle (or otherwise
/// breaks the RectF path), the assertions catch it before it ships.
/// </summary>
[TestClass]
public class FillRectangleRectFTests : StarDriveTest
{
    [TestMethod]
    public void FillRectangle_RectF_WritesPixels()
    {
        GraphicsDevice device = Game.GraphicsDevice;

        const int W = 32, H = 32;
        const int X = 8,  Y = 8;
        const int RW = 16, RH = 16;

        using var rt = new RenderTarget2D(device, W, H, mipMap: false,
                                          SurfaceFormat.Color, DepthFormat.None);

        RenderTargetBinding[] prev = device.GetRenderTargets();
        try
        {
            device.SetRenderTarget(rt);
            device.Clear(XnaColor.Black);

            using var batch = new SpriteBatch(device);
            batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque);
            batch.FillRectangle(new RectF(X, Y, RW, RH), XnaColor.Red);
            batch.End();
        }
        finally
        {
            device.SetRenderTargets(prev);
        }

        var pixels = new XnaColor[W * H];
        rt.GetData(pixels);

        // Inside the rect: every pixel should be the fill color.
        int redHits = 0;
        for (int y = Y; y < Y + RH; y++)
            for (int x = X; x < X + RW; x++)
                if (pixels[y * W + x].R > 200 && pixels[y * W + x].G < 20 && pixels[y * W + x].B < 20)
                    redHits++;
        Assert.AreEqual(RW * RH, redHits,
            $"FillRectangle(RectF) wrote {redHits}/{RW * RH} red pixels. " +
            "The RectF overload likely went through a zero source rectangle path " +
            "and rendered nothing (see project_rectf_fillrectangle_broken).");

        // Outside the rect: pixels should remain black (no overflow).
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                bool inside = x >= X && x < X + RW && y >= Y && y < Y + RH;
                if (inside) continue;
                XnaColor c = pixels[y * W + x];
                Assert.IsTrue(c.R == 0 && c.G == 0 && c.B == 0,
                    $"Expected black outside rect at ({x},{y}), got {c}.");
            }
    }

    [TestMethod]
    public void DrawTexture2D_RectF_WritesPixels()
    {
        GraphicsDevice device = Game.GraphicsDevice;

        const int W = 32, H = 32;
        const int X = 4,  Y = 4;
        const int RW = 8, RH = 8;

        using var tex = new Texture2D(device, 1, 1);
        tex.SetData(new[] { XnaColor.White });

        using var rt = new RenderTarget2D(device, W, H, mipMap: false,
                                          SurfaceFormat.Color, DepthFormat.None);

        RenderTargetBinding[] prev = device.GetRenderTargets();
        try
        {
            device.SetRenderTarget(rt);
            device.Clear(XnaColor.Black);

            using var batch = new SpriteBatch(device);
            batch.Begin(SpriteSortMode.Deferred, BlendState.Opaque);
            // Call SpriteExtensions.Draw explicitly: an in-RectF arg would
            // otherwise resolve to MonoGame's batch.Draw(Texture2D, Rectangle,
            // Color) via RectF's implicit conversion, bypassing the InternalDraw
            // path we're trying to guard.
            SpriteExtensions.Draw(batch, tex, new RectF(X, Y, RW, RH), XnaColor.Lime);
            batch.End();
        }
        finally
        {
            device.SetRenderTargets(prev);
        }

        var pixels = new XnaColor[W * H];
        rt.GetData(pixels);

        int limeHits = 0;
        for (int y = Y; y < Y + RH; y++)
            for (int x = X; x < X + RW; x++)
                if (pixels[y * W + x].G > 200 && pixels[y * W + x].R < 20 && pixels[y * W + x].B < 20)
                    limeHits++;
        Assert.AreEqual(RW * RH, limeHits,
            $"Draw(Texture2D, RectF, Color) wrote {limeHits}/{RW * RH} lime pixels. " +
            "InternalDraw's RectF path is broken — likely a zero source rectangle " +
            "is being passed to SpriteBatch.Draw.");
    }
}
