#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using SDUtils.Deterministic;

namespace StarDrive.Tools.ArenaFingerprint;

public sealed class PortableFingerprintOptions
{
    public const int DefaultGenerationSeed = 0x00005EED;
    public const uint DefaultRngSeed = 0xA12EA000u;
    public const int DefaultSteps = 2000;
    public const double DefaultStepDt = 1.0 / 60.0;

    public int GenerationSeed { get; set; } = DefaultGenerationSeed;
    public uint RngSeed { get; set; } = DefaultRngSeed;
    public int Steps { get; set; } = DefaultSteps;
    public double StepDt { get; set; } = DefaultStepDt;
    public string OutputPath { get; set; } = "";
    public bool SelfTest { get; set; }
    public bool ShowHelp { get; set; }

    public static string Usage =>
        "ArenaFingerprint [--self-test] [--steps N] [--generation-seed 0xHEX] " +
        "[--rng-seed 0xHEX] [--out path]\n" +
        "Default output: ./sim-output/determinism-fingerprint.txt next to ArenaFingerprint.exe";

    public PortableFingerprintOptions Clone() => new()
    {
        GenerationSeed = GenerationSeed,
        RngSeed = RngSeed,
        Steps = Steps,
        StepDt = StepDt,
        OutputPath = OutputPath,
        SelfTest = SelfTest,
        ShowHelp = ShowHelp,
    };

    public static PortableFingerprintOptions Default(string baseDirectory) => new()
    {
        OutputPath = Path.Combine(baseDirectory, "sim-output", "determinism-fingerprint.txt"),
    };

    public static PortableFingerprintOptions FromArgs(string[] args, string baseDirectory)
    {
        PortableFingerprintOptions options = Default(baseDirectory);
        for (int i = 0; i < args.Length; ++i)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                case "/?":
                    options.ShowHelp = true;
                    break;
                case "--self-test":
                    options.SelfTest = true;
                    break;
                case "--steps":
                    options.Steps = ParsePositiveInt(RequireValue(args, ref i, arg), arg);
                    break;
                case "--generation-seed":
                    options.GenerationSeed = unchecked((int)ParseUInt(RequireValue(args, ref i, arg), arg));
                    break;
                case "--rng-seed":
                    options.RngSeed = ParseUInt(RequireValue(args, ref i, arg), arg);
                    break;
                case "--out":
                    options.OutputPath = Path.GetFullPath(RequireValue(args, ref i, arg));
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

        return options;
    }

    static string RequireValue(string[] args, ref int index, string arg)
    {
        if (++index >= args.Length)
            throw new ArgumentException($"{arg} requires a value.");
        return args[index];
    }

    static int ParsePositiveInt(string text, string arg)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) || value <= 0)
            throw new ArgumentException($"{arg} requires a positive integer.");
        return value;
    }

    static uint ParseUInt(string text, string arg)
    {
        NumberStyles style = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? NumberStyles.HexNumber
            : NumberStyles.Integer;
        string valueText = style == NumberStyles.HexNumber ? text[2..] : text;
        if (!uint.TryParse(valueText, style, CultureInfo.InvariantCulture, out uint value))
            throw new ArgumentException($"{arg} requires a uint value.");
        return value;
    }
}

public sealed class PortableFingerprintSelfTestResult
{
    public required PortableFingerprintRun Baseline { get; init; }
    public required PortableFingerprintRun Rerun { get; init; }
    public required PortableFingerprintRun DifferentSeed { get; init; }
    public required string OutputPath { get; init; }
}

public sealed class PortableFingerprintRun
{
    public readonly string Label;
    public readonly int GenerationSeed;
    public readonly uint RngSeed;
    public readonly int Steps;
    public readonly List<string> HeaderLines = new();
    public readonly List<string> StepLines = new();
    public string SequenceSha256 = "";

    public PortableFingerprintRun(string label, int generationSeed, uint rngSeed, int steps)
    {
        Label = label;
        GenerationSeed = generationSeed;
        RngSeed = rngSeed;
        Steps = steps;
    }

