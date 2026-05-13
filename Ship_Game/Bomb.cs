using Microsoft.Xna.Framework.Graphics;
using Ship_Game.Audio;
using Ship_Game.Gameplay;
using SDGraphics;
using Ship_Game.Data.Mesh;

namespace Ship_Game
{
    public sealed class Bomb
    {
        public Vector3 Position;
        public Vector3 Velocity;
        private Planet TargetPlanet;
        public Matrix World { get; private set; }

        public IWeaponTemplate Weapon;

        private ParticleEmitter TrailEmitter;
        private ParticleEmitter FireTrailEmitter;
        public readonly int TroopDamageMin;
        public readonly int TroopDamageMax;
        public readonly int HardDamageMin;
        public readonly int HardDamageMax;
        public readonly float PopKilled;
        public readonly float FertilityDamage;
        public readonly string SpecialAction;
        public Empire Owner;
        private float PlanetRadius;
        public readonly int ShipLevel;
        public readonly float ShipHealthPercent;

        public readonly SubTexture Texture;
        public readonly StaticMesh Model;

        // Set inside Update when the bomb should be retired (impact or miss).
        // The sim loop in UniverseScreen.UpdateGame.cs does the actual
        // BombList.RemoveAt — keeps BombList mutation single-threaded relative
        // to the main-thread DrawBombs reader.
        public bool Dead { get; private set; }

        public Bomb(Vector3 position, Empire empire, string weaponName, int shipLevel, float shipHealthPercent)
        {
            Owner = empire;

            const string TextureName = "projBall_02_orange";
            const string ModelName   = "projBall";
            Texture = ResourceManager.ProjTexture(TextureName);
            Model = ResourceManager.ProjectileMesh(ModelName, out var model) ? model : null;
            if (Model == null) Log.Error($"Failed to find Bomb ModelName: {ModelName}");

            Position    = position;
            ShipLevel   = shipLevel;
            Weapon = ResourceManager.GetWeaponTemplateOrNull(weaponName)
                  ?? ResourceManager.GetWeaponTemplate("NuclearBomb");

            TroopDamageMin = Weapon.BombTroopDamageMin;
            TroopDamageMax = Weapon.BombTroopDamageMax;
            HardDamageMin  = Weapon.BombHardDamageMin;
            HardDamageMax  = Weapon.BombHardDamageMax;
            PopKilled      = Weapon.BombPopulationKillPerHit;
            FertilityDamage = Weapon.FertilityDamage;
            SpecialAction   = Weapon.HardCodedAction;
            ShipHealthPercent = shipHealthPercent;
        }

        void DoImpact()
        {
            TargetPlanet.DropBomb(this);
            Dead = true;
        }

        private void SurfaceImpactEffects()
        {
            if (Owner.Universe.IsSystemViewOrCloser &&
                TargetPlanet.System.InFrustum)
            {
                TargetPlanet.PlayPlanetSfx("sd_bomb_impact_01", Position);
                ExplosionManager.AddExplosionNoFlames(Owner.Universe.Screen, Position, 200f, 7.5f);
                Owner.Universe.Screen.Particles.Flash.AddParticle(Position, Vector3.Zero);
                for (int i = 0; i < 50; i++)
                    Owner.Universe.Screen.Particles.Explosion.AddParticle(Position, Vector3.Zero);
            }
        }

        public void PlayCombatScreenEffects(Planet planet, OrbitalDrop od)
        {
            if (Owner.Universe.Screen.IsViewingCombatScreen(planet))
            {
                GameAudio.PlaySfxAsync("Explo1");
                if (Owner.Universe.Screen.workersPanel is CombatScreen cs)
                    cs.AddExplosion(od.TargetTile.ClickRect, 4);
            }
            else
                SurfaceImpactEffects(); // If viewing the planet from space
        }

        public void ResolveSpecialBombActions(Planet planet)
        {
            if (SpecialAction.IsEmpty() || SpecialAction != "Free Owlwoks")
                return;

            Empire cordrazine = planet.Universe.Cordrazine;
            if (planet.Owner == null || planet.Owner != cordrazine)
                return;

            bool owlwoksFreed = false;
            foreach (Troop troop in planet.Troops.GetTroopsOf(cordrazine))
            {
                if (troop.TargetType == TargetType.Soft)
                {
                    owlwoksFreed = true;
                    troop.SetOwner(Owner);
                    troop.Name = cordrazine.data.TroopName.Text;
                    troop.Description = cordrazine.data.TroopDescription.Text;
                }
            }

            if (owlwoksFreed)
            {
                StarDriveGame.Instance?.SetSteamAchievement("Owlwoks_Freed");
            }
        }

        public void SetTarget(Planet p)
        {
            TargetPlanet = p;
            PlanetRadius = TargetPlanet.Radius;
            Vector3 vtt = TargetPlanet.Position3D + p.Random.Vector32D(500 * p.Scale) - Position;
            Velocity = vtt.Normalized(1350f);
        }

        public void Update(FixedSimTime timeStep)
        {
            Position += Velocity * timeStep.FixedTime;
            World = Matrix.CreateTranslation(Position);

            Vector3 planetPos = TargetPlanet.Position3D;
            float impactRadius = TargetPlanet.ShieldStrengthCurrent > 0f ? 100f : 30f;
            if (Position.InRadius(planetPos, PlanetRadius + impactRadius))
            {
                DoImpact();
                return;
            }

            // Miss detection: dot product of (planet - bomb) with velocity
            // tells us which side of closest-approach we're on. Positive ⇒
            // still approaching; negative ⇒ already past. The radius gate
            // (PlanetRadius + 500) keeps the bomb visible until it's clearly
            // past the planet, otherwise it would vanish while still
            // alongside the surface for big planets.
            if ((planetPos - Position).Dot(Velocity) < 0f &&
                !Position.InRadius(planetPos, PlanetRadius + 500f))
            {
                Dead = true;
                return;
            }

            // Inside the fiery-trail radius? Start / continue the trail emitters.
            if (Position.InRadius(planetPos, PlanetRadius + 1000f))
            {
                if (TrailEmitter == null)
                {
                    Velocity *= 0.65f;
                    TrailEmitter     = Owner.Universe.Screen.Particles.ProjectileTrail.NewEmitter(500f, Position);
                    FireTrailEmitter = Owner.Universe.Screen.Particles.FireTrail.NewEmitter(500f, Position);
                }
                TrailEmitter.Update(timeStep.FixedTime, Position);
                FireTrailEmitter.Update(timeStep.FixedTime, Position);
            }
        }
    }
}