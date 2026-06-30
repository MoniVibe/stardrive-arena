using System;
using System.Linq.Expressions;
using System.Text;
using Microsoft.Xna.Framework.Graphics;
using Color = Microsoft.Xna.Framework.Color;
using SDGraphics;
using Ship_Game.Audio;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.Ships;
using Vector2 = SDGraphics.Vector2;
using Rectangle = SDGraphics.Rectangle;
using Ship_Game.Universe;

namespace Ship_Game
{
    public sealed class AutomationWindow : GameScreen
    {
        public bool IsOpen { get; private set; }
        readonly UniverseScreen Screen;
        UniverseState UState => Screen.UState;
        Submenu ConstructionSubMenu;
        DropOptions<int> FreighterDropDown;
        DropOptions<int> ColonyShipDropDown;
        DropOptions<int> ScoutDropDown;
        DropOptions<int> ConstructorDropDown;
        DropOptions<int> ResearchStationDropDown;
        DropOptions<int> MiningStationDropDown;
        UIList AutomationList;
        UIList ShipPickerList;
        bool DraggingWindow;
        Vector2 DragOffset;
        bool ResearchStationsEnabled;
        bool MiningOpsEnabled;
        const int WindowWidth = 220;
        const int WindowMaxHeight = 710;
        const int WindowMinHeight = 420;
        const int ScreenMargin = 15;
        const int RightOverlayReserve = 345;
        const int HeaderDragHeight = 28;

        public AutomationWindow(UniverseScreen screen) : base(screen, toPause: null)
        {
            Screen = screen;
            Rect = DefaultWindowRect(ScreenWidth, ScreenHeight);
            CanEscapeFromScreen = false;
        }

        public static Rectangle DefaultWindowRect(int screenWidth, int screenHeight)
        {
            int maxHeight = Math.Max(260, screenHeight - ScreenMargin * 2);
            int height = Math.Min(WindowMaxHeight, Math.Max(WindowMinHeight, screenHeight - 110));
            height = Math.Min(height, maxHeight);

            int playfieldWidth = Math.Max(WindowWidth, screenWidth - RightOverlayReserve);
            int x = (playfieldWidth - WindowWidth) / 2;
            int y = Math.Max(40, (screenHeight - height) / 2);
            return ClampWindowRect(new Rectangle(x, y, WindowWidth, height), screenWidth, screenHeight);
        }

        public static Rectangle ClampWindowRect(Rectangle rect, int screenWidth, int screenHeight)
        {
            int maxX = Math.Max(ScreenMargin, screenWidth - rect.Width - ScreenMargin);
            int maxY = Math.Max(ScreenMargin, screenHeight - rect.Height - ScreenMargin);
            int x = Math.Min(maxX, Math.Max(ScreenMargin, rect.X));
            int y = Math.Min(maxY, Math.Max(ScreenMargin, rect.Y));
            return new Rectangle(x, y, rect.Width, rect.Height);
        }

        class CheckedDropdown : UIElementV2
        {
            UICheckBox Check;
            DropOptions<int> Options;
            public DropOptions<int> Create(Expression<Func<bool>> binding, LocalizedText title, LocalizedText tooltip)
            {
                Check = new UICheckBox(-200f, -200f, binding, Fonts.Arial12Bold, title, tooltip);
                Options = new DropOptions<int>(new Vector2(-200f, -200f), 190, 18);
                return Options;
            }
            public override void PerformLayout()
            {
                Check.Pos = Pos;
                Check.PerformLayout();
                Options.Pos = new Vector2(Pos.X, Pos.Y + 16f);
                Options.PerformLayout();
                Height = Options.Bottom - Pos.Y;
            }
            public override bool HandleInput(InputState input)
            {
                return Check.HandleInput(input) || Options.HandleInput(input);
            }
            public override void Draw(SpriteBatch batch, DrawTimes elapsed)
            {
                Check.Draw(batch, elapsed);
                Options.Draw(batch, elapsed);
            }
        }