    public void FinalizeSequenceDigest()
    {
        string text = string.Join("\n", StepLines);
        SequenceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    public string ToFileText()
    {
        var sb = new StringBuilder();
        foreach (string line in HeaderLines)
            sb.AppendLine(line);
        sb.AppendLine($"SequenceSha256={SequenceSha256}");
        sb.AppendLine($"RunLabel={Label}");
        sb.AppendLine();
        foreach (string line in StepLines)
            sb.AppendLine(line);
        return sb.ToString();
    }
}

public static class PortableFingerprintRunner
{
    const string Profile = "PortableFloatCollision-v1";
    const string RngAlgorithm = "splitmix64-v1";
    const string HashAlgorithm = "SdHash128-v1";
    const float ArenaCenterX = 950000f;
    const float ArenaCenterY = -725000f;
    const float ArenaRadius = 1800f;

    public static PortableFingerprintRun Run(PortableFingerprintOptions options, string label = "portable")
    {
        if (options.Steps <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Step count must be positive.");

        var sim = SyntheticArenaSim.Create(options.GenerationSeed, options.RngSeed);
        var run = new PortableFingerprintRun(label, options.GenerationSeed, options.RngSeed, options.Steps);
        AddHeader(run, options);
        CaptureStep(run, sim, 0);

        float dt = (float)options.StepDt;
        for (int step = 1; step <= options.Steps; ++step)
        {
            sim.Step(dt);
            CaptureStep(run, sim, step);
        }

        run.FinalizeSequenceDigest();
        return run;
    }

    public static string Write(PortableFingerprintRun run, string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, run.ToFileText(), Encoding.UTF8);
        return fullPath;
    }

    public static PortableFingerprintSelfTestResult RunSelfTest(PortableFingerprintOptions options)
    {
        PortableFingerprintOptions firstOptions = options.Clone();
        firstOptions.SelfTest = false;

        PortableFingerprintRun baseline = Run(firstOptions, "portable");
        PortableFingerprintRun rerun = Run(firstOptions, "portable-rerun");
        int divergence = FirstDivergence(baseline, rerun, out string reason);
        if (divergence >= 0)
            throw new InvalidOperationException(
                $"Same-seed portable fingerprint diverged at step {divergence}: {reason}\n" +
                $"baseline={SafeStepLine(baseline, divergence)}\nrerun={SafeStepLine(rerun, divergence)}");

        PortableFingerprintOptions differentOptions = options.Clone();
        differentOptions.SelfTest = false;
        differentOptions.GenerationSeed = unchecked(options.GenerationSeed + 1);
        differentOptions.RngSeed = unchecked(options.RngSeed + 1);
        PortableFingerprintRun different = Run(differentOptions, "different-seed");
        if (baseline.SequenceSha256 == different.SequenceSha256)
            throw new InvalidOperationException("Different seed unexpectedly produced the same fingerprint digest.");

        string path = Write(baseline, options.OutputPath);
        return new PortableFingerprintSelfTestResult
        {
            Baseline = baseline,
            Rerun = rerun,
            DifferentSeed = different,
            OutputPath = path,
        };
    }

    public static int FirstDivergence(PortableFingerprintRun a, PortableFingerprintRun b, out string reason)
    {
        if (a.StepLines.Count != b.StepLines.Count)
        {
            reason = $"step-line count mismatch {a.StepLines.Count} != {b.StepLines.Count}";
            return Math.Min(a.StepLines.Count, b.StepLines.Count);
        }
        for (int i = 0; i < a.StepLines.Count; ++i)
        {
            if (a.StepLines[i] == b.StepLines[i])
                continue;
            reason = "step line mismatch";
            return i;
        }
        if (a.SequenceSha256 != b.SequenceSha256)
        {
            reason = $"sequence digest mismatch {a.SequenceSha256} != {b.SequenceSha256}";
            return -2;
        }
        reason = "none";
        return -1;
    }

    public static string SafeStepLine(PortableFingerprintRun run, int step)
        => step >= 0 && step < run.StepLines.Count ? run.StepLines[step] : "<none>";

