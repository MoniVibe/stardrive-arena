using System;
using System.Linq;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SDGraphics;
using SDUtils;
using Ship_Game;

namespace UnitTests.NotificationTests
{
    [TestClass]
    public class TestNotifications : StarDriveTest
    {
        NotificationManager NotifMgr;

        public TestNotifications()
        {
            CreateUniverseAndPlayerEmpire();
            AddDummyPlanetToEmpire(new Vector2(2000), Player);
            NotifMgr = new NotificationManager(Universe.ScreenManager, Universe);
        }

        /// <summary>
        /// Add 12 notifications. 4 spy, 4 planet, 4, 4 spy
        /// </summary>
        /// <param name="empire"></param>
        public void AddNotifications(Empire empire)
        {
            NotifMgr.AddAgentResult(true, "AgentTest", empire);
            NotifMgr.AddAgentResult(true, "AgentTest", empire);
            NotifMgr.AddAgentResult(true, "AgentTest", empire);
            NotifMgr.AddAgentResult(true, "AgentTest", empire);

            var planet = empire.GetPlanets().First();
            NotifMgr.AddPlanetDiedNotification(planet);
            NotifMgr.AddPlanetDiedNotification(planet);
            NotifMgr.AddPlanetDiedNotification(planet);
            NotifMgr.AddPlanetDiedNotification(planet);

            NotifMgr.AddAgentResult(true, "AgentTest", empire);
            NotifMgr.AddAgentResult(true, "AgentTest", empire);
            NotifMgr.AddAgentResult(true, "AgentTest", empire);
            NotifMgr.AddAgentResult(true, "AgentTest", empire);
        }

        [TestMethod]
        public void TestRemoveTooManyNotifications()
        {
            NotifMgr.MaxEntriesToDisplay = 7;
            AddNotifications(Player);
            AssertEqual(12, NotifMgr.NumberOfNotifications);
            NotifMgr.Update(10f);
            AssertEqual(11, NotifMgr.NumberOfNotifications);
            NotifMgr.Update(10f);
            AssertEqual(10, NotifMgr.NumberOfNotifications);
            NotifMgr.Update(10f);
            NotifMgr.Update(10f);
            NotifMgr.Update(10f);
            AssertEqual(7, NotifMgr.NumberOfNotifications);
        }

        [TestMethod]
        public void BeingInvadedNotification_IsLocalOnlyForInvadedEmpire_NotInvader()
        {
            NotifMgr.AddBeingInvadedNotification(Player.GetPlanets().First().System, Player, Enemy, strRatio: 0.5f);

            var listField = typeof(NotificationManager).GetField("NotificationList",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var notifications = (Array<Notification>)listField.GetValue(NotifMgr);
            Notification notification = notifications.First();

            Assert.IsTrue(notification.LocalEmpireOnly,
                "Invasion warnings must be local-only in authoritative multiplayer.");
            Assert.AreEqual(Player, notification.RelevantEmpire,
                "The warning belongs to the invaded empire; tagging the invader leaks host warnings to joiners.");
            Assert.AreNotEqual(Enemy, notification.RelevantEmpire);
        }
    }
}
