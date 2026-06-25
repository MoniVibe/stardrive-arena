using Ship_Game.Multiplayer.Authoritative;

namespace Ship_Game;

public partial class UniverseScreen
{
    Authoritative4XLiveSession Authoritative4XLive;

    public Authoritative4XLiveSession Authoritative4XMultiplayer => Authoritative4XLive;
    public bool IsAuthoritative4XMultiplayer => Authoritative4XLive != null;

    public void AttachAuthoritative4XMultiplayer(Authoritative4XLiveSession session)
    {
        DetachAuthoritative4XMultiplayer();
        Authoritative4XLive = session;
    }

    void UpdateAuthoritative4XMultiplayer()
    {
        Authoritative4XLive?.Poll();
    }

    void DetachAuthoritative4XMultiplayer()
    {
        Authoritative4XLive?.Dispose();
        Authoritative4XLive = null;
    }
}
