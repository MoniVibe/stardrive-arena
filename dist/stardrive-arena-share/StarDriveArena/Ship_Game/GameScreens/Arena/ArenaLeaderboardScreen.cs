using System;
using System.Linq;
using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;
using Vector2 = SDGraphics.Vector2;
using Color = Microsoft.Xna.Framework.Color;

namespace Ship_Game.GameScreens.Arena;

/// <summary>
/// PHASE C — LADDER LEADERBOARD: shows persistent AI contenders and queues one as the next
/// arena fight. The popup is sortable by rating or class; the queued challenge is consumed by
/// ArenaFightScreen.SpawnEnemyRound on NEXT FIGHT.
/// </summary>
public sealed class ArenaLeaderboardScreen : GameScreen
{
    readonly ArenaFightScreen Arena;
    bool SortByClass;

    public ArenaLeaderboardScreen(ArenaFightScreen arena) : base(arena, toPause: arena)
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

        Vector2 c = ScreenCenter;
        var panel = new RectF(c.X - 280, c.Y - 260, 560, 520);
        Add(ArenaTheme.Panel(panel));

        Add(ArenaTheme.ScreenTitle(new Vector2(panel.X + 24, panel.Y + 18),
            "ARENA LADDER — CONTENDERS"));

        string queued = Arena.QueuedChallengeName.NotEmpty()
            ? $"Queued: {Arena.QueuedChallengeName}"
            : "No challenge queued";
        Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 50),
            $"{queued}  |  Sort: {(SortByClass ? "Class" : "Rating")}",
            ArenaTheme.BodyFont, ArenaTheme.TextSecondary));

        float footerTop = panel.Bottom - 72f;
        var listRect = new RectF(panel.X + 24, panel.Y + 86, panel.W - 48, footerTop - panel.Y - 98);
        var list = Add(new ScrollList<ArenaPopupListItem>(listRect, 34));
        list.ItemPadding = new Vector2(0f, 6f);
        list.EnableItemHighlight = true;
        list.OnClick = item => item.Activate();

        ContenderRecord[] contenders = SortedContenders();
        for (int i = 0; i < contenders.Length; ++i)
        {
            ContenderRecord contender = contenders[i];
            string label = $"{contender.Rating,4}  {contender.RoleClass,-10}  {contender.Name}  ({contender.Wins}-{contender.Losses})";
            list.AddItem(new ArenaPopupListItem(label, () => Challenge_OnClick(contender)));
        }

        UIList actions = AddList(new Vector2(c.X - 134, footerTop + 14), new Vector2(268, 40));
        actions.Padding = new Vector2(8f, 2f);
        actions.LayoutStyle = ListLayoutStyle.ResizeList;
        actions.Direction = new Vector2(1f, 0f);
        ArenaTheme.AddPillButton(actions, SortByClass ? "SORT: RATING" : "SORT: CLASS", Sort_OnClick, 150f);
        ArenaTheme.AddPillButton(actions, "BACK", Back_OnClick, 90f);
    }

    ContenderRecord[] SortedContenders()
    {
        ContenderRecord[] contenders = Arena.Contenders.Where(c => c != null).ToArray();
        return SortByClass
            ? contenders.OrderBy(c => c.RoleClass, StringComparer.Ordinal)
                .ThenByDescending(c => c.Rating)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ToArray()
            : contenders.OrderByDescending(c => c.Rating)
                .ThenBy(c => c.RoleClass, StringComparer.Ordinal)
                .ThenBy(c => c.Name, StringComparer.Ordinal)
                .ToArray();
    }

    void Challenge_OnClick(ContenderRecord contender)
    {
        if (contender != null && Arena.ChallengeContender(contender.Name))
            GameAudio.AcceptClick();
        else
            GameAudio.NegativeClick();
        LoadContent();
    }

    void Sort_OnClick(UIButton button)
    {
        GameAudio.AcceptClick();
        SortByClass = !SortByClass;
        LoadContent();
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