        public override void LoadContent()
        {
            base.LoadContent();
            RemoveAll();

            ResearchStationsEnabled = !Screen.Player.Universe.P.DisableResearchStations;
            MiningOpsEnabled = !Screen.Player.Universe.P.DisableMiningOps;

            RectF win = new(Rect);
            ConstructionSubMenu = new(win, GameText.Automation);

            AutomationList = AddList(new(win.X + 10f, win.Y + 290));
            AutomationList.Padding = new(2f, 10f);
            AutomationList.AddCheckbox(() => Screen.Player.AutoPickConstructors,  title: GameText.AutoPickConstructorsName, tooltip: GameText.AutoPickConstructorsTip);
            AutomationList.AddCheckbox(() => Screen.Player.AutoPickBestColonizer, title: GameText.AutoPickColonyShip, tooltip: GameText.TheBestColonyShipWill);
            AutomationList.AddCheckbox(() => Screen.Player.AutoPickBestFreighter, title: GameText.AutoPickFreighter, tooltip: GameText.IfAutoTradeIsChecked);
            AutomationList.AddCheckbox(() => Screen.Player.AutoResearch,          title: GameText.AutoResearch, tooltip: GameText.YourEmpireWillAutomaticallySelect);
            AutomationList.AddCheckbox(() => Screen.Player.AutoBuildTerraformers, title: GameText.AutoBuildTerraformers, tooltip: GameText.AutoBuildTerraformersTip);
            AutomationList.AddCheckbox(() => Screen.Player.AutoTaxes,             title: GameText.AutoTaxes, tooltip: GameText.YourEmpireWillAutomaticallyManage3);
            AutomationList.AddCheckbox(() => Screen.Player.AutoMilitary,          title: "Auto Military", tooltip: "Let the AI manage military shipbuilding and fleet defense.");

            if (ResearchStationsEnabled && Screen.Player.CanBuildResearchStations)
                AutomationList.AddCheckbox(() => Screen.Player.AutoPickBestResearchStation, title: GameText.AutoPickResearchStation, tooltip: GameText.AutoPickResearchStationTip);

            if (MiningOpsEnabled && Screen.Player.CanBuildMiningStations)
                AutomationList.AddCheckbox(() => Screen.Player.AutoPickBestMiningStation, title: GameText.AutoPickMiningStation, tooltip: GameText.AutoPickMiningStationTip);

            AutomationList.AddCheckbox(() => RushConstruction,                      title: GameText.RushAllConstruction, tooltip: GameText.RushAllConstructionTip);
            AutomationList.AddCheckbox(() => UState.P.AllowPlayerInterTrade,        title: GameText.AllowPlayerInterTradeTitle, tooltip: GameText.AllowPlayerInterTradeTip);
            AutomationList.AddCheckbox(() => UState.P.SuppressOnBuildNotifications, title: GameText.DisableBuildingAlerts, tooltip: GameText.NormallyWhenYouManuallyAdd);
            AutomationList.AddCheckbox(() => UState.P.DisableInhibitionWarning,     title: GameText.DisableInhibitionAlerts, tooltip: GameText.InhibitionAlertsAreDisplayedWhen);
            AutomationList.AddCheckbox(() => UState.P.DisableVolcanoWarning,        title: GameText.DisableVolcanoAlerts, tooltip: GameText.DisableVolcanoActivationOrDeactivation);
            AutomationList.AddCheckbox(() => UState.P.DisableCrashSiteWarning,      title: GameText.DisableCrashSiteAlerts, tooltip: GameText.DisableCrashSiteAlertsTip);
            AutomationList.AddCheckbox(() => UState.P.EnableStarvationWarning,      title: GameText.EnableStarvationWarning, tooltip: GameText.EnableStarvationWarningTip);
            AutomationList.AddCheckbox(() => UState.P.PrioitizeProjectors,          title: GameText.PrioritizeProjector, tooltip: GameText.PrioritizeProjectorTip);

            ShipPickerList = AddList(new Vector2(win.X + 10f, win.Y + 26f));
            ShipPickerList.Padding = new Vector2(2f, 10f);

            ScoutDropDown = ShipPickerList.Add(new CheckedDropdown())
                .Create(() => Screen.Player.AutoExplore, title:GameText.Autoexplore, tooltip:GameText.YourEmpireWillAutomaticallyManage);

            ColonyShipDropDown = ShipPickerList.Add(new CheckedDropdown())
                .Create(() => Screen.Player.AutoColonize, title:GameText.Autocolonize, tooltip:GameText.YourEmpireWillAutomaticallyCreate);

            ConstructorDropDown = ShipPickerList.Add(new CheckedDropdown())
                .Create(() => Screen.Player.AutoBuildSpaceRoads, Localizer.Token(GameText.Autobuild) + " Projectors", GameText.YourEmpireWillAutomaticallyCreate2);

            FreighterDropDown = ShipPickerList.Add(new CheckedDropdown())
                .Create(() => Screen.Player.AutoFreighters, title: GameText.AutomaticTrade, tooltip: GameText.YourEmpireWillAutomaticallyManage2);

            if (ResearchStationsEnabled)
                ResearchStationDropDown = ShipPickerList.Add(new CheckedDropdown())
                    .Create(() => Screen.Player.AutoBuildResearchStations, title: GameText.AutoBuildResearchStation, tooltip: GameText.AutoBuildResearchStationTip);

            if (MiningOpsEnabled)
                MiningStationDropDown = ShipPickerList.Add(new CheckedDropdown())
                    .Create(() => Screen.Player.AutoBuildMiningStations, title: GameText.AutoBuildMiningStation, tooltip: GameText.AutoBuildMiningStationTip);


            // draw ordering is still imperfect, this is a hack
            ShipPickerList.ReverseZOrder();
            UpdateDropDowns();
        }

