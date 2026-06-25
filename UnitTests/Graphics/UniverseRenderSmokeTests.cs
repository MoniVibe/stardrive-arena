using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game;
using Ship_Game.Data;
using Ship_Game.Data.Texture;
using Ship_Game.Ships;
using SynapseGaming.LightingSystem.Core;
using SynapseGaming.LightingSystem.Lights;
using SynapseGaming.LightingSystem.Rendering;
using XnaMatrix = Microsoft.Xna.Framework.Matrix;
using XnaVector3 = Microsoft.Xna.Framework.Vector3;
using SdMatrix = SDGraphics.Matrix;

namespace UnitTests.Graphics;

/// <summary>
/// DECISIVE FEASIBILITY SPIKE: prove StarDrive's REAL 3D view can be rendered
/// HEADLESS from the unit-test harness. We spin up a developer-sandbox universe
/// (which gives us lights + a scene Environment), manually load ONE warship hull
/// 3D mesh into a SceneObject, register it with ScreenManager, and render it to a
/// 512x512 RenderTarget2D using the same render sequence the game's ScreenManager
/// uses. Then we assert the frame is NON-BLACK (a recognizable ship covered pixels)
/// and save it as a PNG for human review.
///
/// This settles whether a real-3D battle clip is later feasible. We only need ONE
/// good frame here.
/// </summary>
[TestClass]
public class UniverseRenderSmokeTests : StarDriveTest
{
    const int RT = 512;

    [TestMethod]
    public void RenderShip3D_ToPng_NonBlack()
    {
        // Developer sandbox => Player+Enemy, ships, AND Universe.LoadContent()
        // which runs ResetLighting() (Global Fill/Back + per-system lights) and
        // sets up the scene Environment. Lights + Environment must exist for the
        // mesh's LightingEffect to produce non-black output.
        CreateDeveloperSandboxUniverse("Human", numOpponents: 1, paused: true);

        GraphicsDevice device = Game.GraphicsDevice;

        // Pick a substantial warship design, deterministically. Iterate from the
        // priciest down until one whose hull mesh actually loads headless.
        IShipDesign[] candidates = ResourceManager.Ships.Designs
            .Where(d => d.BaseStrength > 100f)
            .OrderByDescending(d => d.GetCost(Player))
            .ThenBy(d => d.Name, StringComparer.Ordinal)
            .ToArray();

        Console.WriteLine($"[spike] candidate warship designs (BaseStrength>100): {candidates.Length}");

        ShipHull loadedHull = null;
        SceneObject so = null;
        string loadedName = null;
        foreach (IShipDesign design in candidates)
        {
            ShipHull hull = design.BaseHull;
            if (hull == null)
                continue;
            try
            {
                if (hull.LoadModel(out SceneObject candidateSO, Content) && candidateSO != null)
                {
                    loadedHull = hull;
                    so = candidateSO;
                    loadedName = design.Name;
                    break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"[spike] LoadModel threw for '{design.Name}': {e.Message}");
            }
        }

        Assert.IsNotNull(so, "Could not LoadModel any warship hull headless. SUSPECT 1: zero scene objects.");
        Console.WriteLine($"[spike] loaded hull for design '{loadedName}' modelPath='{loadedHull.ModelPath}'");
        Console.WriteLine($"[spike] hull.Volume={loadedHull.Volume}  hull.ModelZ={loadedHull.ModelZ}  MeshOffset={loadedHull.MeshOffset}");

        // Place model at origin (centered using MeshOffset like the game does),
        // facing the camera (top-down), and mark it Rendered so the render manager
        // submits it.
        so.World = XnaMatrix.CreateTranslation(new XnaVector3(loadedHull.MeshOffset.X, loadedHull.MeshOffset.Y, 0f));
        so.Visibility = ObjectVisibility.Rendered;
        ScreenManager.Instance.AddObject(so);

        Console.WriteLine($"[spike] SceneObject visibility={so.Visibility}");

        // Camera: top-down like the game. Frame the model using its Volume.
        float modelSpan = Math.Max(loadedHull.Volume.X, loadedHull.Volume.Y);
        if (modelSpan <= 1f) modelSpan = 512f; // safety fallback
        float camHeight = modelSpan * 2.5f;

        Console.WriteLine($"[spike] modelSpan={modelSpan}  camHeight={camHeight}");

        // The developer-sandbox per-system Key lights sit at the solar systems
        // (far from our origin-placed hull), so only the dim Global Fill/Back +
        // 0.06 ambient reach it => a dark frame. Add a bright local preview light
        // above the ship so the frame is vivid. This also de-risks the "lighting"
        // suspect for a future clip: we CAN drive scene brightness from the test.
        var previewLight = new PointLight
        {
            Name            = "Spike Preview Light",
            DiffuseColor    = Color.White.ToVector3(),
            Intensity       = 2.0f,
            ObjectType      = ObjectType.Static,
            FillLight       = false,
            Radius          = camHeight * 8f,
            Position        = new XnaVector3(0, 0, camHeight * 0.8f),
            Enabled         = true,
            FalloffStrength = 1f,
        };
        previewLight.World = XnaMatrix.CreateTranslation(previewLight.Position);
        ScreenManager.Instance.AddLight(previewLight, dynamic: false);

        // View: mirror the game's top-down camera (looks straight DOWN -Z).
        SdMatrix view = Matrices.CreateLookAtDown(0, 0, -camHeight);
        // Projection: square aspect (1.0) to match the square RT, near=1, far well past the camera.
        SdMatrix proj = (SdMatrix)XnaMatrix.CreatePerspectiveFieldOfView(
            MathHelper.PiOver4, 1f, 1f, camHeight * 10f);

        int nonBlack = RenderToRtAndCount(device, so, view, proj, out Color[] pixels);
        float frac = nonBlack / (float)(RT * RT);
        Console.WriteLine($"[spike] PASS-1 nonBlackFraction={frac:P2} ({nonBlack}/{RT * RT})");

        // Save the PNG regardless, for human review.
        string dir = Path.Combine(Directory.GetCurrentDirectory(), "battle-replays");
        Directory.CreateDirectory(dir);
        string png = Path.Combine(dir, "ship3d_smoke.png");
        ImageUtils.SaveAsPng(png, RT, RT, pixels);
        Console.WriteLine($"[spike] PNG saved: {png}");

        Assert.IsTrue(frac > 0.01f,
            $"Expected >1% non-black pixels, got {frac:P2}. PNG at {png}. " +
            "Walk the 3 suspects: (1) zero SOs rendered, (2) camera not framing, (3) lighting not bound.");
    }

