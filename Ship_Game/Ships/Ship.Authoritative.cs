using System;
using SDGraphics;
using Ship_Game.ExtensionMethods;
using Ship_Game.Multiplayer.Authoritative;
using SynapseGaming.LightingSystem.Core;
using Vector2 = SDGraphics.Vector2;
using Vector3 = SDGraphics.Vector3;
using Matrix = SDGraphics.Matrix;

namespace Ship_Game.Ships
{
    public partial class Ship
    {
        const float PassiveAuthoritativeVisualTicksPerSecond = 60f;
        const float PassiveAuthoritativeVisualBankEase = 0.35f;
        const float PassiveAuthoritativeVisualBankEpsilon = 0.0001f;
        const int PassiveAuthoritativeVisualHoldRefreshes = 12;
        float PassiveAuthoritativeLastRotation;
        uint PassiveAuthoritativeLastTransformTick;
        bool PassiveAuthoritativeHasLastTransform;
        float PassiveAuthoritativeVisualBank;
        float PassiveAuthoritativeVisualBankTarget;
        int PassiveAuthoritativeRefreshesSinceTarget;

        public void MarkAsTransientEnvironment() => IsTransientEnvironment = true;

        public void SetAuthoritativeTransform(Vector2 position, Vector2 velocity, float rotation,
            SolarSystem system, bool active, bool dying, float yRotation, float xRotation)
        {
            AuthoritativeMutationGuard.AssertCanMutate(this, AuthoritativeMutationFamily.ShipRuntime,
                "Transform");
            if (Position != position || Velocity != velocity)
                ReinsertSpatial = true;

            Position = position;
            Velocity = velocity;
            Rotation = rotation;
            System = system;
            Active = active;
            Dying = dying;
            YRotation = yRotation;
            XRotation = xRotation;
        }

        internal void ObservePassiveAuthoritativeTransform(uint tick, float rotation, bool active, bool dying)
        {
            if (!active || dying)
            {
                PassiveAuthoritativeLastRotation = rotation;
                PassiveAuthoritativeLastTransformTick = tick;
                PassiveAuthoritativeHasLastTransform = true;
                ResetPassiveAuthoritativeVisualBank();
                return;
            }

            if (!PassiveAuthoritativeHasLastTransform)
            {
                PassiveAuthoritativeLastRotation = rotation;
                PassiveAuthoritativeLastTransformTick = tick;
                PassiveAuthoritativeHasLastTransform = true;
                PassiveAuthoritativeVisualBankTarget = 0f;
                PassiveAuthoritativeRefreshesSinceTarget = 0;
                return;
            }

            uint elapsedTicks = tick > PassiveAuthoritativeLastTransformTick
                ? tick - PassiveAuthoritativeLastTransformTick
                : 1u;
            float rotationDelta = SignedRotationDelta(PassiveAuthoritativeLastRotation, rotation);
            PassiveAuthoritativeLastRotation = rotation;
            PassiveAuthoritativeLastTransformTick = tick;
            PassiveAuthoritativeRefreshesSinceTarget = 0;

            if (Math.Abs(rotationDelta) <= PassiveAuthoritativeVisualBankEpsilon)
            {
                PassiveAuthoritativeVisualBankTarget = 0f;
                return;
            }

            float turnRate = Math.Max(RotationRadsPerSecond, PassiveAuthoritativeVisualBankEpsilon);
            float turnFraction = Math.Abs(rotationDelta) / elapsedTicks
                               * PassiveAuthoritativeVisualTicksPerSecond / turnRate;
            float targetMagnitude = (turnFraction * MaxBank).Clamped(0f, MaxBank);
            PassiveAuthoritativeVisualBankTarget = -Math.Sign(rotationDelta) * targetMagnitude;
        }

