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
        const float PassiveAuthoritativeInterpolationDefaultFrameSeconds = 1f / PassiveAuthoritativeVisualTicksPerSecond;
        const float PassiveAuthoritativeInterpolationMaxFrameSeconds = 0.25f;
        const float PassiveAuthoritativeInterpolationSnapDistance = 25_000f;
        const uint PassiveAuthoritativeInterpolationMaxTickGap = 60u;
        float PassiveAuthoritativeLastRotation;
        uint PassiveAuthoritativeLastTransformTick;
        bool PassiveAuthoritativeHasLastTransform;
        float PassiveAuthoritativeVisualBank;
        float PassiveAuthoritativeVisualBankTarget;
        int PassiveAuthoritativeRefreshesSinceTarget;
        Vector2 PassiveAuthoritativePreviousPosition;
        Vector2 PassiveAuthoritativeCurrentPosition;
        Vector2 PassiveAuthoritativeRenderPosition;
        float PassiveAuthoritativePreviousRotation;
        float PassiveAuthoritativeCurrentRotation;
        float PassiveAuthoritativeRenderRotation;
        uint PassiveAuthoritativePreviousTransformTick;
        uint PassiveAuthoritativeCurrentTransformTick;
        float PassiveAuthoritativeInterpolationElapsedSeconds;
        float PassiveAuthoritativeInterpolationDurationSeconds;
        bool PassiveAuthoritativeHasCurrentTransform;

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

        internal void ObservePassiveAuthoritativeTransform(uint tick, Vector2 position, float rotation,
            bool active, bool dying)
        {
            bool snapped = ObservePassiveAuthoritativeInterpolation(tick, position, rotation, active, dying);
            ObservePassiveAuthoritativeVisualBank(tick, rotation, active, dying, snapped);
        }

        bool ObservePassiveAuthoritativeInterpolation(uint tick, Vector2 position, float rotation,
            bool active, bool dying)
        {
            if (!active || dying)
            {
                ResetPassiveAuthoritativeInterpolation(tick, position, rotation);
                return true;
            }

            if (!PassiveAuthoritativeHasCurrentTransform)
            {
                ResetPassiveAuthoritativeInterpolation(tick, position, rotation);
                return true;
            }

            if (tick <= PassiveAuthoritativeCurrentTransformTick)
                return false;

            PassiveAuthoritativePreviousPosition = PassiveAuthoritativeCurrentPosition;
            PassiveAuthoritativePreviousRotation = PassiveAuthoritativeCurrentRotation;
            PassiveAuthoritativePreviousTransformTick = PassiveAuthoritativeCurrentTransformTick;
            PassiveAuthoritativeCurrentPosition = position;
            PassiveAuthoritativeCurrentRotation = rotation;
            PassiveAuthoritativeCurrentTransformTick = tick;

            uint elapsedTicks = tick - PassiveAuthoritativePreviousTransformTick;
            PassiveAuthoritativeInterpolationDurationSeconds = SnapshotDurationSeconds(elapsedTicks);
            bool shouldSnap = elapsedTicks > PassiveAuthoritativeInterpolationMaxTickGap
                           || PassiveAuthoritativePreviousPosition.Distance(position)
                              > PassiveAuthoritativeInterpolationSnapDistance;

            PassiveAuthoritativeInterpolationElapsedSeconds = shouldSnap
                ? PassiveAuthoritativeInterpolationDurationSeconds
                : 0f;
            UpdatePassiveAuthoritativeRenderTransform();
            return shouldSnap;
        }

        void ResetPassiveAuthoritativeInterpolation(uint tick, Vector2 position, float rotation)
        {
            PassiveAuthoritativePreviousPosition = position;
            PassiveAuthoritativeCurrentPosition = position;
            PassiveAuthoritativeRenderPosition = position;
            PassiveAuthoritativePreviousRotation = rotation;
            PassiveAuthoritativeCurrentRotation = rotation;
            PassiveAuthoritativeRenderRotation = rotation;
            PassiveAuthoritativePreviousTransformTick = tick;
            PassiveAuthoritativeCurrentTransformTick = tick;
            PassiveAuthoritativeInterpolationElapsedSeconds = 0f;
            PassiveAuthoritativeInterpolationDurationSeconds = 0f;
            PassiveAuthoritativeHasCurrentTransform = true;
        }

        static float SnapshotDurationSeconds(uint elapsedTicks)
            => Math.Max(1u, elapsedTicks) / PassiveAuthoritativeVisualTicksPerSecond;

        void ObservePassiveAuthoritativeVisualBank(uint tick, float rotation, bool active, bool dying, bool snapped)
        {
            if (!active || dying || snapped)
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

            if (tick <= PassiveAuthoritativeLastTransformTick)
                return;

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

        static float LerpRotationShortestArc(float previous, float current, float amount)
            => (previous + SignedRotationDelta(previous, current) * amount).AsNormalizedRadians();

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

        void AdvancePassiveAuthoritativeInterpolation(float elapsedSeconds)
        {
            if (!Active || Dying || !PassiveAuthoritativeHasCurrentTransform)
            {
                PassiveAuthoritativeRenderPosition = Position;
                PassiveAuthoritativeRenderRotation = Rotation;
                return;
            }

            PassiveAuthoritativeInterpolationElapsedSeconds = (
                PassiveAuthoritativeInterpolationElapsedSeconds + SanitizedPassiveAuthoritativeFrameSeconds(elapsedSeconds))
                .Clamped(0f, PassiveAuthoritativeInterpolationDurationSeconds);
            UpdatePassiveAuthoritativeRenderTransform();
        }

        void UpdatePassiveAuthoritativeRenderTransform()
        {
            if (!PassiveAuthoritativeHasCurrentTransform
                || PassiveAuthoritativeInterpolationDurationSeconds <= PassiveAuthoritativeVisualBankEpsilon)
            {
                PassiveAuthoritativeRenderPosition = PassiveAuthoritativeHasCurrentTransform
                    ? PassiveAuthoritativeCurrentPosition
                    : Position;
                PassiveAuthoritativeRenderRotation = PassiveAuthoritativeHasCurrentTransform
                    ? PassiveAuthoritativeCurrentRotation
                    : Rotation;
                return;
            }

            float amount = (PassiveAuthoritativeInterpolationElapsedSeconds
                            / PassiveAuthoritativeInterpolationDurationSeconds).Clamped(0f, 1f);
            PassiveAuthoritativeRenderPosition =
                PassiveAuthoritativePreviousPosition.LerpTo(PassiveAuthoritativeCurrentPosition, amount);
            PassiveAuthoritativeRenderRotation =
                LerpRotationShortestArc(PassiveAuthoritativePreviousRotation, PassiveAuthoritativeCurrentRotation, amount);
        }

        static float SanitizedPassiveAuthoritativeFrameSeconds(float elapsedSeconds)
        {
            if (!float.IsFinite(elapsedSeconds) || elapsedSeconds < 0f)
            {
                elapsedSeconds = GameBase.Base?.Elapsed?.RealTime.Seconds
                              ?? PassiveAuthoritativeInterpolationDefaultFrameSeconds;
            }

            return elapsedSeconds.Clamped(0f, PassiveAuthoritativeInterpolationMaxFrameSeconds);
        }

        float PassiveAuthoritativeRenderYRotation
            => (YRotation + PassiveAuthoritativeVisualBank).Clamped(-MaxBank, MaxBank);

        Vector2 PassiveAuthoritativeTacticalIconPosition
            => PassiveAuthoritativeHasCurrentTransform ? PassiveAuthoritativeRenderPosition : Position;

        float PassiveAuthoritativeTacticalIconRotation
            => (PassiveAuthoritativeHasCurrentTransform ? PassiveAuthoritativeRenderRotation : Rotation)
               + PassiveAuthoritativeVisualBank * 0.35f;

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
        internal Vector2 PassiveAuthoritativeRenderPositionForTest => PassiveAuthoritativeRenderPosition;
        internal float PassiveAuthoritativeRenderRotationForTest => PassiveAuthoritativeRenderRotation;
        internal float PassiveAuthoritativeInterpolationDurationForTest => PassiveAuthoritativeInterpolationDurationSeconds;
        internal float PassiveAuthoritativeInterpolationElapsedForTest => PassiveAuthoritativeInterpolationElapsedSeconds;
        internal float PassiveAuthoritativeRenderYRotationForTest => PassiveAuthoritativeRenderYRotation;
        internal float PassiveAuthoritativeTacticalIconRotationForTest => PassiveAuthoritativeTacticalIconRotation;
        internal float PassiveAuthoritativeTacticalIconWidthScaleForTest => PassiveAuthoritativeTacticalIconWidthScale;

        public void SyncSceneObjectForPassiveAuthoritativeView(bool forceVisible = false, float elapsedSeconds = -1f)
        {
            if (!Active || Dying)
                return;
            if (!forceVisible && !IsVisibleToPlayer)
                return;

            AdvancePassiveAuthoritativeInterpolation(elapsedSeconds);
            Vector2 renderPosition = PassiveAuthoritativeHasCurrentTransform
                ? PassiveAuthoritativeRenderPosition
                : Position;
            float renderRotation = PassiveAuthoritativeHasCurrentTransform
                ? PassiveAuthoritativeRenderRotation
                : Rotation;

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
                         * Matrix.CreateRotationZ(renderRotation)
                         * Matrix.CreateTranslation(new Vector3(renderPosition, 0f));
            ShipSO.Visibility = GlobalStats.ShipVisibility;
        }
    }
}
