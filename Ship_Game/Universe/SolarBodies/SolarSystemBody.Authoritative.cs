using Ship_Game.Multiplayer.Authoritative;

namespace Ship_Game
{
    public partial class SolarSystemBody
    {
        public bool OwnerIsHumanControlled => AuthoritativeHumanPlayers.IsHumanControlled(Owner);

        // Determinism: switch this solar body to a reproducible per-entity RNG stream derived from the
        // world root seed + this body's stable Id. Generation-time consumers (moon counts, tile events,
        // random commodities/volcanoes) then draw reproducibly, so the generated universe is bit-identical
        // run-to-run. seed==0 (normal play) keeps the default clock-seeded ThreadSafeRandom.
        public void UseDeterministicRandom(ulong rootSeed)
        {
            if (rootSeed != 0)
                Random = Determinism.DeterministicStreams.For(rootSeed, Determinism.RngStreamKind.SolarBody, (ulong)Id);
        }

        protected void UpdatePresentationVisibilityOnly()
        {
            bool visible = System.InFrustum;
            if (visible && ShouldCreateSO)
            {
                CreatePlanetSceneObject();
            }
            else if (SO != null)
            {
                UpdateSO(visible);
            }
        }
    }
}