        Rectangle HeaderDragRect => new(Rect.X, Rect.Y, Rect.Width, HeaderDragHeight);

        void MoveWindowTo(Vector2 topLeft)
        {
            Rectangle next = ClampWindowRect(new Rectangle((int)topLeft.X, (int)topLeft.Y, Rect.Width, Rect.Height),
                ScreenWidth, ScreenHeight);
            if (next.X == Rect.X && next.Y == Rect.Y)
                return;

            Rect = next;
            RectF win = new(Rect);
            ConstructionSubMenu.RectF = win;
            ConstructionSubMenu.PerformLayout();
            AutomationList?.SetAbsPos(win.X + 10f, win.Y + 290f);
            AutomationList?.PerformLayout();
            ShipPickerList?.SetAbsPos(win.X + 10f, win.Y + 26f);
            ShipPickerList?.PerformLayout();
        }

        bool HandleDrag(InputState input)
        {
            if (DraggingWindow)
            {
                if (input.LeftMouseDown)
                {
                    MoveWindowTo(input.CursorPosition - DragOffset);
                    return true;
                }

                DraggingWindow = false;
                return input.LeftMouseReleased || input.LeftMouseUp;
            }

            if (input.LeftMouseClick && HeaderDragRect.HitTest(input.CursorPosition))
            {
                DraggingWindow = true;
                DragOffset = input.CursorPosition - Pos;
                return true;
            }

            return false;
        }

        public void ToggleVisibility()
        {
            GameAudio.AcceptClick();
            IsOpen = !IsOpen;
            if (IsOpen)
                LoadContent();
        }

