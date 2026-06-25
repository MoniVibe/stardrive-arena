using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Ship_Game.Ships;

namespace UnitTests.Determinism;

/// <summary>
/// Lightweight battle "clip" recorder: samples ship positions / team / health over a headless autobattle and
/// emits a SELF-CONTAINED HTML file that plays the fight back as a top-down tactical view — colored dots that
/// move and wink out as ships die, with play/pause + scrub. No graphics device or video encoder needed: it's
/// pure captured data + a tiny embedded canvas player, so it works in the headless test harness.
/// </summary>
public sealed class BattleReplay
{
    readonly List<(Ship ship, int team)> Ships = new();
    readonly List<int[]> Frames = new(); // per registered ship: [x, y, hp0to100]
    public string Title = "Battle";
    public int FrameCount => Frames.Count;

    public void Register(IEnumerable<Ship> ships, int team)
    {
        foreach (Ship s in ships) Ships.Add((s, team));
    }

    // DYNAMIC ship set: ships may be registered AT ANY time during the run (e.g. per-wave enemies that re-spawn
    // each wave). A ship registered after Capture() calls already happened simply has NO entry in those earlier
    // frames (each frame is only as wide as the ship count AT capture time — a "ragged" frame list). Both writers
    // treat any ship index past a frame's recorded width as ABSENT (offscreen, opacity 0), so a late-registered
    // ship renders as absent for every frame before its first capture; the existing per-frame opacity/position
    // animation already makes it appear when its data starts and vanish when it dies. Identical to Register(...)
    // for callers that register everything up front (frames stay rectangular), so existing replays are unchanged.
    public void RegisterAt(IEnumerable<Ship> ships, int team) => Register(ships, team);

    public void Capture()
    {
        // Record ONLY the ships registered so far. Late-registered ships are absent in earlier (narrower) frames;
        // the writers back-pad them as absent so every ship shares the same keyframe count.
        var f = new int[Ships.Count * 3];
        for (int i = 0; i < Ships.Count; ++i)
        {
            Ship s = Ships[i].ship;
            f[i * 3]     = (int)s.Position.X;
            f[i * 3 + 1] = (int)s.Position.Y;
            f[i * 3 + 2] = s.Active ? (int)(s.HealthPercent * 100f) : 0;
        }
        Frames.Add(f);
    }

    // Health-percent of ship i in frame f, or 0 (ABSENT) if i was not yet registered when f was captured.
    static int Hp(int[] f, int i)   => i * 3 + 2 < f.Length ? f[i * 3 + 2] : 0;
    static int PosX(int[] f, int i) => i * 3     < f.Length ? f[i * 3]     : 0;
    static int PosY(int[] f, int i) => i * 3 + 1 < f.Length ? f[i * 3 + 1] : 0;

    public string WriteHtml(string outDir, string fileName)
    {
        Directory.CreateDirectory(outDir);

        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (int[] f in Frames)
            for (int i = 0; i < Ships.Count; ++i)
                if (Hp(f, i) > 0)
                {
                    x0 = Math.Min(x0, PosX(f, i)); x1 = Math.Max(x1, PosX(f, i));
                    y0 = Math.Min(y0, PosY(f, i)); y1 = Math.Max(y1, PosY(f, i));
                }
        if (x0 > x1) { x0 = y0 = 0; x1 = y1 = 1; }
        int padX = (int)((x1 - x0) * 0.08f) + 1, padY = (int)((y1 - y0) * 0.08f) + 1;
        x0 -= padX; x1 += padX; y0 -= padY; y1 += padY;

        var teams = new StringBuilder();
        for (int i = 0; i < Ships.Count; ++i) { if (i > 0) teams.Append(','); teams.Append(Ships[i].team); }

        // Emit RECTANGULAR frames (width = final ship count); back-pad late-registered ships as absent so the
        // embedded JS player can index every ship in every frame.
        var frames = new StringBuilder().Append('[');
        for (int fi = 0; fi < Frames.Count; ++fi)
        {
            if (fi > 0) frames.Append(',');
            int[] f = Frames[fi];
            frames.Append('[');
            for (int i = 0; i < Ships.Count; ++i)
            {
                if (i > 0) frames.Append(',');
                frames.Append(PosX(f, i)).Append(',').Append(PosY(f, i)).Append(',').Append(Hp(f, i));
            }
            frames.Append(']');
        }
        frames.Append(']');

        string html = HtmlTemplate
            .Replace("{TITLE}", System.Net.WebUtility.HtmlEncode(Title))
            .Replace("{TEAMS}", teams.ToString())
            .Replace("{FRAMES}", frames.ToString())
            .Replace("{X0}", x0.ToString()).Replace("{Y0}", y0.ToString())
            .Replace("{X1}", x1.ToString()).Replace("{Y1}", y1.ToString());

        string path = Path.GetFullPath(Path.Combine(outDir, fileName));
        File.WriteAllText(path, html);
        return path;
    }

