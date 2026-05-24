using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using Ship_Game.Data.Mesh;
using SunBurnLights = SynapseGaming.LightingSystem.Lights;
using SynapseGaming.LightingSystem.Core;
using SynapseGaming.LightingSystem.Effects.Forward;
using SynapseGaming.LightingSystem.Rendering;

namespace UnitTests.Graphics;

/// <summary>
/// Phase 2.8 sub-phase A5: assert that lights submitted to the SunBurn-stub
/// LightManager actually propagate onto the underlying BasicEffect's
/// DirectionalLight0/1/2 slots when LightingEffectBinder.Apply runs. This is
/// the data-flow assertion for the highest-risk part of the new renderer —
/// the new light → effect parameter binding.
/// </summary>
[TestClass]
public class LightingEffectBinderTests : StarDriveTest
{
    [TestMethod]
    public void SubmitDirectionalLight_PropagatesToBasicEffect()
    {
        var lightManager = new SunBurnLights.LightManager();
        var dir = new SunBurnLights.DirectionalLight
        {
            Name = "TestKey",
            Direction = new Vector3(0.5f, -0.5f, -0.5f),
            DiffuseColor = new Vector3(1f, 0.9f, 0.8f),
            Intensity = 1.5f,
            Enabled = true,
        };
        lightManager.Submit(dir);

        var env = new SceneEnvironment
        {
            AmbientLightColor = new Vector3(0.2f, 0.2f, 0.25f),
        };

        using var fx = new LightingEffect(Game.GraphicsDevice);
        LightingEffectBinder.Apply(fx, lightManager.ActiveLights, env, Vector3.Zero);

        Assert.IsTrue(fx.LightingEnabled, "LightingEnabled should be true once a directional light is submitted.");
        Assert.IsTrue(fx.DirectionalLight0.Enabled, "DirectionalLight0 should be enabled.");
        Vector3 expected = Vector3.Normalize(new Vector3(0.5f, -0.5f, -0.5f));
        Vector3 actual = fx.DirectionalLight0.Direction;
        Assert.AreEqual(expected.X, actual.X, 0.001f, "Direction.X mismatch");
        Assert.AreEqual(expected.Y, actual.Y, 0.001f, "Direction.Y mismatch");
        Assert.AreEqual(expected.Z, actual.Z, 0.001f, "Direction.Z mismatch");

        Vector3 expectedDiffuse = new Vector3(1f, 0.9f, 0.8f) * 1.5f;
        Assert.AreEqual(expectedDiffuse.X, fx.DirectionalLight0.DiffuseColor.X, 0.001f, "DiffuseColor.X mismatch");
        Assert.AreEqual(expectedDiffuse.Y, fx.DirectionalLight0.DiffuseColor.Y, 0.001f, "DiffuseColor.Y mismatch");
        Assert.AreEqual(expectedDiffuse.Z, fx.DirectionalLight0.DiffuseColor.Z, 0.001f, "DiffuseColor.Z mismatch");

        Assert.IsFalse(fx.DirectionalLight1.Enabled, "DirectionalLight1 should be disabled (only 1 light submitted).");
        Assert.IsFalse(fx.DirectionalLight2.Enabled, "DirectionalLight2 should be disabled (only 1 light submitted).");

        Assert.AreEqual(env.AmbientLightColor.X, fx.AmbientLightColor.X, 0.001f, "Ambient should match SceneEnvironment.AmbientLightColor.");
    }

    [TestMethod]
    public void NoLightsSubmitted_AmbientFloorApplied()
    {
        var lightManager = new SunBurnLights.LightManager();
        var env = new SceneEnvironment { AmbientLightColor = Vector3.Zero };
        using var fx = new LightingEffect(Game.GraphicsDevice);
        LightingEffectBinder.Apply(fx, lightManager.ActiveLights, env, Vector3.Zero);

        // §4.6 #12: MinAmbient floor (0.2) lifts zero ambient so hulls
        // never go fully black. LightingEnabled stays true as a result.
        Assert.IsTrue(fx.LightingEnabled, "Ambient floor should keep lighting enabled.");
        Assert.AreEqual(0.2f, fx.AmbientLightColor.X, 0.001f, "Ambient.X should be lifted to MinAmbient.");
        Assert.AreEqual(0.2f, fx.AmbientLightColor.Y, 0.001f, "Ambient.Y should be lifted to MinAmbient.");
        Assert.AreEqual(0.2f, fx.AmbientLightColor.Z, 0.001f, "Ambient.Z should be lifted to MinAmbient.");

        Assert.IsFalse(fx.DirectionalLight0.Enabled);
        Assert.IsFalse(fx.DirectionalLight1.Enabled);
        Assert.IsFalse(fx.DirectionalLight2.Enabled);
    }

    // Big-battle regression: warp/station explosion lights can carry
    // Radius >= 1000 and sit at the camera's XY. Without the ObjectType.Static
    // filter on sun candidacy they win bestSun, displace the actual system
    // sun from the 3 PointLight slots, and ships beyond the explosion radius
    // render with only ambient — i.e. nearly black.
    [TestMethod]
    public void DynamicPointLight_DoesNotDisplaceSystemSun()
    {
        var lightManager = new SunBurnLights.LightManager();

        // Real system sun far away in XY (camera is focused on the battle).
        var sun = new SunBurnLights.PointLight
        {
            Name = "Key",
            Position = new Vector3(100000f, 100000f, -50000f),
            Radius = 150000f,
            DiffuseColor = new Vector3(1f, 0.95f, 0.9f),
            Intensity = 1f,
            Enabled = true,
            ObjectType = ObjectType.Static,
        };
        lightManager.Submit(sun);

        // Warp explosion sitting right at the camera — Radius > 1000 means
        // it qualifies as a sun candidate by radius alone, and it's much
        // closer in XY than the real sun.
        var explosionSun = new SunBurnLights.PointLight
        {
            Name = "Warp Explosion",
            Position = new Vector3(0f, 0f, -100f),
            Radius = 1500f,
            DiffuseColor = new Vector3(0.9f, 0.8f, 0.7f),
            Intensity = 8f,
            Enabled = true,
            ObjectType = ObjectType.Dynamic,
        };
        lightManager.Submit(explosionSun);

        using var fx = new LightingEffect(Game.GraphicsDevice);
        LightingEffectBinder.Apply(fx, lightManager.ActiveLights, new SceneEnvironment(), Vector3.Zero);

        Assert.IsTrue(fx.PointLight0.Enabled, "System sun should fill PointLight0.");
        Assert.AreEqual(sun.Position.X, fx.PointLight0.Position.X, 0.01f,
            "PointLight0 should be the Static system sun, not the Dynamic explosion.");
        Assert.AreEqual(sun.Position.Y, fx.PointLight0.Position.Y, 0.01f);
    }
}