        public override void Draw(SpriteBatch batch, DrawTimes elapsed)
        {
            if (!Visible)
                return;

            Rectangle r = ConstructionSubMenu.Rect;
            r.Y += 25;
            r.Height -= 25;
            var sel = new Selector(r, new Color(0, 0, 0, 210));
            sel.Draw(batch, elapsed);
            ConstructionSubMenu.Draw(batch, elapsed);

            ConstructorDropDown.Visible = !Screen.Player.AutoPickConstructors;
            FreighterDropDown.Visible  = !Screen.Player.AutoPickBestFreighter;
            ColonyShipDropDown.Visible = !Screen.Player.AutoPickBestColonizer;

            // Re-populate the Research/Mining dropdowns the first time CanBuild* flips to
            // true. LoadContent only ran InitDropOptions once at window-open time, so if
            // the player completes the gating tech while AutomationWindow is still open
            // the dropdown becomes Visible (below) but stays empty — clicking it then
            // crashed on Options[0] access via ActiveName. Lazy-init on demand here.
            if (ResearchStationsEnabled)
            {
                bool canBuild = Screen.Player.CanBuildResearchStations;
                if (canBuild && ResearchStationDropDown.Count == 0)
                {
                    EmpireData pd = Screen.Player.data;
                    InitDropOptions(ResearchStationDropDown, ref pd.CurrentResearchStation, pd.DefaultResearchStation,
                        ship => ship.IsShipGoodToBuild(Screen.Player) && ship.IsResearchStation);
                }
                ResearchStationDropDown.Visible = !Screen.Player.AutoPickBestResearchStation && canBuild;
            }
            if (MiningOpsEnabled)
            {
                bool canBuild = Screen.Player.CanBuildMiningStations;
                if (canBuild && MiningStationDropDown.Count == 0)
                {
                    EmpireData pd = Screen.Player.data;
                    InitDropOptions(MiningStationDropDown, ref pd.CurrentMiningStation, pd.DefaultMiningStation,
                        ship => ship.IsShipGoodToBuild(Screen.Player) && ship.IsMiningStation);
                }
                MiningStationDropDown.Visible = !Screen.Player.AutoPickBestMiningStation && canBuild;
            }
            base.Draw(batch, elapsed);
        }

        public override bool HandleInput(InputState input)
        {
            if (!IsOpen)
                return false;

            if (HandleDrag(input))
                return true;

            bool authoritative = Authoritative4XClientContext.ShouldBlockLocalMutation(Screen.Player);
            AutomationSnapshot before = authoritative ? CaptureAutomationSnapshot() : default;

            if (base.HandleInput(input))
            {
                CopyDropDownsToPlayerData();
                if (authoritative)
                {
                    AutomationSnapshot after = CaptureAutomationSnapshot();
                    RestoreAutomationSnapshot(before);
                    SetDropDownsToSnapshot(before);
                    if (after.DiffersFrom(before))
                    {
                        Authoritative4XClientContext.TrySubmitEmpireAutomation(Screen.Player, after.Flags,
                            after.Freighter, after.Colony, after.Scout, after.Constructor,
                            after.ResearchStation, after.MiningStation);
                    }
                    if (after.UniversePreferences != before.UniversePreferences)
                    {
                        Authoritative4XClientContext.TrySubmitUniversePreferences(Screen.Player,
                            after.UniversePreferences);
                    }
                }

                return true;
            }
            return false;
        }

        void CopyDropDownsToPlayerData()
        {
            EmpireData playerData = Screen.Player.data;
            playerData.CurrentAutoFreighter   = ActiveNameOrEmpty(FreighterDropDown);
            playerData.CurrentAutoColony      = ActiveNameOrEmpty(ColonyShipDropDown);
            playerData.CurrentConstructor     = ActiveNameOrEmpty(ConstructorDropDown);
            playerData.CurrentAutoScout       = ActiveNameOrEmpty(ScoutDropDown);

            if (ResearchStationsEnabled && Screen.Player.CanBuildResearchStations)
                playerData.CurrentResearchStation = ActiveNameOrEmpty(ResearchStationDropDown);
            if (MiningOpsEnabled && Screen.Player.CanBuildMiningStations)
                playerData.CurrentMiningStation = ActiveNameOrEmpty(MiningStationDropDown);
        }

        static string ActiveNameOrEmpty(DropOptions<int> options)
            => options != null && options.Count > 0 ? options.ActiveName ?? "" : "";

