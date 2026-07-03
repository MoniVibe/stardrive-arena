using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.Ships;
using Color = Microsoft.Xna.Framework.Color;
using Vector3d = SDGraphics.Vector3d;

namespace Ship_Game;

public partial class UniverseScreen
{
    Authoritative4XLiveSession Authoritative4XLive;
    Empire Authoritative4XLocalPlayer;
    bool AuthoritativeDiplomacyPopupOpen;
    DateTime NextAuthoritative4XViewPerfUtc;

    public Authoritative4XLiveSession Authoritative4XMultiplayer => Authoritative4XLive;
    public bool IsAuthoritative4XMultiplayer => Authoritative4XLive != null;
    public bool IsAuthoritative4XHost => Authoritative4XLive?.IsHost == true;
    public Empire Authoritative4XLocalPlayerForUi => Authoritative4XLocalPlayer;
    public override bool KeepActiveWhenGameUnfocused => IsAuthoritative4XMultiplayer;

    public void AttachAuthoritative4XMultiplayer(Authoritative4XLiveSession session)
    {
        DetachAuthoritative4XMultiplayer();
        Authoritative4XLive = session;
        Authoritative4XLive?.ActivateUiCommandContext();
        ResetAuthoritativeHostSimulationClock();
        EnsureAuthoritative4XLocalBinding(forceVisibilityRefresh: true);
        if (Authoritative4XLive != null && !Authoritative4XLive.IsHost)
            RefreshAuthoritative4XPassiveClientView();
    }

    public void RestoreAuthoritative4XClientViewFrom(UniverseScreen previous)
    {
        if (previous == null || previous == this)
            return;

        if (IsFinite(previous.CamPos))
            CamPos = previous.CamPos;
        if (IsFinite(previous.CamDestination))
            CamDestination = previous.CamDestination;
        else
            CamDestination = CamPos;

        ClearSelectedItems();
        PrevSelectedShip = null;
    }

    static bool IsFinite(Vector3d value)
        => double.IsFinite(value.X) && double.IsFinite(value.Y) && double.IsFinite(value.Z);

    void EnsureAuthoritative4XLocalBinding(bool forceVisibilityRefresh = false)
    {
        if (Authoritative4XLive == null)
            return;

        Authoritative4XLive.ActivateUiCommandContext();
        Empire local = UState.GetEmpire(Authoritative4XLive.LocalEmpireId);
        if (local == null)
            throw new System.InvalidOperationException(
                $"Authoritative 4X local empire {Authoritative4XLive.LocalEmpireId} was not found.");

        bool changed = Authoritative4XLocalPlayer != local;
        Authoritative4XLocalPlayer = local;
        if (EmpireUI != null && EmpireUI.Player != local)
            EmpireUI.Player = local;

        if (local != UState.Player && (changed || UState.FogMapBytes != null))
            ClearAuthoritative4XImportedFogMap();
        if (changed || forceVisibilityRefresh)
            RefreshAuthoritative4XLocalVisibility();
    }

    void ClearAuthoritative4XImportedFogMap()
    {
        UState.FogMapBytes = null;
        if (ScreenManager?.GraphicsDevice == null
            || FogMapTargetA == null || FogMapTargetA.IsDisposed
            || FogMapTargetB == null || FogMapTargetB.IsDisposed)
        {
            return;
        }

        var device = ScreenManager.GraphicsDevice;
        device.SetRenderTarget(FogMapTargetA);
        device.Clear(Color.Transparent);
        device.SetRenderTarget(FogMapTargetB);
        device.Clear(Color.Transparent);
        device.SetRenderTarget(null);
        FogMap = FogMapTargetA;
    }

    public bool IsLocalEmpireForUi(Empire empire)
        => empire != null && empire == Player;

    public bool IsLocalShipForUi(Ship ship)
        => ship?.Loyalty != null && IsLocalEmpireForUi(ship.Loyalty);

    public bool IsHostileShipTargetForUi(Ship ship)
    {
        Empire local = Player;
        Empire target = ship?.Loyalty;
        return local != null
               && target != null
               && target != local
               && (local.IsEmpireAttackable(target, ship)
                   || AuthoritativeHumanPlayers.IsHumanVsHuman(local, target));
    }

    public bool IsKnownToLocalPlayerForUi(Ship ship)
    {
        if (ship?.Loyalty == null)
            return false;

        Empire localPlayer = Player;
        return ship.Loyalty == localPlayer
               || ship.KnownByEmpires.KnownBy(localPlayer);
    }

    public bool IsVisibleToLocalPlayerInMapForUi(Ship ship)
        => IsShipInCurrentFrustumForUi(ship) && IsKnownToLocalPlayerForUi(ship);

    public bool IsVisibleToLocalPlayerForUi(Ship ship)
        => IsVisibleToLocalPlayerInMapForUi(ship) && UState.IsSystemViewOrCloser;