    static void AddHeader(PortableFingerprintRun run, PortableFingerprintOptions options)
    {
        run.HeaderLines.Add("# StarDrive Arena portable deterministic fingerprint");
        run.HeaderLines.Add($"GeneratedUtc={DateTime.UtcNow:O}");
        run.HeaderLines.Add("ContentMode=content-free synthetic arena");
        run.HeaderLines.Add($"GameVersion={GameVersion()}");
        run.HeaderLines.Add($"RunnerVersion={RunnerVersion()}");
        run.HeaderLines.Add($"RuntimeVersion={RuntimeInformation.FrameworkDescription}");
        run.HeaderLines.Add($"OS={RuntimeInformation.OSDescription}");
        run.HeaderLines.Add($"OSArchitecture={RuntimeInformation.OSArchitecture}");
        run.HeaderLines.Add($"ProcessArchitecture={RuntimeInformation.ProcessArchitecture}");
        run.HeaderLines.Add($"CpuModel={CpuModelForHeader()}");
        run.HeaderLines.Add($"ProcessorCount={Environment.ProcessorCount}");
        run.HeaderLines.Add($"MachineName={Environment.MachineName}");
        run.HeaderLines.Add($"DeterminismProfile={Profile}");
        run.HeaderLines.Add($"BuildFingerprint={Hex(ComputeBuildFingerprint())}");
        run.HeaderLines.Add($"GenerationSeed=0x{run.GenerationSeed:X8}");
        run.HeaderLines.Add($"RngSeed=0x{run.RngSeed:X8}");
        run.HeaderLines.Add($"SimSteps={run.Steps}");
        run.HeaderLines.Add($"StepLines={run.Steps + 1}");
        run.HeaderLines.Add($"StepDt={options.StepDt.ToString("R", CultureInfo.InvariantCulture)}");
        run.HeaderLines.Add("PrimaryHash=PortableSyntheticArenaStateHash");
        run.HeaderLines.Add("Columns=step authAlgorithm authHi authLo debugFull lanes frame rngState playerAlive enemyAlive projectiles shipCollisions projectileHits playerDigest enemyDigest projectileDigest");
    }

    static void CaptureStep(PortableFingerprintRun run, SyntheticArenaSim sim, int step)
    {
        SyntheticArenaHash hash = sim.ComputeHash();
        run.StepLines.Add(
            $"step={step.ToString("D4", CultureInfo.InvariantCulture)} " +
            $"authAlgorithm={HashAlgorithm} authHi={Hex(hash.AuthHi)} authLo={Hex(hash.AuthLo)} " +
            $"debugFull={Hex(hash.DebugFull)} lanes={hash.Lanes} " +
            $"frame={sim.Frame} rngState={Hex(sim.RngState)} " +
            $"playerAlive={sim.AliveCount(0)} enemyAlive={sim.AliveCount(1)} " +
            $"projectiles={sim.ProjectileCount} shipCollisions={sim.LastShipCollisions} " +
            $"projectileHits={sim.LastProjectileHits} " +
            $"playerDigest={hash.PlayerDigest} enemyDigest={hash.EnemyDigest} " +
            $"projectileDigest={hash.ProjectileDigest}");
    }

    static string CpuModelForHeader()
    {
        string? id = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
        if (!string.IsNullOrWhiteSpace(id))
            return id;
        string? arch = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
        string? level = Environment.GetEnvironmentVariable("PROCESSOR_LEVEL");
        string? revision = Environment.GetEnvironmentVariable("PROCESSOR_REVISION");
        string fallback = $"{arch} level={level} revision={revision}".Trim();
        return !string.IsNullOrWhiteSpace(fallback) ? fallback : RuntimeInformation.ProcessArchitecture.ToString();
    }