        AutomationSnapshot CaptureAutomationSnapshot()
        {
            Empire player = Screen.Player;
            EmpireData data = player.data;
            return new AutomationSnapshot
            {
                Flags = AutomationFlags(player),
                Freighter = data.CurrentAutoFreighter ?? "",
                Colony = data.CurrentAutoColony ?? "",
                Scout = data.CurrentAutoScout ?? "",
                Constructor = data.CurrentConstructor ?? "",
                ResearchStation = data.CurrentResearchStation ?? "",
                MiningStation = data.CurrentMiningStation ?? "",
                AllowPlayerInterTrade = UState.P.AllowPlayerInterTrade,
                PrioitizeProjectors = UState.P.PrioitizeProjectors,
            };
        }

        void RestoreAutomationSnapshot(AutomationSnapshot snapshot)
        {
            Empire player = Screen.Player;
            EmpireData data = player.data;
            ApplyAutomationFlags(player, snapshot.Flags);
            data.CurrentAutoFreighter = snapshot.Freighter;
            data.CurrentAutoColony = snapshot.Colony;
            data.CurrentAutoScout = snapshot.Scout;
            data.CurrentConstructor = snapshot.Constructor;
            data.CurrentResearchStation = snapshot.ResearchStation;
            data.CurrentMiningStation = snapshot.MiningStation;
            UState.P.AllowPlayerInterTrade = snapshot.AllowPlayerInterTrade;
            UState.P.PrioitizeProjectors = snapshot.PrioitizeProjectors;
        }

        void SetDropDownsToSnapshot(AutomationSnapshot snapshot)
        {
            SetActiveEntry(FreighterDropDown, snapshot.Freighter);
            SetActiveEntry(ColonyShipDropDown, snapshot.Colony);
            SetActiveEntry(ScoutDropDown, snapshot.Scout);
            SetActiveEntry(ConstructorDropDown, snapshot.Constructor);
            SetActiveEntry(ResearchStationDropDown, snapshot.ResearchStation);
            SetActiveEntry(MiningStationDropDown, snapshot.MiningStation);
        }

        static void SetActiveEntry(DropOptions<int> options, string name)
        {
            if (options != null && !string.IsNullOrEmpty(name))
                options.SetActiveEntry(name);
        }

