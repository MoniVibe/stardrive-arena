using System;
using System.Globalization;
using System.IO;

namespace Ship_Game.Multiplayer.Authoritative;

public enum Authoritative4XQaFailureKind
{
    None,
    Coverage,
    Connection,
    EnvironmentMismatch,
    SyncMismatch,
    CommandRejected,
    Timeout,
    ProcessFailure,
    Unknown,
}

public sealed class Authoritative4XQaSummary
{
    public int FilesRead;
    public int OkLines;
    public int FailedLines;
    public int CommandLines;
    public int ResultLines;
    public int SyncMismatchLines;
    public int RawHashDriftLines;
    public int NetworkErrorLines;
    public int ViewPerfLines;
    public int FunctionalAssertLines;
    public int BudgetAssertLines;
    public int AutomationAssertLines;
    public int GovernorAssertLines;
    public int TradeAssertLines;
    public int DefenseAssertLines;
    public int BuildingQueueAssertLines;
    public int ShipyardAssertLines;
    public int DiplomacyAssertLines;
    public int HostAppliedCommands;
    public int JoinAppliedCommands;
    public float MaxDrawMs;
    public float MaxRenderMs;
    public float MaxOverlaysMs;
    public float MaxIconsMs;
    public float MaxFogMs;
    public string LastFinalHash = "";
    public string FirstDiff = "";
    public string EvidenceLine = "";
    public string MaxViewPerfLine = "";
    public Authoritative4XQaFailureKind FailureKind;
    public bool HasFunctionalCoverage
        => BudgetAssertLines > 0
           && AutomationAssertLines > 0
           && GovernorAssertLines > 0
           && TradeAssertLines > 0
           && DefenseAssertLines > 0
           && ShipyardAssertLines > 0
           && DiplomacyAssertLines > 0;

    public Authoritative4XQaFailureKind EffectiveFailureKind
    {
        get
        {
            if (FailureKind != Authoritative4XQaFailureKind.None)
                return FailureKind;
            if (OkLines == 0)
                return Authoritative4XQaFailureKind.Unknown;
            return HasFunctionalCoverage ? Authoritative4XQaFailureKind.None : Authoritative4XQaFailureKind.Coverage;
        }
    }

    public bool Passed => EffectiveFailureKind == Authoritative4XQaFailureKind.None;

    public string OneLine()
    {
        string status = Passed ? "PASS" : $"FAIL {EffectiveFailureKind}";
        return $"{status}: ok={OkLines} failed={FailedLines} commands={CommandLines} results={ResultLines} "
               + $"hostApplied={HostAppliedCommands} joinApplied={JoinAppliedCommands} syncMismatch={SyncMismatchLines} "
               + $"rawDrift={RawHashDriftLines} networkError={NetworkErrorLines} viewPerf={ViewPerfLines} "
               + $"asserts={FunctionalAssertLines} budget={BudgetAssertLines} automation={AutomationAssertLines} "
               + $"governor={GovernorAssertLines} trade={TradeAssertLines} defense={DefenseAssertLines} "
               + $"building={BuildingQueueAssertLines} shipyard={ShipyardAssertLines} diplomacy={DiplomacyAssertLines} "
               + $"maxDrawMs={MaxDrawMs:0.###} maxRenderMs={MaxRenderMs:0.###} maxOverlaysMs={MaxOverlaysMs:0.###} "
               + $"final='{LastFinalHash}' firstDiff='{FirstDiff}' evidence='{EvidenceLine}'";
    }
}

