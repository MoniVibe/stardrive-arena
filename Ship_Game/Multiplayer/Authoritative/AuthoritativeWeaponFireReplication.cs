using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using SDUtils;
using Ship_Game.Data;
using Ship_Game.Gameplay;
using Ship_Game.Ships;
using Ship_Game.Universe;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.Multiplayer.Authoritative;

public readonly struct AuthoritativeWeaponFireEvent
{
    public readonly uint Tick;
    public readonly uint Sequence;
    public readonly int ShooterShipId;
    public readonly string WeaponUid;
    public readonly int ModuleGridX;
    public readonly int ModuleGridY;
    public readonly int TargetShipId;
    public readonly Vector2 Source;
    public readonly Vector2 Direction;
    public readonly bool IsBeam;
    public readonly Vector2 Destination;

    public AuthoritativeWeaponFireEvent(uint tick, uint sequence, int shooterShipId, string weaponUid,
        int moduleGridX, int moduleGridY, int targetShipId, Vector2 source, Vector2 direction,
        bool isBeam, Vector2 destination)
    {
        Tick = tick;
        Sequence = sequence;
        ShooterShipId = shooterShipId;
        WeaponUid = weaponUid ?? "";
        ModuleGridX = moduleGridX;
        ModuleGridY = moduleGridY;
        TargetShipId = targetShipId;
        Source = source;
        Direction = direction.SqLen() > 0.000001f ? direction.Normalized() : Vector2.Right;
        IsBeam = isBeam;
        Destination = destination;
    }

    public void AppendRow(StringBuilder sb)
    {
        sb.Append("WF|").Append(Tick)
          .Append('|').Append(Sequence)
          .Append('|').Append(ShooterShipId)
          .Append('|').Append(WeaponUid)
          .Append('|').Append(ModuleGridX)
          .Append('|').Append(ModuleGridY)
          .Append('|').Append(TargetShipId)
          .Append('|').Append(FloatBits(Source.X))
          .Append('|').Append(FloatBits(Source.Y))
          .Append('|').Append(FloatBits(Direction.X))
          .Append('|').Append(FloatBits(Direction.Y))
          .Append('|').Append(IsBeam ? 1 : 0)
          .Append('|').Append(FloatBits(Destination.X))
          .Append('|').Append(FloatBits(Destination.Y))
          .AppendLine();
    }

    public static bool TryParse(string line, out AuthoritativeWeaponFireEvent fire)
    {
        fire = default;
        string[] p = line?.Split('|');
        if (p == null || p.Length < 15 || p[0] != "WF")
            return false;

        if (!uint.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint tick)
            || !uint.TryParse(p[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out uint sequence)
            || !int.TryParse(p[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int shooterShipId)
            || !int.TryParse(p[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int moduleGridX)
            || !int.TryParse(p[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out int moduleGridY)
            || !int.TryParse(p[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetShipId)
            || !TryParseFloatBits(p[8], out float sourceX)
            || !TryParseFloatBits(p[9], out float sourceY)
            || !TryParseFloatBits(p[10], out float directionX)
            || !TryParseFloatBits(p[11], out float directionY)
            || !int.TryParse(p[12], NumberStyles.Integer, CultureInfo.InvariantCulture, out int isBeam)
            || !TryParseFloatBits(p[13], out float destinationX)
            || !TryParseFloatBits(p[14], out float destinationY))
        {
            return false;
        }

        fire = new AuthoritativeWeaponFireEvent(tick, sequence, shooterShipId, p[4] ?? "",
            moduleGridX, moduleGridY, targetShipId, new Vector2(sourceX, sourceY),
            new Vector2(directionX, directionY), isBeam != 0, new Vector2(destinationX, destinationY));
        return true;
    }

    static uint FloatBits(float value) => BitConverter.SingleToUInt32Bits(value);

    static bool TryParseFloatBits(string text, out float value)
    {
        value = 0f;
        if (!uint.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint bits))
            return false;
        value = BitConverter.UInt32BitsToSingle(bits);
        return true;
    }
}

public sealed class AuthoritativeWeaponFireReplication
{
    public const int MaxEventsPerSnapshot = 384;
    const int MaxClientDedupeRows = 2048;

    readonly List<AuthoritativeWeaponFireEvent> PendingHostEvents = new(MaxEventsPerSnapshot);
    readonly List<string> PendingClientEchoRows = new(MaxEventsPerSnapshot);
    readonly Queue<string> RecentClientRows = new(MaxClientDedupeRows);
    readonly HashSet<string> RecentClientRowSet = new(StringComparer.Ordinal);

    uint RecordingTick;
    uint NextSequence;
    int DroppedSinceLastEmit;

    public int PendingHostEventCount => PendingHostEvents.Count;
    public int PendingClientEchoRowCount => PendingClientEchoRows.Count;
    public int LastOverflowDropCount { get; private set; }
    public int TotalOverflowDropCount { get; private set; }

    public void BeginHostTick(uint tick)
    {
        RecordingTick = tick;
    }

    public void EndHostTick(uint tick)
    {
        if (RecordingTick == tick)
            RecordingTick = 0;
    }

    public void RecordProjectile(Weapon weapon, Ship shooter, Vector2 source, Vector2 direction, GameObject target)
    {
        if (!CanRecord(weapon, shooter))
            return;

        int targetShipId = TargetShipId(target);
        Vector2 destination = target?.Position ?? source + direction.Normalized(weapon.BaseRange);
        AddHostEvent(new AuthoritativeWeaponFireEvent(RecordingTick, NextSequence++, shooter.Id,
            weapon.UID, weapon.Module?.Pos.X ?? -1, weapon.Module?.Pos.Y ?? -1, targetShipId,
            source, direction, isBeam: false, destination));
    }

    public void RecordBeam(Weapon weapon, Ship shooter, Vector2 source, Vector2 destination, GameObject target)
    {
        if (!CanRecord(weapon, shooter))
            return;

        Vector2 direction = destination - source;
        if (direction.SqLen() <= 0.000001f)
            direction = shooter.Rotation.RadiansToDirection();

        AddHostEvent(new AuthoritativeWeaponFireEvent(RecordingTick, NextSequence++, shooter.Id,
            weapon.UID, weapon.Module?.Pos.X ?? -1, weapon.Module?.Pos.Y ?? -1, TargetShipId(target),
            source, direction, isBeam: true, destination));
    }

    public void EmitRows(uint tick, StringBuilder sb)
    {
        for (int i = 0; i < PendingHostEvents.Count; ++i)
            PendingHostEvents[i].AppendRow(sb);
        PendingHostEvents.Clear();

        for (int i = 0; i < PendingClientEchoRows.Count; ++i)
            sb.AppendLine(PendingClientEchoRows[i]);
        PendingClientEchoRows.Clear();

        LastOverflowDropCount = DroppedSinceLastEmit;
        DroppedSinceLastEmit = 0;
    }

    public void ApplyLine(UniverseState universe, string line)
    {
        if (universe == null || string.IsNullOrEmpty(line))
            return;

        if (PendingClientEchoRows.Count < MaxEventsPerSnapshot)
            PendingClientEchoRows.Add(line);

        if (!AuthoritativeWeaponFireEvent.TryParse(line, out AuthoritativeWeaponFireEvent fire))
            return;

        if (!RememberClientRow(line))
            return;

        universe.Objects?.AddRenderOnlyWeaponFireVisual(new RenderOnlyWeaponFireVisual(universe, fire));
    }

    bool CanRecord(Weapon weapon, Ship shooter)
    {
        return RecordingTick != 0
               && weapon != null
               && shooter?.Active == true
               && shooter.Universe?.AuthoritativeWeaponFire == this
               && !string.IsNullOrEmpty(weapon.UID);
    }

    void AddHostEvent(AuthoritativeWeaponFireEvent fire)
    {
        if (PendingHostEvents.Count >= MaxEventsPerSnapshot)
        {
            ++DroppedSinceLastEmit;
            ++TotalOverflowDropCount;
            return;
        }
        PendingHostEvents.Add(fire);
    }

    bool RememberClientRow(string line)
    {
        if (!RecentClientRowSet.Add(line))
            return false;

        RecentClientRows.Enqueue(line);
        while (RecentClientRows.Count > MaxClientDedupeRows)
            RecentClientRowSet.Remove(RecentClientRows.Dequeue());
        return true;
    }

    static int TargetShipId(GameObject target)
    {
        return target switch
        {
            Ship ship => ship.Id,
            ShipModule module => module.GetParent()?.Id ?? 0,
            _ => 0,
        };
    }
}

public sealed class RenderOnlyWeaponFireVisual
{
    readonly int ShooterShipId;
    readonly int TargetShipId;
    readonly string WeaponUid;
    readonly int ModuleGridX;
    readonly int ModuleGridY;
    readonly bool IsBeam;
    readonly Vector2 RowSource;
    readonly Vector2 RowDestination;
    readonly float Lifetime;
    readonly float Speed;
    readonly float ProjectileWorldSize;
    readonly float BeamThickness;

    Ship Shooter;
    Ship Target;
    Weapon Weapon;
    ShipModule Module;
    Vector2 Position;
    Vector2 Direction;
    float Age;

    public bool Active { get; private set; } = true;

    public RenderOnlyWeaponFireVisual(UniverseState universe, AuthoritativeWeaponFireEvent fire)
    {
        ShooterShipId = fire.ShooterShipId;
        TargetShipId = fire.TargetShipId;
        WeaponUid = fire.WeaponUid ?? "";
        ModuleGridX = fire.ModuleGridX;
        ModuleGridY = fire.ModuleGridY;
        IsBeam = fire.IsBeam;
        RowSource = fire.Source;
        RowDestination = fire.Destination;
        Position = fire.Source;
        Direction = fire.Direction.SqLen() > 0.000001f ? fire.Direction.Normalized() : Vector2.Right;

        Resolve(universe);
        Speed = Math.Max(Weapon?.ProjectileSpeed ?? 3000f, 250f);
        float range = Weapon?.BaseRange ?? Math.Max(RowSource.Distance(RowDestination), 1000f);
        Lifetime = IsBeam
            ? Math.Clamp(Weapon?.BeamDuration ?? 0.12f, 0.05f, 2.5f)
            : Math.Clamp((range / Speed) + 0.35f, 0.15f, 8f);
        ProjectileWorldSize = Math.Max(16f, 20f * (Weapon?.ProjectileRadius ?? 4f) * (Weapon?.Scale ?? 1f));
        BeamThickness = Math.Max(2f, (Weapon?.BeamThickness ?? 4) * 0.5f);
    }

    public void Update(UniverseState universe, FixedSimTime timeStep)
    {
        if (!Active)
            return;

        float dt = timeStep.FixedTime > 0f ? timeStep.FixedTime : 1f / 60f;
        Age += dt;
        if (Age >= Lifetime)
        {
            Active = false;
            return;
        }

        Resolve(universe);
        if (IsBeam)
            return;

        float step = Speed * dt;
        // Only guided weapons track the live target; ballistic shots fly the
        // straight line they were fired on (destination fixed at fire time).
        if (Weapon?.Tag_Guided == true)
        {
            Vector2 destination = CurrentDestination();
            Vector2 toDestination = destination - Position;
            if (toDestination.Length() <= Math.Max(step, ProjectileWorldSize))
            {
                Active = false;
                return;
            }
            Direction = toDestination.Normalized();
        }
        else if (Position.Distance(BallisticDestination()) <= Math.Max(step, ProjectileWorldSize))
        {
            Active = false;
            return;
        }

        Position += Direction * step;
    }

    public void Draw(SpriteBatch batch, UniverseScreen screen)
    {
        if (!Active || screen == null)
            return;

        if (IsBeam)
        {
            screen.DrawLineWideProjected(CurrentBeamSource(), CurrentDestination(), WeaponColor(), BeamThickness);
            return;
        }

        SubTexture texture = Weapon != null ? ResourceManager.ProjTexture(Weapon.ProjectileTexturePath) : null;
        if (texture?.Texture == null)
        {
            screen.DrawCircleProjected(Position, ProjectileWorldSize * 0.5f, WeaponColor(), 2f);
            return;
        }

        screen.ProjectToScreenCoords(Position, 25f, ProjectileWorldSize, out Vector2d screenPos, out double screenSize);
        float maxTexSize = Math.Max(texture.Width, texture.Height);
        float scale = (float)(screenSize / Math.Max(maxTexSize, 1f));
        if (scale <= 0f || float.IsNaN(scale))
            return;

        batch.Draw(texture.Texture, screenPos.ToVec2f(), texture.Rect, WeaponColor(),
            Direction.ToRadians(), texture.CenterF, scale, SpriteEffects.None, 1f);
    }

    void Resolve(UniverseState universe)
    {
        UniverseObjectManager objects = universe?.Objects;
        if (objects == null)
            return;

        if (Shooter?.Active != true || Shooter.Id != ShooterShipId)
            Shooter = objects.FindShip(ShooterShipId);
        if (TargetShipId > 0 && (Target?.Active != true || Target.Id != TargetShipId))
            Target = objects.FindShip(TargetShipId);

        if (Shooter?.Active != true)
            return;

        if (Module == null || Module.GetParent() != Shooter)
            Module = Shooter.GetModuleAt(ModuleGridX, ModuleGridY);

        if (Weapon == null)
            Weapon = ResolveWeapon(Shooter, Module, WeaponUid);
    }

    Vector2 CurrentBeamSource()
        => Module?.Active == true ? Module.Position : RowSource;

    Vector2 CurrentDestination()
    {
        if (Target?.Active == true)
            return Target.Position;
        return BallisticDestination();
    }

    Vector2 BallisticDestination()
        => RowDestination.SqLen() > 0.000001f
            ? RowDestination
            : RowSource + Direction * (Weapon?.BaseRange ?? 1000f);

    Color WeaponColor()
    {
        return Weapon?.Light switch
        {
            "Green" => Color.LightGreen,
            "Red" => Color.OrangeRed,
            "Orange" => Color.Orange,
            "Purple" => Color.MediumPurple,
            "Blue" => Color.DeepSkyBlue,
            _ => Color.White,
        };
    }

    static Weapon ResolveWeapon(Ship shooter, ShipModule module, string weaponUid)
    {
        if (module?.InstalledWeapon != null
            && (string.IsNullOrEmpty(weaponUid)
                || string.Equals(module.InstalledWeapon.UID, weaponUid, StringComparison.Ordinal)))
        {
            return module.InstalledWeapon;
        }

        if (shooter == null)
            return null;

        for (int i = 0; i < shooter.Weapons.Count; ++i)
        {
            Weapon weapon = shooter.Weapons[i];
            if (weapon != null && string.Equals(weapon.UID, weaponUid, StringComparison.Ordinal))
                return weapon;
        }
        return null;
    }
}
