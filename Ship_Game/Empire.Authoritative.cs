using System.Linq;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.Ships;

namespace Ship_Game
{
    public sealed partial class Empire
    {
        public bool IsHumanControlled => AuthoritativeHumanPlayers.IsHumanControlled(this);
        public bool IsAIControlled => !IsHumanControlled || AISidekickEnabled;

        // Hand this empire fully over to the AI (every slice). For per-slice control, set the individual
        // Auto* flags instead (e.g. AutoMilitary / AutoSpy / AutoResearch) and leave AISidekickEnabled off.
        public void EnableAISidekick(bool enableOracle = false)
        {
            AISidekickEnabled           = true;
            if (enableOracle && isPlayer) OracleSidekickEnabled = true; // player-only cheat layer (opt-in)
            AutoResearch                = true;
            AutoColonize                = true;
            AutoExplore                 = true;
            AutoTaxes                   = true;
            AutoMilitary                = true;
            AutoSpy                     = true;
            AutoBuildSpaceRoads         = true;
            AutoBuildResearchStations   = true;
            AutoBuildMiningStations     = true;
            AutoPickBestColonizer       = true;
            AutoPickConstructors        = true;
            AutoPickBestResearchStation = true;
            AutoPickBestMiningStation   = true;
            AutoBuildTerraformers       = true;
        }

        // Determinism (VS2/RC7): switch this empire to a reproducible per-entity RNG stream derived from
        // the world root seed + this empire's stable Id. Used for lockstep/replay; normal play keeps the
        // default clock-seeded ThreadSafeRandom.
        public void UseDeterministicRandom(ulong rootSeed)
            => Random = Determinism.DeterministicStreams.For(rootSeed, Determinism.RngStreamKind.Empire, (ulong)Id);

        // Determinism: put this empire's RNG on its reproducible per-empire stream DURING generation,
        // before the Id is assigned, so personality-trait draws made at creation time are deterministic.
        // Keyed by the predicted stable Id (EmpireList.Count at Add time) so it matches the topology that
        // UseDeterministicRandom uses once the global re-seed runs.
        public void SeedPersonalityRandom(ulong rootSeed, ulong predictedId)
            => Random = Determinism.DeterministicStreams.For(rootSeed, Determinism.RngStreamKind.Empire, predictedId);

        public void RebuildUnlockCachesForAuthoritativeSync()
        {
            string[] exactResearchQueue = data.ResearchQueue.ToArray();
            IShipDesign[] authoritativePlayerDesigns = ShipsWeCanBuildSnapshot
                .Where(d => d.IsPlayerDesign)
                .ToArray();
            var exactTechState = TechEntries
                .Select(t => (Tech: t, t.Progress, t.Unlocked, t.Level))
                .ToArray();

            ResetUnlocks();
            ApplyDataUnlocks();
            ResetTechsAndUnlocks();
            UpdateShipsWeCanBuild(includePlayerDesigns: false);
            foreach (IShipDesign design in authoritativePlayerDesigns)
            {
                if (WeCanBuildThis(design))
                    AddBuildableShip(design);
            }
            foreach (var state in exactTechState)
            {
                state.Tech.Progress = state.Progress;
                state.Tech.Unlocked = state.Unlocked;
                state.Tech.Level = state.Level;
            }
            Research.SetQueueExact(exactResearchQueue);
            foreach (Planet planet in OwnedPlanets)
                planet.RefreshBuildingsWeCanBuildHere();
        }

        public void SetAuthoritativeAutomationState(AuthoritativeEmpireAutomationFlags flags,
            string freighter = null, string colony = null, string scout = null, string constructor = null,
            string researchStation = null, string miningStation = null, bool updateRushQueues = false)
        {
            AuthoritativeMutationGuard.AssertCanMutate(this, AuthoritativeMutationFamily.EmpireAutomation,
                "AutomationFlags");

            flags &= AuthoritativeEmpireAutomationFlags.All;
            AutoPickConstructors = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickConstructors);
            AutoPickBestColonizer = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestColonizer);
            AutoPickBestFreighter = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestFreighter);
            AutoResearch = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoResearch);
            AutoBuildTerraformers = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildTerraformers);
            AutoTaxes = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoTaxes);
            AutoPickBestResearchStation = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestResearchStation);
            AutoPickBestMiningStation = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestMiningStation);
            AutoExplore = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoExplore);
            AutoColonize = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoColonize);
            AutoBuildSpaceRoads = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildSpaceRoads);
            AutoFreighters = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoFreighters);
            AutoBuildResearchStations = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildResearchStations);
            AutoBuildMiningStations = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildMiningStations);
            AutoMilitary = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoMilitary);

            bool rushAll = flags.HasFlag(AuthoritativeEmpireAutomationFlags.RushAllConstruction);
            RushAllConstruction = rushAll;
            if (updateRushQueues)
                SwitchRushAllConstruction(rushAll);

            if (freighter != null)
                data.CurrentAutoFreighter = freighter;
            if (colony != null)
                data.CurrentAutoColony = colony;
            if (scout != null)
                data.CurrentAutoScout = scout;
            if (constructor != null)
                data.CurrentConstructor = constructor;
            if (researchStation != null)
                data.CurrentResearchStation = researchStation;
            if (miningStation != null)
                data.CurrentMiningStation = miningStation;
        }

        internal void SetKnownEmpireForAuthoritativeSync(Empire them, bool known)
        {
            if (them == null || them == this)
                return;

            AuthoritativeMutationGuard.AssertCanMutate(this, AuthoritativeMutationFamily.Diplomacy,
                "KnownEmpires");
            KnownEmpires.SetValue(them.Id, known);
        }
    }
}
