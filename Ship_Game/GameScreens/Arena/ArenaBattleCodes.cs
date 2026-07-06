using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SDLockstep;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// Copy/paste replay descriptor for deterministic Arena multiplayer brawls. The code contains
/// only the match setup, fleets, build fingerprint, and the expected turn-sequence digest; the
/// actual replay is produced by re-running the deterministic sim from the imported descriptor.
/// </summary>
public static class ArenaBattleCodes
{
    const string Prefix = "SGB1-";
    const string Magic = "SGB1";
    // 2: Arena P1 RulesetV0 + canonical design bundles (folded into SettingsHash), appended after
    // the existing fields so a format-1 code still parses (missing fields -> defaults).
    const int FormatVersion = 2;

    public static string Export(ArenaMultiplayerSettings settings, string sequenceSha256)
        => Export(settings, sequenceSha256, buildHashOverride: null);

    public static string Export(ArenaMultiplayerSettings settings, string sequenceSha256,
        string buildHashOverride)
    {
        settings = (settings ?? new ArenaMultiplayerSettings()).WithResolvedFleets();
        SessionStartMessage start = settings.ToStartMessage();
        string buildHash = buildHashOverride ?? start.BuildHash ?? "";
        string digest = NormalizeSha256(sequenceSha256);
        if (digest.Length != 64)
            throw new ArgumentException("A 64-character SHA-256 turn sequence digest is required.",
                nameof(sequenceSha256));

        using var payload = new MemoryStream();
        using (var w = new BinaryWriter(payload, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(Magic);
            w.Write(FormatVersion);
            w.Write(start.ProtocolVersion);
            w.Write(start.MatchSeed);
            w.Write(start.RngSeed);
            w.Write(start.InputDelay);
            w.Write(start.MaxTurns);
            w.Write(start.CommandEveryTurns);
            w.Write(start.GameSpeed);
            w.Write(start.StartPaused);
            WriteString(w, start.SettingsHash);
            WriteString(w, buildHash);
            WriteString(w, start.BuildSummary);
            WriteString(w, start.HostRacePreference);
            WriteString(w, start.JoinRacePreference);
            WriteString(w, start.HostLoadoutTrait);
            WriteString(w, start.JoinLoadoutTrait);
            WriteString(w, start.HostFleet);
            WriteString(w, start.JoinFleet);
            // Arena P1 RulesetV0 + design bundles (format 2). Order matches the TryImport reader.
            w.Write(start.RulesetVersion);
            w.Write(start.RulesetMode);
            w.Write(start.RulesetBudgetModel);
            w.Write(start.RulesetBudgetCredits);
            w.Write(start.RulesetRosterSource);
            w.Write(start.RulesetCountdownSeconds);
            w.Write(start.RulesetMaxMatchSeconds);
            w.Write(start.RulesetMaxFleetShipsPerSide);
            w.Write(start.RulesetWagerCredits);
            WriteString(w, start.RulesetCommitmentHash);
            WriteString(w, start.RulesetContentFingerprint);
            WriteString(w, start.HostFleetBundle);
            WriteString(w, start.JoinFleetBundle);
            WriteString(w, digest);
        }

        return Prefix + Base64UrlEncode(payload.ToArray());
    }

    public static bool TryImport(string code, out ArenaBattleCode imported)
        => TryImport(code, out imported, currentBuildHashOverride: null);

    public static bool TryImport(string code, out ArenaBattleCode imported,
        string currentBuildHashOverride)
    {
        imported = null;
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(Prefix, StringComparison.Ordinal))
        {
            imported = ArenaBattleCode.Failure("Battle code must start with SGB1-.");
            return false;
        }

        try
        {
            byte[] bytes = Base64UrlDecode(code.Substring(Prefix.Length).Trim());
            using var payload = new MemoryStream(bytes, writable: false);
            using var r = new BinaryReader(payload, Encoding.UTF8);
            string magic = r.ReadString();
            int format = r.ReadInt32();
            if (!string.Equals(magic, Magic, StringComparison.Ordinal) || format < 1 || format > FormatVersion)
            {
                imported = ArenaBattleCode.Failure("Unsupported battle code format.");
                return false;
            }

            var start = new SessionStartMessage
            {
                ProtocolVersion = r.ReadInt32(),
                MatchSeed = r.ReadInt32(),
                RngSeed = r.ReadUInt32(),
                InputDelay = r.ReadInt32(),
                MaxTurns = r.ReadInt32(),
                CommandEveryTurns = r.ReadInt32(),
                GameSpeed = r.ReadSingle(),
                StartPaused = r.ReadBoolean(),
                SettingsHash = ReadString(r),
                BuildHash = ReadString(r),
                BuildSummary = ReadString(r),
                HostRacePreference = ReadString(r),
                JoinRacePreference = ReadString(r),
                HostLoadoutTrait = ReadString(r),
                JoinLoadoutTrait = ReadString(r),
                HostFleet = ReadString(r),
                JoinFleet = ReadString(r),
            };
            if (format >= 2)
            {
                start.RulesetVersion = r.ReadInt32();
                start.RulesetMode = r.ReadInt32();
                start.RulesetBudgetModel = r.ReadInt32();
                start.RulesetBudgetCredits = r.ReadInt32();
                start.RulesetRosterSource = r.ReadInt32();
                start.RulesetCountdownSeconds = r.ReadInt32();
                start.RulesetMaxMatchSeconds = r.ReadInt32();
                start.RulesetMaxFleetShipsPerSide = r.ReadInt32();
                start.RulesetWagerCredits = r.ReadInt32();
                start.RulesetCommitmentHash = ReadString(r);
                start.RulesetContentFingerprint = ReadString(r);
                start.HostFleetBundle = ReadString(r);
                start.JoinFleetBundle = ReadString(r);
            }
            string sequenceSha256 = NormalizeSha256(ReadString(r));
            if (payload.Position != payload.Length)
            {
                imported = ArenaBattleCode.Failure("Battle code contains unexpected trailing data.");
                return false;
            }
            if (sequenceSha256.Length != 64)
            {
                imported = ArenaBattleCode.Failure("Battle code is missing a valid replay digest.");
                return false;
            }

            ArenaMultiplayerSettings settings = ArenaMultiplayerSettings.FromStartMessage(start)
                .WithResolvedFleets();
            if (!string.Equals(start.SettingsHash, settings.SettingsHash, StringComparison.Ordinal))
            {
                imported = ArenaBattleCode.Failure(
                    $"Battle code settings hash mismatch. Encoded {start.SettingsHash}, decoded {settings.SettingsHash}.");
                return false;
            }

            string currentBuildHash = currentBuildHashOverride ?? ArenaMultiplayerPeerSignature.Hash(settings);
            string warning = "";
            if (!string.Equals(start.BuildHash, currentBuildHash, StringComparison.Ordinal))
                warning = "Your CA/BlackBox/Arena build differs from the battle code; replay may desync.";

            imported = new ArenaBattleCode(settings, start, sequenceSha256, warning, "");
            return true;
        }
        catch (Exception e) when (e is EndOfStreamException or FormatException or IOException
                                      or ArgumentException or OverflowException)
        {
            imported = ArenaBattleCode.Failure($"Malformed battle code: {e.Message}");
            return false;
        }
    }

