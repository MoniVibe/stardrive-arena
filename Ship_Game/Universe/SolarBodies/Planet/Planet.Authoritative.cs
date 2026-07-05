namespace Ship_Game
{
    public sealed partial class Planet
    {
        void SeedDeterministicBodyRandom(SolarSystem system)
        {
            if (system?.Universe?.IsDeterministicRng == true)
                UseDeterministicRandom(system.Universe.DeterministicRootSeed);
        }

        public void UpdatePassiveAuthoritativeView()
        {
            UpdatePresentationVisibilityOnly();

            if (HasSpacePort && InFrustum)
            {
                Station ??= new SpaceStation();
                Station.UpdateVisibleStation(this, FixedSimTime.Zero);
            }
            else
            {
                Station?.RemoveSceneObject();
            }

            if (!HasSpacePort)
                Station = null;
        }
    }
}