public static class Authoritative4XQaSummarizer
{
    public static Authoritative4XQaSummary SummarizeFiles(params string[] paths)
    {
        var summary = new Authoritative4XQaSummary();
        if (paths == null)
            return summary;

        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                continue;
            ++summary.FilesRead;
            SummarizeTextInto(summary, File.ReadAllText(path));
        }
        return summary;
    }

    public static Authoritative4XQaSummary SummarizeText(string text)
    {
        var summary = new Authoritative4XQaSummary();
        SummarizeTextInto(summary, text ?? "");
        return summary;
    }

    static void SummarizeTextInto(Authoritative4XQaSummary summary, string text)
    {
        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;
            SummarizeLine(summary, line);
        }
    }

    static void SummarizeLine(Authoritative4XQaSummary summary, string line)
    {
        if (line.Contains("[auth4x-probe] OK", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("OK ", StringComparison.OrdinalIgnoreCase))
        {
            ++summary.OkLines;
            string final = ValueAfter(line, "final=");
            if (!string.IsNullOrWhiteSpace(final))
                summary.LastFinalHash = final;
        }

        if (line.Contains("[auth4x-probe] FAILED", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("FAILED ", StringComparison.OrdinalIgnoreCase))
        {
            ++summary.FailedLines;
            MarkFailure(summary, Authoritative4XQaFailureKind.ProcessFailure, line);
        }

        if (line.Contains(" COMMAND ", StringComparison.Ordinal)
            || line.StartsWith("COMMAND ", StringComparison.Ordinal)
            || line.Contains("submit peer=", StringComparison.Ordinal)
            || line.Contains("applied peer=", StringComparison.Ordinal)
            || line.Contains("processed peer=", StringComparison.Ordinal))
            ++summary.CommandLines;

        if (line.StartsWith("assert ", StringComparison.OrdinalIgnoreCase))
        {
            ++summary.FunctionalAssertLines;
            if (line.Contains("category=budget", StringComparison.OrdinalIgnoreCase))
                ++summary.BudgetAssertLines;
            if (line.Contains("category=automation", StringComparison.OrdinalIgnoreCase))
                ++summary.AutomationAssertLines;
            if (line.Contains("category=governor", StringComparison.OrdinalIgnoreCase))
                ++summary.GovernorAssertLines;
            if (line.Contains("category=trade", StringComparison.OrdinalIgnoreCase))
                ++summary.TradeAssertLines;
            if (line.Contains("category=defense", StringComparison.OrdinalIgnoreCase))
                ++summary.DefenseAssertLines;
            if (line.Contains("category=building-queue", StringComparison.OrdinalIgnoreCase))
                ++summary.BuildingQueueAssertLines;
            if (line.Contains("category=shipyard", StringComparison.OrdinalIgnoreCase))
                ++summary.ShipyardAssertLines;
            if (line.Contains("category=diplomacy", StringComparison.OrdinalIgnoreCase))
                ++summary.DiplomacyAssertLines;
        }

        if (line.Contains(" RESULT ", StringComparison.Ordinal)
            || line.StartsWith("RESULT ", StringComparison.Ordinal))
            ++summary.ResultLines;

        if (line.Contains("applied peer=host", StringComparison.Ordinal))
            ++summary.HostAppliedCommands;
        if (line.Contains("applied peer=join", StringComparison.Ordinal))
            ++summary.JoinAppliedCommands;

        if (line.Contains("SYNC_MISMATCH", StringComparison.Ordinal)
            || line.Contains("Authoritative sync mismatch", StringComparison.OrdinalIgnoreCase))
        {
            ++summary.SyncMismatchLines;
            string diff = QuotedValue(line, "firstDiff='");
            if (string.IsNullOrWhiteSpace(diff))
                diff = ValueAfter(line, "firstDiff ");
            if (!string.IsNullOrWhiteSpace(diff) && string.IsNullOrWhiteSpace(summary.FirstDiff))
                summary.FirstDiff = diff;
            MarkFailure(summary, Authoritative4XQaFailureKind.SyncMismatch, line);
        }

        if (line.Contains("RAW_HASH_DRIFT", StringComparison.Ordinal))
            ++summary.RawHashDriftLines;

        if (line.Contains("NETWORK_ERROR", StringComparison.Ordinal)
            || line.Contains("connection attempt failed", StringComparison.OrdinalIgnoreCase)
            || line.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
            || line.Contains("No joiner connected", StringComparison.OrdinalIgnoreCase))
        {
            ++summary.NetworkErrorLines;
            MarkFailure(summary, Authoritative4XQaFailureKind.Connection, line);
        }

        if (line.Contains("environment mismatch", StringComparison.OrdinalIgnoreCase)
            || line.Contains("env mismatch", StringComparison.OrdinalIgnoreCase)
            || line.Contains("ValidateStartMessage", StringComparison.OrdinalIgnoreCase))
            MarkFailure(summary, Authoritative4XQaFailureKind.EnvironmentMismatch, line);

        if (line.Contains("Host rejected", StringComparison.OrdinalIgnoreCase)
            || line.Contains("accepted=False", StringComparison.Ordinal))
            MarkFailure(summary, Authoritative4XQaFailureKind.CommandRejected, line);

        if (line.Contains("Timed out waiting", StringComparison.OrdinalIgnoreCase)
            || line.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || line.Contains("TimeoutException", StringComparison.OrdinalIgnoreCase))
            MarkFailure(summary, Authoritative4XQaFailureKind.Timeout, line);

        if (line.Contains("VIEW_PERF", StringComparison.Ordinal))
        {
            ++summary.ViewPerfLines;
            float draw = Metric(line, "drawMs=");
            float render = Metric(line, "renderMs=");
            float overlays = Metric(line, "overlaysMs=");
            float icons = Metric(line, "iconsMs=");
            float fog = Metric(line, "fogMs=");
            if (draw > summary.MaxDrawMs)
            {
                summary.MaxDrawMs = draw;
                summary.MaxViewPerfLine = line;
            }
            summary.MaxRenderMs = Math.Max(summary.MaxRenderMs, render);
            summary.MaxOverlaysMs = Math.Max(summary.MaxOverlaysMs, overlays);
            summary.MaxIconsMs = Math.Max(summary.MaxIconsMs, icons);
            summary.MaxFogMs = Math.Max(summary.MaxFogMs, fog);
        }
    }

    static void MarkFailure(Authoritative4XQaSummary summary,
        Authoritative4XQaFailureKind kind, string evidence)
    {
        if (summary.FailureKind == Authoritative4XQaFailureKind.None
            || Priority(kind) > Priority(summary.FailureKind))
        {
            summary.FailureKind = kind;
            summary.EvidenceLine = evidence;
        }
    }

    static int Priority(Authoritative4XQaFailureKind kind)
        => kind switch
        {
            Authoritative4XQaFailureKind.SyncMismatch => 100,
            Authoritative4XQaFailureKind.EnvironmentMismatch => 90,
            Authoritative4XQaFailureKind.CommandRejected => 80,
            Authoritative4XQaFailureKind.Connection => 70,
            Authoritative4XQaFailureKind.Timeout => 60,
            Authoritative4XQaFailureKind.ProcessFailure => 50,
            Authoritative4XQaFailureKind.Coverage => 40,
            Authoritative4XQaFailureKind.Unknown => 10,
            _ => 0,
        };

    static string QuotedValue(string line, string prefix)
    {
        int start = line.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
            return "";
        start += prefix.Length;
        int end = line.IndexOf('\'', start);
        return end < 0 ? line[start..] : line[start..end];
    }

    static string ValueAfter(string line, string prefix)
    {
        int start = line.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            return "";
        start += prefix.Length;
        int end = line.IndexOf(' ', start);
        return end < 0 ? line[start..] : line[start..end];
    }

    static float Metric(string line, string prefix)
    {
        string value = ValueAfter(line, prefix);
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed)
            ? parsed
            : 0f;
    }
}