        static float SignedRotationDelta(float previous, float current)
        {
            float delta = current - previous;
            while (delta > RadMath.PI)
                delta -= RadMath.TwoPI;
            while (delta < -RadMath.PI)
                delta += RadMath.TwoPI;
            return delta;
        }

        void ResetPassiveAuthoritativeVisualBank()
        {
            PassiveAuthoritativeVisualBank = 0f;
            PassiveAuthoritativeVisualBankTarget = 0f;
            PassiveAuthoritativeRefreshesSinceTarget = 0;
        }

        float AdvancePassiveAuthoritativeVisualBank()
        {
            if (!Active || Dying)
                PassiveAuthoritativeVisualBankTarget = 0f;
            else if (++PassiveAuthoritativeRefreshesSinceTarget > PassiveAuthoritativeVisualHoldRefreshes)
                PassiveAuthoritativeVisualBankTarget = 0f;

            PassiveAuthoritativeVisualBank = PassiveAuthoritativeVisualBank.LerpTo(
                PassiveAuthoritativeVisualBankTarget, PassiveAuthoritativeVisualBankEase);
            if (Math.Abs(PassiveAuthoritativeVisualBank - PassiveAuthoritativeVisualBankTarget)
                <= PassiveAuthoritativeVisualBankEpsilon)
            {
                PassiveAuthoritativeVisualBank = PassiveAuthoritativeVisualBankTarget;
            }
            if (PassiveAuthoritativeVisualBankTarget == 0f
                && Math.Abs(PassiveAuthoritativeVisualBank) <= PassiveAuthoritativeVisualBankEpsilon)
            {
                PassiveAuthoritativeVisualBank = 0f;
            }

            return PassiveAuthoritativeRenderYRotation;
        }

        float PassiveAuthoritativeRenderYRotation
            => (YRotation + PassiveAuthoritativeVisualBank).Clamped(-MaxBank, MaxBank);

        float PassiveAuthoritativeTacticalIconRotation
            => Rotation + PassiveAuthoritativeVisualBank * 0.35f;

        float PassiveAuthoritativeTacticalIconWidthScale
        {
            get
            {
                float maxBank = Math.Max(MaxBank, PassiveAuthoritativeVisualBankEpsilon);
                float bankRatio = (Math.Abs(PassiveAuthoritativeVisualBank) / maxBank).Clamped(0f, 1f);
                return (1f - bankRatio * 0.16f).Clamped(0.84f, 1f);
            }
        }

        internal float PassiveAuthoritativeVisualBankForTest => PassiveAuthoritativeVisualBank;
        internal float PassiveAuthoritativeVisualBankTargetForTest => PassiveAuthoritativeVisualBankTarget;
        internal float PassiveAuthoritativeRenderYRotationForTest => PassiveAuthoritativeRenderYRotation;
        internal float PassiveAuthoritativeTacticalIconRotationForTest => PassiveAuthoritativeTacticalIconRotation;
        internal float PassiveAuthoritativeTacticalIconWidthScaleForTest => PassiveAuthoritativeTacticalIconWidthScale;

        public void SyncSceneObjectForPassiveAuthoritativeView(bool forceVisible = false)
        {
            if (!Active || Dying)
                return;
            if (!forceVisible && !IsVisibleToPlayer)
                return;

            if (ShipSO == null)
            {
                Universe.Screen?.QueueSceneObjectCreation(this);
                return;
            }

            NotVisibleToPlayerTimer = 0f;
            float renderYRotation = AdvancePassiveAuthoritativeVisualBank();
            ShipSO.World = Matrix.CreateTranslation(new Vector3(ShipData.BaseHull.MeshOffset, 0f))
                         * Matrix.CreateRotationY(renderYRotation)
                         * Matrix.CreateRotationX(XRotation)
                         * Matrix.CreateRotationZ(Rotation)
                         * Matrix.CreateTranslation(new Vector3(Position, 0f));
            ShipSO.Visibility = GlobalStats.ShipVisibility;
        }
    }
}