    // A genuinely ANIMATED clip: a self-contained SVG that plays the fight via SMIL (each ship a dot that
    // moves along its recorded path, shrinks/fades with damage, and vanishes on death). Plays in any browser
    // or SVG viewer and can be rendered inline. frameStride downsamples frames to keep the file small.
    public string WriteAnimatedSvg(string outDir, string fileName, int w = 640, int h = 400, float fps = 12f, int frameStride = 2)
    {
        Directory.CreateDirectory(outDir);
        CultureInfo inv = CultureInfo.InvariantCulture;

        var fr = new List<int[]>();
        for (int f = 0; f < Frames.Count; f += Math.Max(1, frameStride)) fr.Add(Frames[f]);
        if (fr.Count < 2 && Frames.Count > 0) fr.Add(Frames[^1]);

        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (int[] f in fr)
            for (int i = 0; i < Ships.Count; ++i)
                if (Hp(f, i) > 0)
                {
                    x0 = Math.Min(x0, PosX(f, i)); x1 = Math.Max(x1, PosX(f, i));
                    y0 = Math.Min(y0, PosY(f, i)); y1 = Math.Max(y1, PosY(f, i));
                }
        if (x0 > x1) { x0 = y0 = 0; x1 = y1 = 1; }
        int px = (int)((x1 - x0) * 0.08f) + 1, py = (int)((y1 - y0) * 0.08f) + 1;
        x0 -= px; x1 += px; y0 -= py; y1 += py;
        float dur = Math.Max(2f, fr.Count / fps);

        int SX(int vx) => (int)(8 + (vx - x0) / (float)(x1 - x0) * (w - 16));
        int SY(int vy) => (int)(8 + (vy - y0) / (float)(y1 - y0) * (h - 16));
        string[] col = { "#49a9ff", "#ff5b5b", "#3ad888", "#ffbb33", "#b06bff", "#9aa0aa" };

        var sb = new StringBuilder();
        sb.Append($"<svg viewBox=\"0 0 {w} {h}\" xmlns=\"http://www.w3.org/2000/svg\" role=\"img\">");
        sb.Append($"<title>{System.Net.WebUtility.HtmlEncode(Title)}</title>");
        sb.Append($"<rect width=\"{w}\" height=\"{h}\" fill=\"#0b0f1a\"/>");
        for (int i = 0; i < Ships.Count; ++i)
        {
            var pos = new StringBuilder(); var rad = new StringBuilder(); var op = new StringBuilder();
            for (int f = 0; f < fr.Count; ++f)
            {
                int[] frame = fr[f];
                int x = PosX(frame, i), y = PosY(frame, i), hp = Hp(frame, i); // ragged-safe: absent => hp 0
                if (f > 0) { pos.Append(';'); rad.Append(';'); op.Append(';'); }
                pos.Append(SX(x)).Append(',').Append(SY(y));
                rad.Append(hp > 0 ? 3 + hp / 22 : 0);
                op.Append(hp > 0 ? ((35 + 65 * hp / 100) / 100f).ToString("0.00", inv) : "0");
            }
            sb.Append($"<circle cx=\"0\" cy=\"0\" r=\"4\" fill=\"{col[Ships[i].team % col.Length]}\">");
            sb.Append($"<animateMotion dur=\"{dur.ToString("0.0", inv)}s\" repeatCount=\"indefinite\" calcMode=\"linear\" values=\"{pos}\"/>");
            sb.Append($"<animate attributeName=\"r\" dur=\"{dur.ToString("0.0", inv)}s\" repeatCount=\"indefinite\" calcMode=\"linear\" values=\"{rad}\"/>");
            sb.Append($"<animate attributeName=\"fill-opacity\" dur=\"{dur.ToString("0.0", inv)}s\" repeatCount=\"indefinite\" calcMode=\"linear\" values=\"{op}\"/>");
            sb.Append("</circle>");
        }
        sb.Append("</svg>");

        string path = Path.GetFullPath(Path.Combine(outDir, fileName));
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    const string HtmlTemplate = @"<!doctype html><html><head><meta charset=""utf-8""><title>{TITLE}</title>
<style>body{margin:0;background:#0b0f1a;color:#cdd;font:13px system-ui;text-align:center}
canvas{background:#0b0f1a;display:block;margin:0 auto}#bar{padding:8px}button{cursor:pointer}</style></head>
<body><div id=bar><b>{TITLE}</b> &nbsp; <button id=pp>&#9208;</button> &nbsp; frame <span id=fn>0</span>/<span id=ft>0</span>
&nbsp; <input id=sl type=range min=0 value=0 style=""width:55%""></div>
<canvas id=c width=980 height=660></canvas>
<script>
const TEAMS=[{TEAMS}], FR={FRAMES}, B={x0:{X0},y0:{Y0},x1:{X1},y1:{Y1}};
const COL=['#49f','#f55','#4d8','#fb3','#b6f','#999'];
const cv=document.getElementById('c'),g=cv.getContext('2d'),W=cv.width,H=cv.height,P=28;
const SX=v=>P+(v-B.x0)/(B.x1-B.x0)*(W-2*P), SY=v=>P+(v-B.y0)/(B.y1-B.y0)*(H-2*P);
const sl=document.getElementById('sl'),fn=document.getElementById('fn'),ft=document.getElementById('ft'),pp=document.getElementById('pp');
let i=0,play=true; sl.max=FR.length-1; ft.textContent=FR.length-1;
function draw(){g.clearRect(0,0,W,H);const f=FR[i];for(let k=0;k<TEAMS.length;k++){const hp=f[k*3+2];if(hp<=0)continue;
g.beginPath();g.fillStyle=COL[TEAMS[k]%COL.length];g.globalAlpha=0.3+0.7*hp/100;g.arc(SX(f[k*3]),SY(f[k*3+1]),3+5*hp/100,0,7);g.fill();}
g.globalAlpha=1;fn.textContent=i;sl.value=i;}
function loop(){if(play){i=(i+1)%FR.length;draw();}setTimeout(loop,66);}
pp.onclick=()=>{play=!play;pp.innerHTML=play?'&#9208;':'&#9654;';};
sl.oninput=()=>{play=false;pp.innerHTML='&#9654;';i=+sl.value;draw();};
draw();loop();
</script></body></html>";
}
