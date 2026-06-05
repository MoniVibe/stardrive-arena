using System;
using System.Reflection;
using Microsoft.Xna.Framework.Graphics;
using SharpDX.DXGI;
using D3D11Device = SharpDX.Direct3D11.Device;
using DxgiDevice = SharpDX.DXGI.Device;

namespace Ship_Game.Graphics;

/// <summary>
/// Best-effort live memory-pressure probes used to decide whether an expensive
/// content unload/reload is worth doing (e.g. when leaving a game to the main menu).
/// Every probe is conservative: if it cannot positively confirm there is headroom it
/// reports zero headroom, so the caller takes the safe path.
/// </summary>
public static class MemoryPressure
{
    /// <summary>
    /// Free system RAM (MB) that must remain below the GC's high-memory-load threshold to
    /// keep the in-game content cache resident. This is absolute headroom, NOT a percentage:
    /// 79% load on a 32 GB machine still leaves gigabytes free, while the same percentage on
    /// a 6 GB machine is dangerous. The margin must cover MainMenu's worst-case atlas
    /// decompress (a Combined Arms DXT5 atlas expands ~4x into a transient buffer) plus slack.
    /// </summary>
    public const double MinSystemRamHeadroomMB = 2048;

    /// <summary>Free VRAM budget (MB) required to keep the cache - covers MainMenu's atlas upload.</summary>
    public const double MinVramHeadroomMB = 1024;

    const double MB = 1024.0 * 1024.0;

    /// <summary>
    /// TRUE only when we are confident BOTH system RAM and VRAM have enough absolute headroom
    /// that keeping the in-game content cache resident will not risk an out-of-memory. Returns
    /// FALSE (= please free memory first) whenever either resource is tight OR a probe could
    /// not be evaluated on this machine/runtime.
    /// </summary>
    public static bool HasHeadroomToKeepContent(GraphicsDevice device)
    {
        Log.Info("ExitToMain memory check:");
        bool ramOk  = TryGetSystemRamHeadroomMB(out double ramFreeMB)      && ramFreeMB  >= MinSystemRamHeadroomMB;
        bool vramOk = TryGetVramHeadroomMB(device, out double vramFreeMB)  && vramFreeMB >= MinVramHeadroomMB;
        bool keep = ramOk && vramOk;
        Log.Info($"  decision: RAM free={ramFreeMB:0}MB (need {MinSystemRamHeadroomMB:0}, ok={ramOk}) "
                 + $"VRAM free={vramFreeMB:0}MB (need {MinVramHeadroomMB:0}, ok={vramOk}) "
                 + $"=> {(keep ? "KEEP cache (fast exit)" : "FREE cache (safe exit)")}");
        return keep;
    }

    /// <summary>
    /// Free system RAM (MB) below the GC's high-memory-load threshold - the point past which
    /// the GC stops growing the heap and large allocations start throwing OOM even when the
    /// managed heap itself is healthy. Negative if already over the threshold.
    /// </summary>
    public static bool TryGetSystemRamHeadroomMB(out double headroomMB)
    {
        headroomMB = 0; // assume worst case until proven otherwise
        try
        {
            GCMemoryInfo info = GC.GetGCMemoryInfo();
            long threshold = info.HighMemoryLoadThresholdBytes;
            if (threshold <= 0)
            {
                Log.Warning("  RAM probe: HighMemoryLoadThresholdBytes <= 0");
                return false;
            }
            headroomMB = (threshold - info.MemoryLoadBytes) / MB;
            Log.Info($"  RAM: load={info.MemoryLoadBytes / MB:0}MB / threshold={threshold / MB:0}MB "
                     + $"(total={info.TotalAvailableMemoryBytes / MB:0}MB, managed heap={info.HeapSizeBytes / MB:0}MB) "
                     + $"=> free={headroomMB:0}MB");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"  RAM probe failed: {ex.GetType().Name}: {ex.Message}");
            return false; // GCMemoryInfo unsupported on this runtime
        }
    }

    /// <summary>
    /// Free local VRAM (MB) below this process's DXGI budget. Reflectively pulls the MonoGame
    /// WindowsDX backend's SharpDX D3D11 device (the same field the device-removed diagnostic
    /// uses) and queries IDXGIAdapter3::QueryVideoMemoryInfo. Negative if already over budget.
    /// </summary>
    public static bool TryGetVramHeadroomMB(GraphicsDevice device, out double headroomMB)
    {
        headroomMB = 0; // assume worst case until proven otherwise
        if (device == null)
            return false;
        try
        {
            FieldInfo fld = typeof(GraphicsDevice).GetField("_d3dDevice",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (fld?.GetValue(device) is not D3D11Device d3d)
            {
                Log.Warning("  VRAM probe: MonoGame _d3dDevice field not found / not a SharpDX device");
                return false;
            }

            // Each QueryInterface / Adapter hands back a separately ref-counted COM wrapper;
            // disposing them releases only that extra ref, never the live D3D11 device.
            using DxgiDevice dxgiDevice = d3d.QueryInterface<DxgiDevice>();
            using Adapter adapter = dxgiDevice.Adapter;
            using Adapter3 adapter3 = adapter.QueryInterface<Adapter3>();
            QueryVideoMemoryInformation mem = adapter3.QueryVideoMemoryInfo(0, MemorySegmentGroup.Local);
            if (mem.Budget <= 0)
            {
                Log.Warning("  VRAM probe: DXGI reported Budget <= 0");
                return false;
            }
            headroomMB = (mem.Budget - mem.CurrentUsage) / MB;
            Log.Info($"  VRAM: usage={mem.CurrentUsage / MB:0}MB / budget={mem.Budget / MB:0}MB => free={headroomMB:0}MB");
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning($"  VRAM probe failed: {ex.GetType().Name}: {ex.Message}");
            return false; // pre-DXGI-1.4 GPU/driver, or the MonoGame field shape changed
        }
    }
}