    bool IsShipInCurrentFrustumForUi(Ship ship)
    {
        if (ship?.Active != true)
            return false;
        if (ship.InFrustum)
            return true;
        if (Authoritative4XLive?.IsHost == true)
            return false;

        bool inCurrentFrustum = IsInFrustum(ship.Position, Math.Max(ship.Radius, 32f));
        if (inCurrentFrustum)
            ship.InFrustum = true;
        return inCurrentFrustum;
    }

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
        if (Authoritative4XLive == null)
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
            + $"paused={UState.Paused} speed={UState.GameSpeed.ToString("0.###", CultureInfo.InvariantCulture)} "
            + $"simTurn={SimTurnId} currentSim={CurrentSimTime.ToString("0.###", CultureInfo.InvariantCulture)} "
            + $"targetSim={TargetSimTime.ToString("0.###", CultureInfo.InvariantCulture)} "
            + $"simFps={ActualSimFPS}/{CurrentSimFPS} "
            + $"drawFps={DrawGroupTotalPerf.MeasuredSamples} drawMs={Ms(DrawGroupTotalPerf)} "
            + $"simMs={Ms(TurnTimePerf)} processMs={Ms(ProcessSimTurnsPerf)} "
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

    public void RecordAuthoritative4XUiOrderBlocked(string action, string reason, Ship ship = null,
        Ship targetShip = null, Planet planet = null, ShipGroup group = null)
    {
        Authoritative4XLiveSession live = Authoritative4XLive;
        if (live == null)
            return;

        string ShipInfo(string label, Ship s)
            => s == null ? $"{label}=none" : $"{label}={s.Id}/{s.Loyalty?.Id ?? 0}/{s.Name}";
        string groupInfo = group == null
            ? "group=none"
            : $"group={group.Owner?.Id ?? 0}/ships={group.Ships?.Count ?? 0}";
        live.RecordUiOrderBlocked(
            $"action={action ?? ""} reason='{reason ?? ""}' localPeer={live.LocalPeerId} "
            + $"localEmpire={live.LocalEmpireId} uiPlayer={Player?.Id ?? 0} "
            + $"uStatePlayer={UState.Player?.Id ?? 0} authLocal={Authoritative4XLocalPlayer?.Id ?? 0} "
            + $"{ShipInfo("ship", ship)} {ShipInfo("targetShip", targetShip)} "
            + $"planet={planet?.Id ?? 0}/{planet?.Owner?.Id ?? 0} {groupInfo} "
            + $"selectedShip={SelectedShip?.Id ?? 0}/{SelectedShip?.Loyalty?.Id ?? 0} "
            + $"selectedFleet={SelectedFleet?.Owner?.Id ?? 0}/ships={SelectedFleet?.Ships?.Count ?? 0} "
            + $"selectedList={SelectedShipList?.Count ?? 0}");
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

    internal void RefreshAuthoritative4XLocalVisibility()
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

    internal void RefreshAuthoritative4XPassiveClientView()
    {
        UState.Objects.UpdatePassiveAuthoritativeView();
        RefreshAuthoritative4XLocalVisibility();
    }

    internal int SyncAuthoritative4XPassiveShipSceneObjectsForHeadless()
        => SyncAuthoritative4XPassiveShipSceneObjects(ignoreSessionGate: true);

    int SyncAuthoritative4XPassiveShipSceneObjects(bool ignoreSessionGate = false)
    {
        if (!ignoreSessionGate && (Authoritative4XLive == null || Authoritative4XLive.IsHost))
            return 0;

        Ship[] visible = UState.Ships ?? Array.Empty<Ship>();
        int synced = 0;
        for (int i = 0; i < visible.Length; ++i)
        {
            Ship ship = visible[i];
            if (!ShouldMaintainAuthoritative4XPassiveSceneObject(ship))
                continue;
            ship.SyncSceneObjectForPassiveAuthoritativeView(forceVisible: true);
            ++synced;
        }
        return synced;
    }

    internal bool ShouldMaintainAuthoritative4XPassiveSceneObject(Ship ship)
        => ship?.Active == true
           && !ship.Dying
           && UState.IsSystemViewOrCloser
           && IsKnownToLocalPlayerForUi(ship);

    void MarkKnownToAuthoritativeLocalPlayer(Ship ship)
    {
        if (ship?.Active != true)
            return;

        ship.KnownByEmpires.SetSeen(Authoritative4XLocalPlayer);
        if (UState.IsSystemViewOrCloser && IsShipInCurrentFrustumForUi(ship))
            QueueSceneObjectCreation(ship);
    }

    void UpdateAuthoritative4XMultiplayer()
    {
        EnsureAuthoritative4XLocalBinding();
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
        if (Authoritative4XLive != null && !Authoritative4XLive.IsHost)
        {
            RefreshAuthoritative4XPassiveClientView();
            SyncAuthoritative4XPassiveShipSceneObjects();
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

    internal void DetachAuthoritative4XMultiplayerForTest()
        => DetachAuthoritative4XMultiplayer();

    void DetachAuthoritative4XMultiplayer()
    {
        Authoritative4XLiveSession live = Authoritative4XLive;
        ClearAuthoritative4XMultiplayerSession(live);
        live?.Dispose();
    }

    internal void ClearAuthoritative4XMultiplayerSession(Authoritative4XLiveSession session)
    {
        if (session != null && !ReferenceEquals(Authoritative4XLive, session))
            return;

        Authoritative4XLive = null;
        Authoritative4XLocalPlayer = null;
        AuthoritativeDiplomacyPopupOpen = false;
        if (EmpireUI != null)
            EmpireUI.Player = UState.Player;
    }
}
