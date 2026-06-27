using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = SDGraphics.Rectangle;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.Multiplayer.Authoritative;

/// <summary>
/// Non-pausing human diplomacy prompt for authoritative multiplayer. It deliberately
/// avoids MessageBoxScreen because pausing one peer locally would desync the session.
/// </summary>
public sealed class AuthoritativeDiplomacyPopupScreen : GameScreen
{
    public const string AcceptButtonName = "auth_diplomacy_accept";
    public const string RejectButtonName = "auth_diplomacy_reject";
    public const string OkButtonName = "auth_diplomacy_ok";

    readonly UniverseScreen Universe;
    readonly AuthoritativeDiplomacyPopup Popup;
    Rectangle PanelRect;
    string BodyText;

    public AuthoritativeDiplomacyPopupScreen(UniverseScreen universe, AuthoritativeDiplomacyPopup popup)
        : base(universe, toPause: null)
    {
        Universe = universe;
        Popup = popup ?? throw new System.ArgumentNullException(nameof(popup));
        IsPopup = true;
        CanEscapeFromScreen = true;
        TransitionOnTime = 0.2f;
        TransitionOffTime = 0.2f;
    }

    public override void LoadContent()
    {
        RemoveAll();
        PanelRect = new Rectangle(ScreenWidth / 2 - 250, ScreenHeight / 2 - 130, 500, 260);
        BodyText = Fonts.Arial12Bold.ParseText(BuildBodyText(), PanelRect.Width - 48);

        Add(new UILabel(new Vector2(PanelRect.X + 24, PanelRect.Y + 22),
            "MULTIPLAYER DIPLOMACY", Fonts.Pirulen16, Color.Orange));
        Add(new UILabel(new Vector2(PanelRect.X + 24, PanelRect.Y + 58),
            BodyText, Fonts.Arial12Bold, Color.White));

        if (Popup.RequiresResponse)
        {
            UIButton accept = ButtonSmall(PanelRect.Right - 176, PanelRect.Bottom - 42, "ACCEPT", OnAccept);
            accept.Name = AcceptButtonName;
            UIButton reject = ButtonSmall(PanelRect.Right - 88, PanelRect.Bottom - 42, "REJECT", OnReject);
            reject.Name = RejectButtonName;
        }
        else
        {
            UIButton ok = ButtonSmall(PanelRect.Right - 88, PanelRect.Bottom - 42, "OK", OnOk);
            ok.Name = OkButtonName;
        }

        base.LoadContent();
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        ScreenManager.FadeBackBufferToBlack(TransitionAlpha / 3);
        batch.SafeBegin();
        batch.FillRectangle(PanelRect, new Color(6, 10, 16, 235).Premultiplied());
        batch.DrawRectangle(PanelRect, Color.Orange);
        batch.DrawRectangle(PanelRect.Bevel(1), new Color(90, 125, 150, 180).Premultiplied());
        base.Draw(batch, elapsed);
        batch.SafeEnd();
    }

    void OnAccept(UIButton button) => Submit(AuthoritativeDiplomacyResponseKind.Accept);
    void OnReject(UIButton button) => Submit(AuthoritativeDiplomacyResponseKind.Reject);

    void OnOk(UIButton button)
    {
        GameAudio.AffirmativeClick();
        ExitScreen();
    }

    void Submit(AuthoritativeDiplomacyResponseKind response)
    {
        Authoritative4XUiCommandResult result =
            Authoritative4XClientContext.TrySubmitDiplomacyResponse(Popup.ProposalId, response);
        if (result == Authoritative4XUiCommandResult.Submitted)
        {
            GameAudio.AffirmativeClick();
            ExitScreen();
        }
        else
        {
            GameAudio.NegativeClick();
        }
    }

    string BuildBodyText()
    {
        string proposer = EmpireName(Popup.ProposerEmpireId);
        string target = EmpireName(Popup.TargetEmpireId);
        string action = Popup.ProposalType switch
        {
            AuthoritativeDiplomacyProposalType.DeclareWar => "declared war",
            AuthoritativeDiplomacyProposalType.Alliance => "proposed an alliance",
            AuthoritativeDiplomacyProposalType.Peace => "proposed peace",
            AuthoritativeDiplomacyProposalType.TradeDeal => "proposed a trade deal",
            AuthoritativeDiplomacyProposalType.NonAggression => "proposed a non-aggression pact",
            AuthoritativeDiplomacyProposalType.TechnologyTrade => "offered a technology trade",
            _ => $"sent {Popup.ProposalType}",
        };

        string text = $"{proposer} {action} with {target}.\n{Popup.Message}";
        if (Popup.Terms.NotEmpty())
            text += $"\nTerms: {Popup.Terms}";
        return text;
    }

    string EmpireName(int empireId)
    {
        Empire empire = Universe?.UState?.GetEmpireById(empireId);
        return empire?.Name ?? $"Empire {empireId}";
    }
}