    static string GameVersion()
        => typeof(PortableFingerprintRunner).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    static string RunnerVersion()
    {
        AssemblyInformationalVersionAttribute? attr =
            typeof(PortableFingerprintRunner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        return attr?.InformationalVersion ?? GameVersion();
    }

    static ulong ComputeBuildFingerprint()
    {
        var h = DetHash.New();
        h.AddString(GameVersion());
        h.AddString(RuntimeInformation.FrameworkDescription);
        h.AddString(RuntimeInformation.ProcessArchitecture.ToString());
        h.AddString(RngAlgorithm);
        h.AddString(HashAlgorithm);
        h.AddString(Profile);
        return h.Value;
    }

    static string Hex(ulong value) => "0x" + value.ToString("X16", CultureInfo.InvariantCulture);

    sealed class SyntheticArenaSim
    {
        readonly List<SyntheticShip> Ships = new();
        readonly List<SyntheticProjectile> Projectiles = new();
        DetRandom Rng;
        int NextProjectileId = 1;

        public int Frame { get; private set; }
        public int LastShipCollisions { get; private set; }
        public int LastProjectileHits { get; private set; }
        public ulong RngState => Rng.State;
        public int ProjectileCount => Projectiles.Count;

        public static SyntheticArenaSim Create(int generationSeed, uint rngSeed)
        {
            ulong root = ((ulong)(uint)generationSeed << 32) ^ rngSeed;
            var sim = new SyntheticArenaSim { Rng = new DetRandom(root) };
            sim.SpawnShips();
            return sim;
        }

        void SpawnShips()
        {
            const int perSide = 6;
            for (int team = 0; team < 2; ++team)
            {
                float side = team == 0 ? -1f : 1f;
                for (int i = 0; i < perSide; ++i)
                {
                    float lane = i - (perSide - 1) * 0.5f;
                    float radius = 30f + (i % 3) * 7f + Rng.NextFloat(-2.5f, 2.5f);
                    Ships.Add(new SyntheticShip
                    {
                        Id = team * 100 + i + 1,
                        Team = team,
                        Model = $"SYN-{team}-{i}",
                        X = ArenaCenterX + side * (760f + 36f * i),
                        Y = ArenaCenterY + lane * 148f + Rng.NextFloat(-8f, 8f),
                        VX = -side * Rng.NextFloat(10f, 30f),
                        VY = Rng.NextFloat(-24f, 24f),
                        Rotation = team == 0 ? 0f : MathF.PI,
                        AngularVelocity = Rng.NextFloat(-0.08f, 0.08f),
                        Radius = radius,
                        Mass = radius * radius * 0.08f,
                        Hull = 145f + i * 11f + Rng.NextFloat(0f, 8f),
                        Shield = 70f + (perSide - i) * 4f,
                        Cooldown = Rng.NextFloat(0.05f, 0.7f),
                        Heat = Rng.NextFloat(0f, 0.2f),
                        Alive = true,
                    });
                }
            }
        }

        public void Step(float dt)
        {
            ++Frame;
            LastShipCollisions = 0;
            LastProjectileHits = 0;

            for (int i = 0; i < Ships.Count; ++i)
                UpdateShip(Ships[i], dt);

            for (int i = 0; i < Projectiles.Count; ++i)
                UpdateProjectile(Projectiles[i], dt);

            ResolveShipCollisions();
            ResolveProjectileHits();
            Projectiles.RemoveAll(p => !p.Alive || p.Ttl <= 0f);
        }

        void UpdateShip(SyntheticShip ship, float dt)
        {
            if (!ship.Alive)
                return;

            SyntheticShip? target = NearestEnemy(ship);
            if (target != null)
            {
                float dx = target.X - ship.X;
                float dy = target.Y - ship.Y;
                float desired = MathF.Atan2(dy, dx);
                float turn = NormalizeAngle(desired - ship.Rotation);
                ship.AngularVelocity += Clamp(turn * 0.9f, -1.2f, 1.2f) * dt;
            }

            ship.AngularVelocity *= 0.985f;
            ship.Rotation = NormalizeAngle(ship.Rotation + ship.AngularVelocity * dt);
            float thrust = 28f + ship.Heat * 8f + Rng.NextFloat(-2.0f, 2.0f);
            ship.VX += MathF.Cos(ship.Rotation) * thrust * dt;
            ship.VY += MathF.Sin(ship.Rotation) * thrust * dt;
            ship.VX *= 0.998f;
            ship.VY *= 0.998f;

            ApplyArenaBoundary(ship);
            ship.X += ship.VX * dt;
            ship.Y += ship.VY * dt;
            ship.Cooldown -= dt;
            ship.Heat = MathF.Max(0f, ship.Heat - 0.15f * dt);

            if (target != null && ship.Cooldown <= 0f)
                Fire(ship, target);
        }

        void UpdateProjectile(SyntheticProjectile projectile, float dt)
        {
            if (!projectile.Alive)
                return;

            projectile.X += projectile.VX * dt;
            projectile.Y += projectile.VY * dt;
            projectile.VX *= 0.9995f;
            projectile.VY *= 0.9995f;
            projectile.Ttl -= dt;
        }

        void Fire(SyntheticShip ship, SyntheticShip target)
        {
            float dx = target.X - ship.X;
            float dy = target.Y - ship.Y;
            float distance = MathF.Max(1f, MathF.Sqrt(dx * dx + dy * dy));
            float nx = dx / distance;
            float ny = dy / distance;
            float jitter = Rng.NextFloat(-0.018f, 0.018f);
            float jx = nx * MathF.Cos(jitter) - ny * MathF.Sin(jitter);
            float jy = nx * MathF.Sin(jitter) + ny * MathF.Cos(jitter);
            Projectiles.Add(new SyntheticProjectile
            {
                Id = NextProjectileId++,
                Team = ship.Team,
                X = ship.X + jx * (ship.Radius + 8f),
                Y = ship.Y + jy * (ship.Radius + 8f),
                VX = ship.VX + jx * (620f + Rng.NextFloat(-25f, 25f)),
                VY = ship.VY + jy * (620f + Rng.NextFloat(-25f, 25f)),
                Radius = 5.5f,
                Damage = 13f + Rng.NextFloat(-1.5f, 2.5f),
                Ttl = 3.8f,
                Alive = true,
            });
            ship.Cooldown = 0.36f + ship.Radius * 0.003f + Rng.NextFloat(0.01f, 0.09f);
            ship.Heat += 0.08f;
        }

        void ResolveShipCollisions()
        {
            for (int i = 0; i < Ships.Count; ++i)
            {
                SyntheticShip a = Ships[i];
                if (!a.Alive) continue;
                for (int j = i + 1; j < Ships.Count; ++j)
                {
                    SyntheticShip b = Ships[j];
                    if (!b.Alive) continue;
                    float dx = b.X - a.X;
                    float dy = b.Y - a.Y;
                    float radius = a.Radius + b.Radius;
                    float distSq = dx * dx + dy * dy;
                    if (distSq >= radius * radius)
                        continue;

                    float dist = MathF.Max(0.001f, MathF.Sqrt(distSq));
                    float nx = dx / dist;
                    float ny = dy / dist;
                    float overlap = radius - dist;
                    float totalMass = a.Mass + b.Mass;
                    a.X -= nx * overlap * (b.Mass / totalMass);
                    a.Y -= ny * overlap * (b.Mass / totalMass);
                    b.X += nx * overlap * (a.Mass / totalMass);
                    b.Y += ny * overlap * (a.Mass / totalMass);

                    float rvx = b.VX - a.VX;
                    float rvy = b.VY - a.VY;
                    float rel = rvx * nx + rvy * ny;
                    if (rel < 0f)
                    {
                        float impulse = -(1.22f * rel) / (1f / a.Mass + 1f / b.Mass);
                        float ix = impulse * nx;
                        float iy = impulse * ny;
                        a.VX -= ix / a.Mass;
                        a.VY -= iy / a.Mass;
                        b.VX += ix / b.Mass;
                        b.VY += iy / b.Mass;
                    }

                    float scrape = MathF.Min(2.5f, overlap * 0.015f);
                    ApplyDamage(a, scrape);
                    ApplyDamage(b, scrape);
                    ++LastShipCollisions;
                }
            }
        }

        void ResolveProjectileHits()
        {
            for (int i = 0; i < Projectiles.Count; ++i)
            {
                SyntheticProjectile p = Projectiles[i];
                if (!p.Alive) continue;
                SyntheticShip? hit = null;
                float bestDistSq = float.MaxValue;
                for (int j = 0; j < Ships.Count; ++j)
                {
                    SyntheticShip ship = Ships[j];
                    if (!ship.Alive || ship.Team == p.Team)
                        continue;
                    float dx = ship.X - p.X;
                    float dy = ship.Y - p.Y;
                    float radius = ship.Radius + p.Radius;
                    float distSq = dx * dx + dy * dy;
                    if (distSq <= radius * radius && distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        hit = ship;
                    }
                }

                if (hit == null)
                    continue;

                ApplyDamage(hit, p.Damage);
                p.Alive = false;
                ++LastProjectileHits;
            }
        }

        void ApplyDamage(SyntheticShip ship, float damage)
        {
            if (damage <= 0f || !ship.Alive)
                return;
            float shieldDamage = MathF.Min(ship.Shield, damage);
            ship.Shield -= shieldDamage;
            ship.Hull -= damage - shieldDamage;
            if (ship.Hull <= 0f)
            {
                ship.Hull = 0f;
                ship.Alive = false;
                ship.VX = 0f;
                ship.VY = 0f;
            }
        }

        void ApplyArenaBoundary(SyntheticShip ship)
        {
            float dx = ship.X - ArenaCenterX;
            float dy = ship.Y - ArenaCenterY;
            float distSq = dx * dx + dy * dy;
            float radius = ArenaRadius - ship.Radius;
            if (distSq <= radius * radius)
                return;
            float dist = MathF.Max(0.001f, MathF.Sqrt(distSq));
            float nx = dx / dist;
            float ny = dy / dist;
            ship.X = ArenaCenterX + nx * radius;
            ship.Y = ArenaCenterY + ny * radius;
            float outward = ship.VX * nx + ship.VY * ny;
            if (outward > 0f)
            {
                ship.VX -= 1.85f * outward * nx;
                ship.VY -= 1.85f * outward * ny;
            }
        }

        SyntheticShip? NearestEnemy(SyntheticShip ship)
        {
            SyntheticShip? best = null;
            float bestDistSq = float.MaxValue;
            for (int i = 0; i < Ships.Count; ++i)
            {
                SyntheticShip candidate = Ships[i];
                if (!candidate.Alive || candidate.Team == ship.Team)
                    continue;
                float dx = candidate.X - ship.X;
                float dy = candidate.Y - ship.Y;
                float distSq = dx * dx + dy * dy;
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = candidate;
                }
            }
            return best;
        }

        public int AliveCount(int team)
        {
            int count = 0;
            for (int i = 0; i < Ships.Count; ++i)
                if (Ships[i].Team == team && Ships[i].Alive)
                    ++count;
            return count;
        }

        public SyntheticArenaHash ComputeHash()
        {
            var auth = new Hash128Checksum();
            auth.WriteInt(Frame);
            auth.WriteULong(Rng.State);
            auth.WriteInt(NextProjectileId);
            auth.WriteInt(LastShipCollisions);
            auth.WriteInt(LastProjectileHits);
            WriteShips(auth, -1);
            WriteProjectiles(auth);
            (ulong lo, ulong hi) = auth.Finish128();

            ulong simLane = LaneHash("Sim", c =>
            {
                c.WriteInt(Frame);
                c.WriteULong(Rng.State);
                c.WriteInt(NextProjectileId);
            });
            ulong playerLane = LaneHash("Player", c => WriteShips(c, 0));
            ulong enemyLane = LaneHash("Enemy", c => WriteShips(c, 1));
            ulong projectileLane = LaneHash("Projectiles", WriteProjectiles);
            ulong collisionLane = LaneHash("Collisions", c =>
            {
                c.WriteInt(LastShipCollisions);
                c.WriteInt(LastProjectileHits);
            });

            return new SyntheticArenaHash
            {
                AuthLo = lo,
                AuthHi = hi,
                DebugFull = DetRandom.Mix64(lo ^ hi ^ simLane ^ playerLane ^ enemyLane ^ projectileLane ^ collisionLane),
                Lanes = $"Sim:{Hex(simLane)},Player:{Hex(playerLane)},Enemy:{Hex(enemyLane)},Projectiles:{Hex(projectileLane)},Collisions:{Hex(collisionLane)}",
                PlayerDigest = DigestShips(0),
                EnemyDigest = DigestShips(1),
                ProjectileDigest = DigestProjectiles(),
            };
        }

        string DigestShips(int team)
        {
            var checksum = new Hash128Checksum();
            WriteShips(checksum, team);
            (ulong lo, ulong hi) = checksum.Finish128();
            return $"{Hex(hi)}:{Hex(lo)}";
        }

        string DigestProjectiles()
        {
            var checksum = new Hash128Checksum();
            WriteProjectiles(checksum);
            (ulong lo, ulong hi) = checksum.Finish128();
            return $"{Hex(hi)}:{Hex(lo)}";
        }

        ulong LaneHash(string name, Action<Hash128Checksum> write)
        {
            var checksum = new Hash128Checksum();
            checksum.WriteString(name);
            write(checksum);
            return checksum.Finish64();
        }

        void WriteShips(IDeterminismChecksum checksum, int team)
        {
            int count = 0;
            for (int i = 0; i < Ships.Count; ++i)
            {
                SyntheticShip ship = Ships[i];
                if (team >= 0 && ship.Team != team)
                    continue;
                ++count;
                checksum.WriteInt(ship.Id);
                checksum.WriteInt(ship.Team);
                checksum.WriteString(ship.Model);
                checksum.WriteBool(ship.Alive);
                checksum.FloatRaw(ship.X);
                checksum.FloatRaw(ship.Y);
                checksum.FloatRaw(ship.VX);
                checksum.FloatRaw(ship.VY);
                checksum.FloatRaw(ship.Rotation);
                checksum.FloatRaw(ship.AngularVelocity);
                checksum.FloatRaw(ship.Radius);
                checksum.FloatRaw(ship.Mass);
                checksum.FloatRaw(ship.Hull);
                checksum.FloatRaw(ship.Shield);
                checksum.FloatRaw(ship.Cooldown);
                checksum.FloatRaw(ship.Heat);
            }
            checksum.WriteInt(count);
        }

        void WriteProjectiles(IDeterminismChecksum checksum)
        {
            checksum.WriteInt(Projectiles.Count);
            for (int i = 0; i < Projectiles.Count; ++i)
            {
                SyntheticProjectile p = Projectiles[i];
                checksum.WriteInt(p.Id);
                checksum.WriteInt(p.Team);
                checksum.WriteBool(p.Alive);
                checksum.FloatRaw(p.X);
                checksum.FloatRaw(p.Y);
                checksum.FloatRaw(p.VX);
                checksum.FloatRaw(p.VY);
                checksum.FloatRaw(p.Radius);
                checksum.FloatRaw(p.Damage);
                checksum.FloatRaw(p.Ttl);
            }
        }

        static float NormalizeAngle(float radians)
        {
            while (radians > MathF.PI) radians -= MathF.Tau;
            while (radians < -MathF.PI) radians += MathF.Tau;
            return radians;
        }

        static float Clamp(float value, float min, float max)
            => value < min ? min : value > max ? max : value;
    }

    sealed class SyntheticShip
    {
        public int Id;
        public int Team;
        public string Model = "";
        public float X;
        public float Y;
        public float VX;
        public float VY;
        public float Rotation;
        public float AngularVelocity;
        public float Radius;
        public float Mass;
        public float Hull;
        public float Shield;
        public float Cooldown;
        public float Heat;
        public bool Alive;
    }

    sealed class SyntheticProjectile
    {
        public int Id;
        public int Team;
        public float X;
        public float Y;
        public float VX;
        public float VY;
        public float Radius;
        public float Damage;
        public float Ttl;
        public bool Alive;
    }
}

public sealed class SyntheticArenaHash
{
    public ulong AuthLo;
    public ulong AuthHi;
    public ulong DebugFull;
    public string Lanes = "";
    public string PlayerDigest = "";
    public string EnemyDigest = "";
    public string ProjectileDigest = "";
}
