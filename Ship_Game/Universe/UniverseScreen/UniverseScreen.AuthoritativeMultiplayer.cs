using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.Ships;

namespace Ship_Game;

public partial class UniverseScreen
{
    Authoritative4XLiveSession Authoritative4XLive;
    Empire Authoritative4XLocalPlayer;
    bool AuthoritativeDiplomacyPopupOpen;
    DateTime NextAuthoritative4XViewPerfUtc;

    public Authoritative4XLiveSession Authoritative4XMultiplayer => Authoritative4XLive;
    public bool IsAuthoritative4XMultiplayer => Authoritative4XLive != null;
    public Empire Authoritative4XLocalPlayerForUi => Authoritative4XLocalPlayer;

    public void AttachAuthoritative4XMultiplayer(Authoritative4XLiveSession session)
    {
        DetachAuthoritative4XMultiplayer();
        Authoritative4XLive = session;
        Authoritative4XLocalPlayer = UState.GetEmpire(session.LocalEmpireId);
        if (Authoritative4XLocalPlayer == null)
            throw new System.InvalidOperationException(
                $"Authoritative 4X local empire {session.LocalEmpireId} was not found.");
        if (EmpireUI != null)
            EmpireUI.Player = Authoritative4XLocalPlayer;
        RefreshAuthoritative4XLocalVisibility();
    }

    public bool IsLocalEmpireForUi(Empire empire)
        => empire != null && empire == Player;

    public bool IsLocalShipForUi(Ship ship)
        => ship?.Loyalty != null && IsLocalEmpireForUi(ship.Loyalty);

    public bool IsKnownToLocalPlayerForUi(Ship ship)
    {
        if (ship?.Loyalty == null)
            return false;

        Empire localPlayer = Player;
        return ship.Loyalty == localPlayer
               || ship.KnownByEmpires.KnownBy(localPlayer);
    }

    public bool IsVisibleToLocalPlayerInMapForUi(Ship ship)
        => ship?.InFrustum == true && IsKnownToLocalPlayerForUi(ship);

    public bool IsVisibleToLocalPlayerForUi(Ship ship)
        => IsVisibleToLocalPlayerInMapForUi(ship) && UState.IsSystemViewOrCloser;

    public bool TrySaveAuthoritative4XSessionToDefault(out FileInfo savedFile, out string error)
    {
        savedFile = null;
        error = "";
        Authoritative4XLiveSession live = Authoritative4XLive;
        if (live == null)
        {
            error = "No authoritative multiplayer session is active.";
            return false;
        }
        if (!live.IsHost)
        {
            error = "Only the host can save an authoritative multiplayer session.";
            return false;
        }

        try
        {
            Directory.CreateDirectory(SavedGame.DefaultSaveGameFolder);
            string stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            savedFile = new FileInfo(Path.Combine(SavedGame.DefaultSaveGameFolder,
                $"Authoritative MP {stamp}.sav"));
            return live.TrySaveSession(savedFile, out error);
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }
    }

    void RecordAuthoritative4XViewPerfIfNeeded()
    {
        if (Authoritative4XLive == null || viewState < UnivScreenState.SectorView)
            return;

        DateTime now = DateTime.UtcNow;
        if (now < NextAuthoritative4XViewPerfUtc)
            return;
        NextAuthoritative4XViewPerfUtc = now.AddSeconds(2);

        int visibleShips = UState.Objects?.VisibleShips?.Length ?? 0;
        int visibleProjectiles = UState.Objects?.VisibleProjectiles?.Length ?? 0;
        int visibleBeams = UState.Objects?.VisibleBeams?.Length ?? 0;
        int activeFleets = 0;
        foreach (Empire empire in UState.Empires)
        {
            foreach (var _ in empire.ActiveFleets)
                ++activeFleets;
        }
        int systemsInFrustum = UState.Systems.Count(s => s.InFrustum);

        string Ms(AggregatePerfTimer timer) => (timer.AvgTime * 1000f).ToString("0.###", CultureInfo.InvariantCulture);
        Authoritative4XLive.RecordViewPerf(
            $"view={viewState} camZ={CamPos.Z.ToString("0", CultureInfo.InvariantCulture)} "
            + $"drawFps={DrawGroupTotalPerf.MeasuredSamples} drawMs={Ms(DrawGroupTotalPerf)} "
            + $"renderMs={Ms(RenderGroupTotalPerf)} overlaysMs={Ms(OverlaysGroupTotalPerf)} "
            + $"iconsMs={Ms(IconsGroupTotalPerf)} shipsMs={Ms(DrawShips)} iconMs={Ms(DrawIcons)} "
            + $"fogMs={Ms(DrawFogOfWar)} bordersMs={Ms(DrawBorders)} overFogMs={Ms(DrawOverFog)} "
            + $"visibleShips={visibleShips} projectiles={visibleProjectiles} beams={visibleBeams} "
            + $"fleets={activeFleets} systemsInFrustum={systemsInFrustum}");
    }

