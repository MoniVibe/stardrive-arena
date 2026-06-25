using Microsoft.Xna.Framework.Graphics;
using SDGraphics;
using Ship_Game.Audio;
using Vector2 = SDGraphics.Vector2;

namespace Ship_Game.GameScreens.Arena;

public sealed class ArenaModuleShopItem
{
    public readonly string ModuleUid;
    public readonly string DisplayName;
    public readonly int Price;
    public readonly string ModuleType;
    public readonly int Tier;
    public readonly int RequiredFame;

    public ArenaModuleShopItem(string moduleUid, string displayName, int price, string moduleType,
        int tier = 1, int requiredFame = 0)
    {
        ModuleUid = moduleUid ?? "";
        DisplayName = displayName ?? ModuleUid;
        Price = System.Math.Max(0, price);
        ModuleType = moduleType ?? "";
        Tier = System.Math.Clamp(tier, 1, 3);
        RequiredFame = System.Math.Max(0, requiredFame);
    }
}

public enum ArenaMetaUpgradeKind
{
    FleetSlot,
    FightChoice,
    Scout,
    Research,
}

public sealed class ArenaMetaShopItem
{
    public readonly string Id;
    public readonly string Name;
    public readonly string Description;
    public readonly ArenaMetaUpgradeKind Kind;
    public readonly int Cost;
    public readonly int RequiredFame;
    public readonly int PurchasedCount;
    public readonly int MaxPurchases;
    public readonly bool IsUnlockedByFame;
    public readonly bool CanAfford;

    public bool IsSoldOut => MaxPurchases > 0 && PurchasedCount >= MaxPurchases;
    public bool CanPurchase => IsUnlockedByFame && CanAfford && !IsSoldOut;
    public string Signature
        => $"{Id}|{Kind}|{Cost}|{RequiredFame}|{PurchasedCount}|{MaxPurchases}|{IsUnlockedByFame}|{CanAfford}";

    public ArenaMetaShopItem(string id, string name, string description, ArenaMetaUpgradeKind kind,
        int cost, int requiredFame, int purchasedCount, int maxPurchases,
        bool isUnlockedByFame, bool canAfford)
    {
        Id = id ?? "";
        Name = name ?? Id;
        Description = description ?? "";
        Kind = kind;
        Cost = System.Math.Max(0, cost);
        RequiredFame = System.Math.Max(0, requiredFame);
        PurchasedCount = System.Math.Max(0, purchasedCount);
        MaxPurchases = System.Math.Max(0, maxPurchases);
        IsUnlockedByFame = isUnlockedByFame;
        CanAfford = canAfford;
    }
}

public sealed class ArenaModuleShopScreen : GameScreen
{
    readonly ArenaFightScreen Arena;

    public ArenaModuleShopScreen(ArenaFightScreen arena) : base(arena, toPause: arena)
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
        var panel = new RectF(c.X - 300, c.Y - 250, 600, 500);
        Add(ArenaTheme.Panel(panel));

        Add(ArenaTheme.ScreenTitle(new Vector2(panel.X + 24, panel.Y + 18),
            "SHOP - MODULE RESEARCH"));
        Add(new UILabel(new Vector2(panel.X + 24, panel.Y + 50),
            $"Cash: ${Arena.CurrentCash}  |  Fame: {Arena.CurrentFame}  |  Tech Tier: {Arena.CurrentModuleShopTier}",
            ArenaTheme.BodyFont, ArenaTheme.TextSecondary));

        ArenaMetaShopItem[] meta = Arena.CurrentMetaShopCatalog(includeLocked: true);
        ArenaModuleShopItem[] catalog = Arena.CurrentModuleShopCatalog(affordableOnly: true);
        float footerTop = panel.Bottom - 72f;
        var listRect = new RectF(panel.X + 24, panel.Y + 86, panel.W - 48, footerTop - panel.Y - 98);
        var list = Add(new ScrollList<ArenaPopupListItem>(listRect, 34));
        list.ItemPadding = new Vector2(0f, 6f);
        list.EnableItemHighlight = true;
        list.OnClick = item => item.Activate();

        foreach (ArenaMetaShopItem item in meta)
        {
            string state = item.IsSoldOut ? "MAX"
                : !item.IsUnlockedByFame ? $"Fame {item.RequiredFame}"
                : item.CanAfford ? $"${item.Cost}" : $"Need ${item.Cost}";
            string label = $"META: {item.Name}  ({state})";
            string tooltip = $"{item.Description}  Required Fame: {item.RequiredFame}.";
            list.AddItem(new ArenaPopupListItem(label,
                item.CanPurchase ? () => BuyMeta(item) : null,
                tooltip,
                textColor: item.CanPurchase ? ArenaTheme.TextPrimary : ArenaTheme.TextMuted));
        }

        if (meta.Length > 0)
            list.AddItem(new ArenaPopupListItem("RESEARCH", font: ArenaTheme.LabelFont,
                textColor: ArenaTheme.Amber));

        if (catalog.Length == 0)
        {
            list.AddItem(new ArenaPopupListItem("No affordable unresearched modules.",
                font: ArenaTheme.BodyFont, textColor: ArenaTheme.TextMuted));
        }
        else
        {
            for (int i = 0; i < catalog.Length; ++i)
            {
                ArenaModuleShopItem item = catalog[i];
                string label = $"T{item.Tier}: {item.DisplayName}  RESEARCH ${item.Price}  ({item.ModuleType})";
                list.AddItem(new ArenaPopupListItem(label, () => Buy(item),
                    $"Permanently unlocks {item.DisplayName} for custom designs."));
            }
        }

        UIList footer = AddList(new Vector2(c.X - 54, footerTop + 14), new Vector2(108, 40));
        footer.Padding = new Vector2(2f, 12f);
        footer.LayoutStyle = ListLayoutStyle.ResizeList;
        ArenaTheme.AddPillButton(footer, "BACK", Back_OnClick);
    }

    void Buy(ArenaModuleShopItem item)
    {
        if (Arena != null && item != null && Arena.BuyArenaModule(item.ModuleUid))
            GameAudio.AcceptClick();
        else
            GameAudio.NegativeClick();
        LoadContent();
    }

    void BuyMeta(ArenaMetaShopItem item)
    {
        if (Arena != null && item != null && Arena.BuyMetaUpgrade(item.Id))
            GameAudio.AcceptClick();
        else
            GameAudio.NegativeClick();
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
