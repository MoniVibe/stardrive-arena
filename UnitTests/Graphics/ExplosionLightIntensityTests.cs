using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;
using SynapseGaming.LightingSystem.Lights;

namespace UnitTests.Graphics;

// Big-battle regression: explosion lights decay 10/sec for the full
// 2.25s lifetime. Pre-fix this drove Intensity negative for the tail
// ~1s of a ship explosion, which LightingEffectBinder pushed to the
// shader as a subtractive DiffuseColor*Intensity — hulls inside the
// explosion radius rendered fully black until the explosion expired.
[TestClass]
public class ExplosionLightIntensityTests
{
    [TestMethod]
    public void TickLightIntensity_NeverGoesNegative_OverShipExplosionLifetime()
    {
        var light = new PointLight { Intensity = 12f, Enabled = true };
        const float dt = 1f / 60f;
        // 2.25s @ 60fps = 135 ticks; oversample to 200 to cover one
        // full ship explosion duration plus headroom.
        for (int i = 0; i < 200; ++i)
        {
            ExplosionManager.TickLightIntensity(light, dt);
            Assert.IsTrue(light.Intensity >= 0f,
                $"Intensity went negative on tick {i}: {light.Intensity}");
        }

        Assert.AreEqual(0f, light.Intensity, 0.0001f);
    }

    [TestMethod]
    public void TickLightIntensity_NullLight_DoesNotThrow()
    {
        ExplosionManager.TickLightIntensity(null, 1f / 60f);
    }

    [TestMethod]
    public void TickLightIntensity_DecaysAtExpectedRate()
    {
        var light = new PointLight { Intensity = 12f, Enabled = true };
        // 0.5s @ 60fps → expected intensity = 12 - 10*0.5 = 7
        const float dt = 1f / 60f;
        for (int i = 0; i < 30; ++i)
            ExplosionManager.TickLightIntensity(light, dt);

        Assert.AreEqual(7f, light.Intensity, 0.01f);
    }
}