    public bool LocalShipCanTakeFleetOrders(Ship ship, bool forAttack = false)
    {
        if (!IsLocalShipForUi(ship))
            return false;
        if (!IsAuthoritative4XMultiplayer)
            return ship.PlayerShipCanTakeFleetOrders(forAttack);
        if (!ship.Active)
            return false;
        if (!forAttack && (ship.IsPlatformOrStation || ship.IsSubspaceProjector))
            return false;
        return true;
    }

    bool TryHandleAuthoritative4XPauseInput()
    {
        if (Authoritative4XLive == null)
            return false;
        if (Authoritative4XLive.IsHost)
            Authoritative4XLive.TryTogglePause();
        return true;
    }

    bool TryHandleAuthoritative4XGameSpeedInput(InputState input)
    {
        if (Authoritative4XLive == null)
            return false;
        if (!input.SpeedReset && !input.SpeedUp && !input.SpeedDown)
            return false;

        if (Authoritative4XLive.IsHost)
        {
            float speed = input.SpeedReset ? 1f : GetGameSpeedAdjust(input.SpeedUp);
            Authoritative4XLive.TrySetGameSpeed(speed);
        }
        return true;
    }

    void RefreshAuthoritative4XLocalVisibility()
    {
        if (Authoritative4XLocalPlayer == null)
            return;

        foreach (Ship ship in Authoritative4XLocalPlayer.OwnedShips)
            MarkKnownToAuthoritativeLocalPlayer(ship);

        foreach (Ship ship in UState.Ships)
        {
            if (ship?.Active != true || ship.Loyalty == null)
                continue;
            if (ship.Loyalty != Authoritative4XLocalPlayer
                && !ship.Loyalty.IsAlliedWith(Authoritative4XLocalPlayer))
            {
                continue;
            }

            MarkKnownToAuthoritativeLocalPlayer(ship);
        }
    }

    void MarkKnownToAuthoritativeLocalPlayer(Ship ship)
    {
        if (ship?.Active != true)
            return;

        ship.KnownByEmpires.SetSeen(Authoritative4XLocalPlayer);
        if (ship.InFrustum && UState.IsSystemViewOrCloser)
            QueueSceneObjectCreation(ship);
    }

    void UpdateAuthoritative4XMultiplayer()
    {
        Authoritative4XLive?.Poll();
        if (Authoritative4XLive != null
            && Authoritative4XLive.TryRecoverClientFromReceivedSave(out UniverseScreen recoveredUniverse,
                out string recoveryError))
        {
            ScreenManager?.GoToScreen(recoveredUniverse, clear3DObjects: true);
            return;
        }
        if (Authoritative4XLive != null
            && Authoritative4XLive.TryRecoverHostFromLastSentSave(out recoveredUniverse, out recoveryError))
        {
            ScreenManager?.GoToScreen(recoveredUniverse, clear3DObjects: true);
            return;
        }
        ShowAuthoritativeDiplomacyPopupIfNeeded();
    }

    void ShowAuthoritativeDiplomacyPopupIfNeeded()
    {
        if (Authoritative4XLive == null || AuthoritativeDiplomacyPopupOpen || ScreenManager == null)
            return;
        if (!UState.CanShowDiplomacyScreen)
            return;
        if (!Authoritative4XLive.TryDequeueDiplomacyPopup(out AuthoritativeDiplomacyPopup popup))
            return;

        AuthoritativeDiplomacyPopupOpen = true;
        var screen = new AuthoritativeDiplomacyPopupScreen(this, popup);
        screen.OnExit += () => AuthoritativeDiplomacyPopupOpen = false;
        ScreenManager.AddScreen(screen);
    }

    void DetachAuthoritative4XMultiplayer()
    {
        Authoritative4XLive?.Dispose();
        Authoritative4XLive = null;
        Authoritative4XLocalPlayer = null;
        AuthoritativeDiplomacyPopupOpen = false;
        if (EmpireUI != null)
            EmpireUI.Player = UState.Player;
    }
}
