using System.Globalization;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;
using Vector2 = SDGraphics.Vector2;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaFightOptionsScreen : GameScreen
{
    readonly ArenaFightScreen Arena;
    FightOption[] Options;

    public ArenaFightOptionsScreen(ArenaFightScreen arena) : base(arena, toPause: arena)
    {
        Arena = arena;
        IsPopup = true;
        CanEscapeFromScreen = true;
        TransitionOnTime  = 0.2f;
        TransitionOffTime = 0.2f;
    }

    public override void LoadContent()
    {
        RemoveAll();
        Options = Arena?.GenerateCurrentFightOptions() ?? System.Array.Empty<FightOption>();

        Vector2 c = ScreenCenter;
        var panel = new RectF(c.X - 340, c.Y - 250, 680, 500);
        Add(ArenaTheme.Panel(panel));

        Add(ArenaTheme.ScreenTitle(new Vector2(panel.X + 24, panel.Y + 18),
            "FIGHT OPTIONS"));

        Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 50),
            "Choose the contract for the next round.", ArenaTheme.BodyFont,
            ArenaTheme.TextSecondary));

        float footerTop = panel.Bottom - 76f;
        var listRect = new RectF(panel.X + 24, panel.Y + 84, panel.W - 48, footerTop - panel.Y - 96);
        var list = Add(new ScrollList<ArenaPopupListItem>(listRect, 34));
        list.ItemPadding = new Vector2(0f, 6f);
        list.EnableItemHighlight = true;
        list.OnClick = item => item.Activate();

        if (Options.Length == 0)
        {
            list.AddItem(new ArenaPopupListItem("No contracts available.",
                textColor: ArenaTheme.TextMuted));
        }
        else
        {
            foreach (FightOption option in Options)
                AddOptionRow(list, option);
        }

        UIList actions = AddList(new Vector2(c.X - 90, footerTop + 6));
        actions.Padding = new Vector2(2f, 12f);
        actions.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(actions, "BACK", Back_OnClick);
    }

    void AddOptionRow(ScrollList<ArenaPopupListItem> list, FightOption option)
    {
        string enemy = option.RiskTier == FightRiskTier.Elite
            ? option.BossDesignName
            : option.EscortDesignName;
        string win = option.EstimatedWinRate.ToString("P0", CultureInfo.InvariantCulture);
        string contract = option.IsSpecialContract ? " CONTRACT" : "";
        string text = $"{option.DifficultyTier}{contract}  {win} win  ${option.PreviewCashTotal}  fame +{option.RewardFame}";
        string tooltip = $"{option.RiskTier} risk; {option.EnemyCount} vs {enemy}; {option.Modifier.Name}; reward value {option.ExpectedRewardValue}; id {option.OptionId}";
        list.AddItem(new ArenaPopupListItem(text, () => Pick(option), tooltip,
            textColor: ArenaTheme.RiskColor(option.RiskTier), payload: option));
    }

    void Pick(FightOption option)
    {
        if (Arena != null && Arena.SelectFightOption(option.OptionId))
        {
            GameAudio.AffirmativeClick();
            Arena.StartBout();
        }
        else
        {
            GameAudio.NegativeClick();
        }
        ExitScreen();
    }

    void Back_OnClick(UIButton button)
    {
        GameAudio.AffirmativeClick();
        ExitScreen();
    }

    public override void Draw(SpriteBatch batch, DrawTimes elapsed)
    {
        ScreenManager.FadeBackBufferToBlack(TransitionAlpha * 2 / 3);
        batch.SafeBegin();
        base.Draw(batch, elapsed);
        batch.SafeEnd();
    }
}