        void ApplyAutomationFlags(Empire player, AuthoritativeEmpireAutomationFlags flags)
        {
            player.AutoPickConstructors = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickConstructors);
            player.AutoPickBestColonizer = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestColonizer);
            player.AutoPickBestFreighter = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestFreighter);
            player.AutoResearch = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoResearch);
            player.AutoBuildTerraformers = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildTerraformers);
            player.AutoTaxes = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoTaxes);
            player.AutoPickBestResearchStation = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestResearchStation);
            player.AutoPickBestMiningStation = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoPickBestMiningStation);
            player.AutoExplore = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoExplore);
            player.AutoColonize = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoColonize);
            player.AutoBuildSpaceRoads = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildSpaceRoads);
            player.AutoFreighters = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoFreighters);
            player.AutoBuildResearchStations = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildResearchStations);
            player.AutoBuildMiningStations = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoBuildMiningStations);
            player.AutoMilitary = flags.HasFlag(AuthoritativeEmpireAutomationFlags.AutoMilitary);
            bool rushAll = flags.HasFlag(AuthoritativeEmpireAutomationFlags.RushAllConstruction);
            player.RushAllConstruction = rushAll;
            Screen.RunOnSimThread(() => player.SwitchRushAllConstruction(rushAll));
        }

        static AuthoritativeEmpireAutomationFlags AutomationFlags(Empire player)
        {
            var flags = AuthoritativeEmpireAutomationFlags.None;
            if (player.AutoPickConstructors) flags |= AuthoritativeEmpireAutomationFlags.AutoPickConstructors;
            if (player.AutoPickBestColonizer) flags |= AuthoritativeEmpireAutomationFlags.AutoPickBestColonizer;
            if (player.AutoPickBestFreighter) flags |= AuthoritativeEmpireAutomationFlags.AutoPickBestFreighter;
            if (player.AutoResearch) flags |= AuthoritativeEmpireAutomationFlags.AutoResearch;
            if (player.AutoBuildTerraformers) flags |= AuthoritativeEmpireAutomationFlags.AutoBuildTerraformers;
            if (player.AutoTaxes) flags |= AuthoritativeEmpireAutomationFlags.AutoTaxes;
            if (player.AutoPickBestResearchStation) flags |= AuthoritativeEmpireAutomationFlags.AutoPickBestResearchStation;
            if (player.AutoPickBestMiningStation) flags |= AuthoritativeEmpireAutomationFlags.AutoPickBestMiningStation;
            if (player.AutoExplore) flags |= AuthoritativeEmpireAutomationFlags.AutoExplore;
            if (player.AutoColonize) flags |= AuthoritativeEmpireAutomationFlags.AutoColonize;
            if (player.AutoBuildSpaceRoads) flags |= AuthoritativeEmpireAutomationFlags.AutoBuildSpaceRoads;
            if (player.AutoFreighters) flags |= AuthoritativeEmpireAutomationFlags.AutoFreighters;
            if (player.AutoBuildResearchStations) flags |= AuthoritativeEmpireAutomationFlags.AutoBuildResearchStations;
            if (player.AutoBuildMiningStations) flags |= AuthoritativeEmpireAutomationFlags.AutoBuildMiningStations;
            if (player.AutoMilitary) flags |= AuthoritativeEmpireAutomationFlags.AutoMilitary;
            if (player.RushAllConstruction) flags |= AuthoritativeEmpireAutomationFlags.RushAllConstruction;
            return flags;
        }

        struct AutomationSnapshot
        {
            public AuthoritativeEmpireAutomationFlags Flags;
            public string Freighter;
            public string Colony;
            public string Scout;
            public string Constructor;
            public string ResearchStation;
            public string MiningStation;
            public bool AllowPlayerInterTrade;
            public bool PrioitizeProjectors;

            public AuthoritativeUniversePreferenceFlags UniversePreferences
            {
                get
                {
                    var flags = AuthoritativeUniversePreferenceFlags.None;
                    if (AllowPlayerInterTrade) flags |= AuthoritativeUniversePreferenceFlags.AllowPlayerInterTrade;
                    if (PrioitizeProjectors) flags |= AuthoritativeUniversePreferenceFlags.PrioritizeProjectors;
                    return flags;
                }
            }

            public bool DiffersFrom(AutomationSnapshot other)
                => Flags != other.Flags
                   || EffectiveFreighter != other.EffectiveFreighter
                   || EffectiveColony != other.EffectiveColony
                   || EffectiveScout != other.EffectiveScout
                   || EffectiveConstructor != other.EffectiveConstructor
                   || EffectiveResearchStation != other.EffectiveResearchStation
                   || EffectiveMiningStation != other.EffectiveMiningStation;

            string EffectiveFreighter => Uses(AuthoritativeEmpireAutomationFlags.AutoFreighters)
                                         && !Uses(AuthoritativeEmpireAutomationFlags.AutoPickBestFreighter)
                ? Freighter ?? "" : "";
            string EffectiveColony => Uses(AuthoritativeEmpireAutomationFlags.AutoColonize)
                                      && !Uses(AuthoritativeEmpireAutomationFlags.AutoPickBestColonizer)
                ? Colony ?? "" : "";
            string EffectiveScout => Uses(AuthoritativeEmpireAutomationFlags.AutoExplore) ? Scout ?? "" : "";
            string EffectiveConstructor => Uses(AuthoritativeEmpireAutomationFlags.AutoBuildSpaceRoads)
                                           && !Uses(AuthoritativeEmpireAutomationFlags.AutoPickConstructors)
                ? Constructor ?? "" : "";
            string EffectiveResearchStation => Uses(AuthoritativeEmpireAutomationFlags.AutoBuildResearchStations)
                                               && !Uses(AuthoritativeEmpireAutomationFlags.AutoPickBestResearchStation)
                ? ResearchStation ?? "" : "";
            string EffectiveMiningStation => Uses(AuthoritativeEmpireAutomationFlags.AutoBuildMiningStations)
                                             && !Uses(AuthoritativeEmpireAutomationFlags.AutoPickBestMiningStation)
                ? MiningStation ?? "" : "";

            bool Uses(AuthoritativeEmpireAutomationFlags flag) => Flags.HasFlag(flag);
        }

        void WarnBuildableShips()
        {
            var sb = new StringBuilder("Player.ShipsWeCanBuild = {\n");

            foreach (IShipDesign ship in Screen.Player.ShipsWeCanBuildSnapshot)
                sb.Append("  '").Append(ship.Name).Append("',\n");
            sb.Append("}");

            Log.Warning(sb.ToString());
        }

        void InitDropOptions(DropOptions<int> options, ref string automationShip, string defaultShip, Func<IShipDesign, bool> predicate)
        {
            if (options == null)
                return;
            options.Clear();


            foreach (IShipDesign ship in Screen.Player.ShipsWeCanBuildSnapshot)
            {
                if (predicate(ship))
                    options.AddOption(ship.Name, 0);
            }

            if (!options.SetActiveEntry(automationShip)) // try set the current automationShip active
            {
                if (!options.SetActiveEntry(defaultShip)) // we can't build a default ship??? wtf
                {
                    Log.Warning($"Failed to enable default automation ship '{defaultShip}' for player {Screen.Player}");
                    WarnBuildableShips();
                    options.AddOption(defaultShip, 0);
                }

                // In authoritative multiplayer, opening/repopulating the window must be
                // replica-read-only. The displayed default becomes authoritative only if
                // the player changes a checkbox/dropdown and submits the automation command.
                if (!Authoritative4XClientContext.ShouldBlockLocalMutation(Screen.Player))
                    automationShip = defaultShip;
            }
        }

        public void UpdateDropDowns()
        {
            EmpireData playerData = Screen.Player.data;
            if (MiningOpsEnabled && Screen.Player.CanBuildMiningStations)
            {
                InitDropOptions(MiningStationDropDown, ref playerData.CurrentMiningStation, playerData.DefaultMiningStation,
                    ship => ship.IsShipGoodToBuild(Screen.Player) && ship.IsMiningStation);
            }

            if (ResearchStationsEnabled && Screen.Player.CanBuildResearchStations)
            {
                InitDropOptions(ResearchStationDropDown, ref playerData.CurrentResearchStation, playerData.DefaultResearchStation,
                    ship => ship.IsShipGoodToBuild(Screen.Player) && ship.IsResearchStation);
            }

            InitDropOptions(FreighterDropDown, ref playerData.CurrentAutoFreighter, playerData.DefaultSmallTransport, 
                ship => ship.IsShipGoodToBuild(Screen.Player) && ship.IsFreighter);

            InitDropOptions(ColonyShipDropDown, ref playerData.CurrentAutoColony, playerData.DefaultColonyShip, 
                ship => ship.IsShipGoodToBuild(Screen.Player) && ship.IsColonyShip);

            InitDropOptions(ConstructorDropDown, ref playerData.CurrentConstructor, playerData.DefaultConstructor,
                ship => ship.IsShipGoodToBuild(Screen.Player) && ship.IsConstructor);

            InitDropOptions(ScoutDropDown, ref playerData.CurrentAutoScout, playerData.StartingScout, 
                ship =>
                {
                    if (GlobalStats.Defaults.ReconDropDown)
                        return ship.IsShipGoodToBuild(Screen.Player) && 
                              (ship.Role == RoleName.scout || 
                               ship.ShipCategory == ShipCategory.Recon);

                    return ship.IsShipGoodToBuild(Screen.Player) && 
                          (ship.Role == RoleName.scout ||
                           ship.Role == RoleName.fighter ||
                           ship.ShipCategory == ShipCategory.Recon);
                });
        }

        bool RushConstruction
        {
            get => Screen.Player.RushAllConstruction;
            set // used in the rush construction checkbox at start
            {
                Screen.Player.RushAllConstruction = value;
                Screen.RunOnSimThread(() => Screen.Player.SwitchRushAllConstruction(value));
            }
        }
    }
}
