using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;
using Color = Microsoft.Xna.Framework.Color;
using Rectangle = SDGraphics.Rectangle;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.Multiplayer.Authoritative;

/// <summary>
/// Minimal human-to-human proposal picker for authoritative multiplayer. It bypasses
/// the stock AI diplomacy screen and emits explicit host-validated player commands.
/// </summary>
public sealed class AuthoritativeDiplomacyProposalScreen : GameScreen
{
    public const string DeclareWarButtonName = "auth_diplomacy_propose_war";
    public const string PeaceButtonName = "auth_diplomacy_propose_peace";
    public const string AllianceButtonName = "auth_diplomacy_propose_alliance";
    public const string TradeButtonName = "auth_diplomacy_propose_trade";
    public const string NonAggressionButtonName = "auth_diplomacy_propose_nap";
    public const string BackButtonName = "auth_diplomacy_propose_back";

    readonly UniverseScreen Universe;
    readonly Empire Target;
    Rectangle PanelRect;
    UILabel Status;
    string StatusText = "";

    public AuthoritativeDiplomacyProposalScreen(GameScreen parent, UniverseScreen universe, Empire target)
        : base(parent, toPause: null)
    {
        Universe = universe ?? throw new System.ArgumentNullException(nameof(universe));
        Target = target ?? throw new System.ArgumentNullException(nameof(target));
        IsPopup = true;
        CanEscapeFromScreen = true;
        TransitionOnTime = 0.2f;
        TransitionOffTime = 0.2f;
    }

    public override void LoadContent()
    {
        RemoveAll();
        PanelRect = new Rectangle(ScreenWidth / 2 - 300, ScreenHeight / 2 - 170, 600, 340);
        Add(new UILabel(new Vector2(PanelRect.X + 24, PanelRect.Y + 22),
            "HUMAN DIPLOMACY", Fonts.Pirulen16, Color.Orange));
        Add(new UILabel(new Vector2(PanelRect.X + 24, PanelRect.Y + 58),
            $"Send a proposal to {Target.Name}.", Fonts.Arial12Bold, Color.White));

        float x = PanelRect.X + 34;
        float y = PanelRect.Y + 96;
        AddProposalButton(x, y, "NON-AGGRESSION", NonAggressionButtonName,
            AuthoritativeDiplomacyProposalType.NonAggression);
        AddProposalButton(x + 190, y, "TRADE", TradeButtonName,
            AuthoritativeDiplomacyProposalType.TradeDeal);
        AddProposalButton(x + 380, y, "ALLIANCE", AllianceButtonName,
            AuthoritativeDiplomacyProposalType.Alliance);
        AddProposalButton(x, y + 56, "PEACE", PeaceButtonName,
            AuthoritativeDiplomacyProposalType.Peace);
        AddProposalButton(x + 190, y + 56, "DECLARE WAR", DeclareWarButtonName,
            AuthoritativeDiplomacyProposalType.DeclareWar);

        Status = Add(new UILabel(new Vector2(PanelRect.X + 24, PanelRect.Bottom - 88),
            "", Fonts.Arial12Bold, Color.LightGray)
        {
            DynamicText = _ => StatusText,
        });

        UIButton back = ButtonSmall(PanelRect.Right - 88, PanelRect.Bottom - 42, "BACK", _ => ExitScreen());
        back.Name = BackButtonName;
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

    void AddProposalButton(float x, float y, string text, string name, AuthoritativeDiplomacyProposalType type)
    {
        UIButton button = ButtonMedium(x, y, text, _ => Submit(type));
        button.Name = name;
    }

    void Submit(AuthoritativeDiplomacyProposalType type)
    {
        Authoritative4XUiCommandResult result =
            Authoritative4XClientContext.TrySubmitDiplomacyProposal(Target, type, type.ToString());
        if (result == Authoritative4XUiCommandResult.Submitted)
        {
            GameAudio.AffirmativeClick();
            ExitScreen();
        }
        else
        {
            StatusText = "Proposal blocked.";
            GameAudio.NegativeClick();
        }
    }
}