    public static string SequenceSha256(ArenaMultiplayerRunResult result)
    {
        if (result == null)
            return "";

        var sb = new StringBuilder();
        foreach (ArenaMultiplayerTurnHash h in result.TurnHashes)
        {
            sb.Append(h.Turn).Append('|')
              .Append(h.HostHi.ToString("X16", CultureInfo.InvariantCulture)).Append(':')
              .Append(h.HostLo.ToString("X16", CultureInfo.InvariantCulture)).Append('|')
              .Append(h.JoinHi.ToString("X16", CultureInfo.InvariantCulture)).Append(':')
              .Append(h.JoinLo.ToString("X16", CultureInfo.InvariantCulture)).AppendLine();
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())))
            .ToLowerInvariant();
    }

    public static ArenaBattleCodeReplayCheck VerifyReplay(ArenaBattleCode code)
    {
        if (code == null || code.Settings == null)
            return new ArenaBattleCodeReplayCheck(false, "", "No imported battle code.");

        ArenaMultiplayerRunResult replay = ArenaMultiplayerSession.RunInProcess(code.Settings);
        string digest = SequenceSha256(replay);
        bool match = string.Equals(digest, code.SequenceSha256, StringComparison.OrdinalIgnoreCase);
        string error = match ? "" : $"Replay digest mismatch. Expected {code.SequenceSha256}, got {digest}.";
        return new ArenaBattleCodeReplayCheck(match, digest, error);
    }

    static string NormalizeSha256(string text)
        => (text ?? "").Trim().ToLowerInvariant();

    static void WriteString(BinaryWriter w, string value)
        => w.Write(value ?? "");

    static string ReadString(BinaryReader r)
        => r.ReadString() ?? "";

    static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    static byte[] Base64UrlDecode(string text)
    {
        string b64 = (text ?? "").Replace('-', '+').Replace('_', '/');
        switch (b64.Length % 4)
        {
            case 2: b64 += "=="; break;
            case 3: b64 += "="; break;
            case 0: break;
            default: throw new FormatException("Invalid base64url length.");
        }
        return Convert.FromBase64String(b64);
    }
}

public sealed class ArenaBattleCode
{
    public readonly ArenaMultiplayerSettings Settings;
    public readonly SessionStartMessage StartMessage;
    public readonly string SequenceSha256;
    public readonly string BuildWarning;
    public readonly string Error;

    public bool HasBuildWarning => !string.IsNullOrWhiteSpace(BuildWarning);

    public ArenaBattleCode(ArenaMultiplayerSettings settings, SessionStartMessage startMessage,
        string sequenceSha256, string buildWarning, string error)
    {
        Settings = settings;
        StartMessage = startMessage;
        SequenceSha256 = sequenceSha256 ?? "";
        BuildWarning = buildWarning ?? "";
        Error = error ?? "";
    }

    public static ArenaBattleCode Failure(string error)
        => new(null, null, "", "", error ?? "");
}

public readonly struct ArenaBattleCodeReplayCheck
{
    public readonly bool Match;
    public readonly string SequenceSha256;
    public readonly string Error;

    public ArenaBattleCodeReplayCheck(bool match, string sequenceSha256, string error)
    {
        Match = match;
        SequenceSha256 = sequenceSha256 ?? "";
        Error = error ?? "";
    }
}
