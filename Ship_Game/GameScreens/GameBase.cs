using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SDUtils;
using Ship_Game.Audio;
using Ship_Game.Data;
using SynapseGaming.LightingSystem.Core;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game
{
    public class GameBase : Game
    {
        #pragma warning disable CA2213 // managed by Game
        public GraphicsDeviceManager Graphics;
        #pragma warning restore CA2213
        LightingSystemPreferences Preferences;
        public static ScreenManager ScreenManager;
        public ScreenManager Manager => ScreenManager;

        // This is equivalent to PresentationParameters.BackBufferWidth
        public static int ScreenWidth  { get; protected set; }
        public static int ScreenHeight { get; protected set; }
        public static Viewport Viewport;
        public static Vector2 ScreenSize   { get; protected set; }
        public static Vector2 ScreenCenter { get; protected set; }
        public static int MainThreadId { get; protected set; }

        public static GameBase Base;
        public new GameContentManager Content { get; }
        public static GameContentManager GameContent => Base?.Content;

        public int FrameId { get; protected set; }
        public UpdateTimes Elapsed { get; protected set; }

        /// <summary>
        /// Total elapsed Game time while the Game window has been active
        /// </summary>
        public float TotalElapsed { get; protected set; }

        public Form Form => (Form)Control.FromHandle(Window.Handle);

        /// <summary>
        /// TRUE if GraphicsDevice is not null or disposed
        /// </summary>
        public bool IsDeviceGood => GraphicsDevice is { IsDisposed: false, GraphicsDeviceStatus: GraphicsDeviceStatus.Normal };

        public GameBase()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            Base = this;

            string contentDir = Path.Combine(Directory.GetCurrentDirectory(), "Content");
            base.Content = Content = new GameContentManager(Services, "Game", contentDir);

            Graphics = new GraphicsDeviceManager(this)
            {
                PreferredDepthStencilFormat = DepthFormat.Depth16, // only supported: Depth24Stencil8,
            };
            Graphics.PreferMultiSampling = true;
            Graphics.GraphicsProfile = GraphicsProfile.HiDef;
            Graphics.ApplyChanges();
        }

        void UpdateRendererPreferences(ref GraphicsSettings settings)
        {
            var p = new LightingSystemPreferences
            {
                MaxAnisotropy   = settings.MaxAnisotropy,
                ShadowQuality   = GlobalStats.GetShadowQuality(settings.ShadowDetail),
                ShadowDetail    = (DetailPreference) settings.ShadowDetail,
                EffectDetail    = (DetailPreference) settings.EffectDetail,
                TextureQuality  = (DetailPreference) settings.TextureQuality,
                TextureSampling = (SamplingPreference) settings.TextureSampling,
                PostProcessingDetail = DetailPreference.High,
            };

            if (Preferences != null && Preferences.Equals(p))
                return; // nothing changed.

            if (StarDriveGame.Instance != null)
            {
                Log.Write(ConsoleColor.Magenta, "Apply 3D Graphics Preferences:");
                Log.Write(ConsoleColor.Magenta, $"  Resolution:      {settings.Width}x{settings.Height} Fullscreen:{Graphics.IsFullScreen}");
                Log.Write(ConsoleColor.Magenta, $"  ShadowQuality:   {p.ShadowQuality}");
                Log.Write(ConsoleColor.Magenta, $"  ShadowDetail:    {p.ShadowDetail}");
                Log.Write(ConsoleColor.Magenta, $"  EffectDetail:    {p.EffectDetail}");
                Log.Write(ConsoleColor.Magenta, $"  TextureQuality:  {p.TextureQuality}");
                Log.Write(ConsoleColor.Magenta, $"  TextureSampling: {p.TextureSampling}");
                Log.Write(ConsoleColor.Magenta, $"  MaxAnisotropy:   {p.MaxAnisotropy}");
            }

            Preferences = p;
            ScreenManager?.UpdatePreferences(p);
        }

        bool ApplySettings(ref GraphicsSettings settings)
        {
            GraphicsDevice before = Graphics.GraphicsDevice;
            Graphics.ApplyChanges();
            bool deviceChanged = before != Graphics.GraphicsDevice;

            PresentationParameters p = GraphicsDevice.PresentationParameters;
            ScreenWidth  = p.BackBufferWidth;
            ScreenHeight = p.BackBufferHeight;
            ScreenSize   = new Vector2(ScreenWidth, ScreenHeight);
            ScreenCenter = ScreenSize * 0.5f;
            Viewport     = GraphicsDevice.Viewport;

            UpdateRendererPreferences(ref settings);
            ScreenManager?.UpdateViewports();
            return deviceChanged;
        }

        // @return TRUE if graphics device changed
        public bool ApplyGraphics(GraphicsSettings settings)
        {
            DisplayMode currentMode = GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;

            // check if resolution from graphics settings is ok:
            if (currentMode.Width < settings.Width || currentMode.Height < settings.Height)
            {
                settings.Width  = currentMode.Width;
                settings.Height = currentMode.Height;
            }

            if (settings.Width <= 0 || settings.Height <= 0)
            {
                settings.Width  = 800;
                settings.Height = 600;
            }
            var form = (Form)Control.FromHandle(Window.Handle);
            if (Debugger.IsAttached && settings.Mode == WindowMode.Fullscreen)
                settings.Mode = WindowMode.Borderless;

            // FormBorderStyle MUST be set BEFORE PreferredBackBuffer*: changing
            // the border fires a WinForms SizeChanged event, and MonoGame
            // WindowsDX's SizeChanged handler clobbers PreferredBackBufferWidth
            // /Height with the form's current ClientSize (the Phase 2.2 trap
            // documented below). For Borderless that wasn't fatal — the
            // form.ClientSize assignment further down re-fires SizeChanged with
            // the right values. Fullscreen skips that block, so the clobber
            // stuck and ToggleFullScreen() entered hardware mode at the
            // wrong resolution.
            //
            // Fullscreen also needs Border=None: MonoGame's exclusive fullscreen
            // hides the form visually, but the underlying WinForms client-area
            // origin is still offset by the title-bar height. Mouse-coord
            // transforms clamp to client area, producing a ~25px dead zone at
            // the top — invisible on the main menu, fatal on the universe HUD
            // where interactive elements sit at Y=0. Only surfaces outside the
            // debugger because Debugger.IsAttached above silently downgrades
            // Fullscreen to Borderless for VS launches.
            switch (settings.Mode)
            {
                case WindowMode.Windowed:   form.FormBorderStyle = FormBorderStyle.Fixed3D; break;
                case WindowMode.Borderless: form.FormBorderStyle = FormBorderStyle.None;    break;
                case WindowMode.Fullscreen: form.FormBorderStyle = FormBorderStyle.None;    break;
            }

            Graphics.PreferredBackBufferWidth = settings.Width;
            Graphics.PreferredBackBufferHeight = settings.Height;
            Graphics.SynchronizeWithVerticalRetrace = settings.VSync;

            if (settings.Mode != WindowMode.Fullscreen && Graphics.IsFullScreen)
            {
                Graphics.ToggleFullScreen(); // Exiting fullscreen — always safe
            }
            else if (settings.Mode == WindowMode.Fullscreen && !Graphics.IsFullScreen)
            {
                // Entering fullscreen can fail with DXGI_ERROR_NOT_CURRENTLY_AVAILABLE
                // (0x887A0022) when another app holds exclusive fullscreen, when the
                // display is mid-transition (HDR toggle, multi-monitor reconfigure),
                // when Steam Overlay's DXGI hooks intercept at a bad moment, or when
                // the window isn't fully realized yet during Initialize(). One retry
                // with a 500ms gap covers the window-realization race (the most
                // recoverable case); the other causes are persistent and fall through
                // to the Borderless fallback. Borderless gives the same visual result
                // for almost every user and doesn't need exclusive DXGI ownership.
                if (!TryEnterFullScreen(maxAttempts: 2, retryDelayMs: 500))
                {
                    settings.Mode = WindowMode.Borderless;
                    // FormBorderStyle is already None from the Fullscreen case above,
                    // which matches what Borderless wants — no border-restyle needed.
                    // The "if (settings.Mode != WindowMode.Fullscreen)" block below
                    // will now run and size/center the form correctly.
                }
            }

            // Phase 2.2: in MonoGame WindowsDX 3.8 the WinForms platform binds the
            // backbuffer size to the form's ClientSize via a SizeChanged handler. If we
            // call ApplyChanges() BEFORE the form has been resized, the backbuffer stays
            // at MonoGame's 800x480 default (the SizeChanged event hasn't fired yet to
            // override the preferred values). Result: a tiny 800x480 backbuffer
            // presented in the corner of the oversized form. Resize the form FIRST,
            // then call ApplySettings so the backbuffer tracks the form's ClientSize.
            if (settings.Mode != WindowMode.Fullscreen)
            {
                form.WindowState = FormWindowState.Normal;
                form.ClientSize = new Size(settings.Width, settings.Height);

                // set form to the center of the primary screen
                var bounds = Screen.PrimaryScreen.Bounds;
                Size size = bounds.Size;
                var pt = new System.Drawing.Point(
                    size.Width / 2 - settings.Width / 2,
                    size.Height / 2 - settings.Height / 2);

                // but also make sure that we stay inside the screen, otherwise XNA mouse cursor
                // position reporting goes crazy
                if (pt.X < bounds.Left) pt.X = bounds.Left;
                if (pt.Y < bounds.Top) pt.Y = bounds.Top;
                form.Location = pt;
            }

            bool deviceChanged = ApplySettings(ref settings);

            PresentationParameters pp = GraphicsDevice.PresentationParameters;
            Log.Write(ConsoleColor.Cyan, $"ApplyGraphics: backbuffer={pp.BackBufferWidth}x{pp.BackBufferHeight} form={form.ClientSize.Width}x{form.ClientSize.Height}");

            return deviceChanged;
        }

        // DXGI_ERROR_NOT_CURRENTLY_AVAILABLE — SetFullscreenState rejected the
        // transition. Catching by HRESULT avoids a dependency on SharpDX's
        // exception type here in GameBase; any wrapper that surfaces this code
        // gets handled the same way.
        const int DXGI_ERROR_NOT_CURRENTLY_AVAILABLE = unchecked((int)0x887A0022);

        // Try to enter exclusive fullscreen, retrying once on DXGI transient
        // failures. Returns false after exhausting attempts so the caller can
        // fall back to Borderless. After a failed ApplyChanges the
        // GraphicsDeviceManager's IsFullScreen flag may be left flipped to true
        // (the assignment happens before the underlying SetFullscreenState
        // throws), so each retry resets it explicitly rather than calling
        // ToggleFullScreen — which would invert the desired direction on the
        // second pass.
        bool TryEnterFullScreen(int maxAttempts, int retryDelayMs)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    Graphics.IsFullScreen = true;
                    Graphics.ApplyChanges();
                    return true;
                }
                catch (Exception ex) when (ex.HResult == DXGI_ERROR_NOT_CURRENTLY_AVAILABLE)
                {
                    // Reset the flag so a stale "true" doesn't leak into the
                    // Borderless fallback path or the next retry.
                    Graphics.IsFullScreen = false;
                    if (attempt < maxAttempts)
                    {
                        Log.Warning($"DXGI rejected fullscreen (attempt {attempt}/{maxAttempts}); retrying in {retryDelayMs}ms: {ex.Message}");
                        Thread.Sleep(retryDelayMs);
                    }
                    else
                    {
                        Log.Warning($"DXGI rejected fullscreen after {maxAttempts} attempts; falling back to Borderless: {ex.Message}");
                    }
                }
            }
            return false;
        }

        public void InitializeAudio()
        {
            GameAudio.Initialize(null, "Audio/AudioConfig.yaml");
        }

        protected void UpdateGame(GameTime gameTime)
        {
            if (Log.IsTerminating) // game is crashing, don't update anymore
            {
                Thread.Sleep(15);
                return;
            }

            try
            {
                ++FrameId;
                TotalElapsed = (float)gameTime.TotalGameTime.TotalSeconds;
                float deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
                Elapsed = new UpdateTimes(deltaTime, TotalElapsed);

                if (IsDeviceGood) // only Update if device is OK
                {
                    // 1. Handle Input and 2. Update for each game screen
                    ScreenManager.Update(Elapsed);
                }

                base.Update(gameTime); // MonoGame Update
            }
            catch (Exception ex)
            {
                Log.ErrorDialog(ex, "UpdateGame() failed", Program.SCREEN_UPDATE_FAILURE);
            }
        }

        protected override void Dispose(bool disposing)
        {
            GameAudio.Destroy();
            if (ScreenManager != null)
                ResourceManager.UnloadAllData(ScreenManager);
            Mem.Dispose(ref ScreenManager);

            base.Dispose(disposing); // disposes Graphics
        }
    }
}