    int RenderToRtAndCount(GraphicsDevice device, SceneObject so,
                           SdMatrix view, SdMatrix proj, out Color[] pixels)
    {
        using var rt = new RenderTarget2D(device, RT, RT, mipMap: false,
            SurfaceFormat.Color, DepthFormat.Depth24);

        var dt = new DrawTimes();

        RenderTargetBinding[] prev = device.GetRenderTargets();
        BlendState prevBlend = device.BlendState;
        DepthStencilState prevDepth = device.DepthStencilState;
        RasterizerState prevRaster = device.RasterizerState;

        try
        {
            device.SetRenderTarget(rt);
            device.Clear(Color.Black);
            device.BlendState = BlendState.Opaque;
            device.DepthStencilState = DepthStencilState.Default;
            device.RasterizerState = RasterizerState.CullCounterClockwise;

            ScreenManager.Instance.BeginFrameRendering(dt, ref view, ref proj);
            ScreenManager.Instance.RenderSceneObjects();
            ScreenManager.Instance.EndFrameRendering();
        }
        finally
        {
            device.SetRenderTargets(prev);
            device.BlendState = prevBlend;
            device.DepthStencilState = prevDepth;
            device.RasterizerState = prevRaster;
        }

        pixels = new Color[RT * RT];
        rt.GetData(pixels);

        int nonBlack = 0;
        foreach (Color px in pixels)
            if (px.R + px.G + px.B > 24) ++nonBlack;
        return nonBlack;
    }
}
