using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;
using Ship_Game.UI;
using Vector2 = SDGraphics.Vector2;
using Rectangle = SDGraphics.Rectangle;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaMultiplayerLobbyScreen : GameScreen
{
    public const int DefaultPort = 47377;
    public const int DefaultTurns = 600;
    const string DefaultHost = "127.0.0.1";

    readonly object Sync = new();
    UITextEntry HostEntry;
    UITextEntry PortEntry;
    UITextEntry TurnsEntry;
    Task<ArenaMultiplayerRunResult> ActiveRun;
    ArenaMultiplayerRunResult LastResult;
    string StatusText = "Idle. Host on one machine, join from the other.";

    public ArenaMultiplayerLobbyScreen() : base(null, toPause: null)
    {
        CanEscapeFromScreen = true;
        TransitionOnTime  = 0.2f;
        TransitionOffTime = 0.2f;
    }

    public string CurrentStatus
    {
        get { lock (Sync) return StatusText; }
    }

    public bool IsRunning
    {
        get { lock (Sync) return ActiveRun != null; }
    }

    public ArenaMultiplayerRunResult LatestResult => LastResult;

    public override void LoadContent()
    {
        RemoveAll();

        Vector2 c = ScreenCenter;
        var panel = new RectF(c.X - 390, c.Y - 255, 780, 510);
        Add(ArenaTheme.Panel(panel));
        Add(ArenaTheme.ArenaTitle(new Vector2(panel.X + 24, panel.Y + 26), "STAR GLADIATOR"));
        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 76), "MULTIPLAYER LOCKSTEP"));

        AddField(panel.X + 24, panel.Y + 120, "HOST", DefaultHost, out HostEntry, allowPeriod: true, "arena_mp_host_entry");
        AddField(panel.X + 24, panel.Y + 178, "PORT", DefaultPort.ToString(), out PortEntry, allowPeriod: false, "arena_mp_port_entry");
        AddField(panel.X + 224, panel.Y + 178, "TURNS", DefaultTurns.ToString(), out TurnsEntry, allowPeriod: false, "arena_mp_turns_entry");

        Add(ArenaTheme.StatChip(new RectF(panel.X + 24, panel.Y + 236, 170, 54),
            "ROLE", () => IsRunning ? "RUNNING" : "READY", IsRunning ? ArenaTheme.Cyan : ArenaTheme.Green));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 210, panel.Y + 236, 220, 54),
            "LAST HASH", () => LastResult?.FinalHash.NotEmpty() == true ? LastResult.FinalHash : "-", ArenaTheme.Amber));
        Add(ArenaTheme.StatChip(new RectF(panel.X + 446, panel.Y + 236, 170, 54),
            "WINNER", () => LastResult == null || !LastResult.MatchEnded ? "-" : LastResult.WinnerPeerId.ToString(), ArenaTheme.Magenta));

        Add(ArenaTheme.SectionHeader(new Vector2(panel.X + 24, panel.Y + 318), "STATUS"));
        for (int i = 0; i < 4; ++i)
        {
            int line = i;
            Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 346 + i * 20),
                StatusLine(line), ArenaTheme.BodySmallFont, ArenaTheme.TextSecondary)
            {
                DynamicText = _ => StatusLine(line),
            });
        }

        UIList actions = AddList(new Vector2(panel.X + 24, panel.Bottom - 58));
        actions.Direction = new Vector2(1f, 0f);
        actions.Padding = new Vector2(8f, 8f);
        actions.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPrimaryButton(actions, "HOST", _ => StartHost(), 118f).Name = "arena_mp_host";
        ArenaTheme.AddPillButton(actions, "JOIN", _ => StartJoin(), 118f).Name = "arena_mp_join";
        ArenaTheme.AddPillButton(actions, "SELF TEST", _ => StartSelfTest(), 136f).Name = "arena_mp_self_test";
        ArenaTheme.AddPillButton(actions, "BACK", _ => ExitScreen(), 100f).Name = "arena_mp_back";
    }

    void AddField(float x, float y, string label, string value, out UITextEntry entry, bool allowPeriod, string name)
    {
        Add(ArenaTheme.Label(new Vector2(x, y), label));
        var bg = new Submenu(new RectF(x, y + 18, label == "HOST" ? 360 : 160, 26));
        entry = new UITextEntry(new Rectangle((int)x + 6, (int)y + 20, (int)bg.Width - 12, 24),
            ArenaTheme.BodyFont, value)
        {
            Name = name,
            AllowPeriod = allowPeriod,
            MaxCharacters = label == "HOST" ? 64 : 6,
            DrawUnderline = true,
            AutoCaptureOnHover = true,
            Color = ArenaTheme.TextPrimary,
            HoverColor = ArenaTheme.AmberBright,
            InputColor = ArenaTheme.Cyan,
            Background = bg,
        };
        Add(entry);
    }

    void StartSelfTest()
    {
        int turns = ParseTurns();
        StartRun("SELF TEST", () => RunLocalSelfTestForHeadless(turns));
    }

    void StartHost()
    {
        int port = ParsePort();
        int turns = ParseTurns();
        ArenaMultiplayerSettings settings = CreateDefaultSettings(turns);
        StartRun("HOST", () => ArenaMultiplayerSession.RunNetworkHost(settings, port, AppendLog));
    }

    void StartJoin()
    {
        string host = HostEntry?.Text?.Trim();
        if (host.IsEmpty())
            host = DefaultHost;
        int port = ParsePort();
        StartRun("JOIN", () => ArenaMultiplayerSession.RunNetworkJoin(host, port, AppendLog));
    }

    void StartRun(string mode, Func<ArenaMultiplayerRunResult> run)
    {
        lock (Sync)
        {
            if (ActiveRun != null)
            {
                GameAudio.NegativeClick();
                StatusText = "A multiplayer run is already active.";
                return;
            }

            StatusText = $"{mode}: starting...";
            LastResult = null;
            ActiveRun = Task.Run(run);
        }
        GameAudio.AffirmativeClick();
    }

    public override void Update(float fixedDeltaTime)
    {
        base.Update(fixedDeltaTime);

        Task<ArenaMultiplayerRunResult> completed = null;
        lock (Sync)
        {
            if (ActiveRun?.IsCompleted == true)
            {
                completed = ActiveRun;
                ActiveRun = null;
            }
        }

        if (completed == null)
            return;

        try
        {
            LastResult = completed.GetAwaiter().GetResult();
            SetStatus(Summarize(LastResult));
        }
        catch (Exception e)
        {
            SetStatus("FAILED: " + e.GetBaseException().Message);
        }
    }

    int ParsePort()
        => int.TryParse(PortEntry?.Text, out int port) ? port.Clamped(1, 65535) : DefaultPort;

    int ParseTurns()
        => int.TryParse(TurnsEntry?.Text, out int turns) ? turns.Clamped(30, 2000) : DefaultTurns;

    void AppendLog(string text)
    {
        if (text.IsEmpty())
            return;
        SetStatus(text);
    }

    void SetStatus(string text)
    {
        lock (Sync)
            StatusText = text ?? "";
    }

    string StatusLine(int index)
    {
        string text;
        lock (Sync)
            text = StatusText ?? "";
        string[] lines = text.Split('\n');
        return index >= 0 && index < lines.Length ? lines[index] : "";
    }

    static string Summarize(ArenaMultiplayerRunResult result)
    {
        if (result == null)
            return "No result.";
        if (result.Desynced)
            return $"DESYNC turn {result.DesyncTurn}\n{result.DesyncReason}\nfinal {result.FinalHash}";
        string winner = result.MatchEnded ? result.WinnerPeerId.ToString() : "none";
        return $"COMPLETE turns {result.TurnsCompleted}\nwinner {winner} ended {result.MatchEnded}\nfinal {result.FinalHash}";
    }

    public static ArenaMultiplayerSettings CreateDefaultSettings(int turns = DefaultTurns)
        => new()
        {
            MatchSeed = 0x5EED,
            RngSeed = 0xA12EA000u,
            InputDelay = 3,
            MaxTurns = turns.Clamped(30, 2000),
            CommandEveryTurns = 1,
            HostFleetDesignNames = DefaultFleetNames(0x1001ul),
            JoinFleetDesignNames = DefaultFleetNames(0x2002ul),
        };

    public static ArenaMultiplayerRunResult RunLocalSelfTestForHeadless(int turns = 90)
        => ArenaMultiplayerSession.RunInProcess(CreateDefaultSettings(turns));

    static string[] DefaultFleetNames(ulong seed)
    {
        try
        {
            return CareerManager.StartingRosterDesigns(ArenaStartArchetype.Wingmates, seed)
                .Select(d => d.Name)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public override void ExitScreen()
    {
        base.ExitScreen();
        ScreenManager.GoToScreen(new Ship_Game.GameScreens.MainMenu.MainMenuScreen(), clear3DObjects: true);
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        ScreenManager.FadeBackBufferToBlack(TransitionAlpha * 2 / 3);
        batch.SafeBegin();
        base.Draw(batch, elapsed);
        batch.SafeEnd();
    }
}
