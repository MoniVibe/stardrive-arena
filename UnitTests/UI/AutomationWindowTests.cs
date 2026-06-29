using Microsoft.VisualStudio.TestTools.UnitTesting;
using Ship_Game;

namespace UnitTests.UI
{
    [TestClass]
    public class AutomationWindowTests : StarDriveTest
    {
        [TestMethod]
        public void AutomationWindow_DefaultPlacementAvoidsRightHudReserve()
        {
            const int screenWidth = 1920;
            const int screenHeight = 1080;
            const int rightHudReserve = 345;

            var rect = AutomationWindow.DefaultWindowRect(screenWidth, screenHeight);

            Assert.IsTrue(rect.X >= 15, "Automation window should stay inside the left screen edge.");
            Assert.IsTrue(rect.Y >= 15, "Automation window should stay inside the top screen edge.");
            Assert.IsTrue(rect.X + rect.Width <= screenWidth - rightHudReserve,
                "Default automation window placement should avoid the minimap/right-HUD column.");
            Assert.IsTrue(rect.Y + rect.Height <= screenHeight - 15,
                "Automation window should stay inside the bottom screen edge.");
        }

        [TestMethod]
        public void AutomationWindow_ClampKeepsDraggedWindowOnScreen()
        {
            var rect = AutomationWindow.ClampWindowRect(new SDGraphics.Rectangle(4000, -300, 220, 710), 1920, 1080);

            Assert.AreEqual(1685, rect.X);
            Assert.AreEqual(15, rect.Y);
            Assert.AreEqual(220, rect.Width);
            Assert.AreEqual(710, rect.Height);
        }
    }
}
