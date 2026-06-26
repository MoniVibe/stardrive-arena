using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.Ships;

namespace Ship_Game;

public partial class UniverseScreen
{
    Authoritative4XLiveSession Authoritative4XLive;
    Empire Authoritative4XLocalPlayer;
    bool AuthoritativeDiplomacyPopupOpen;

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
