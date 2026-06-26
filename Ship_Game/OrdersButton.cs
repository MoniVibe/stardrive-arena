using Microsoft.Xna.Framework.Graphics;
using Color = Microsoft.Xna.Framework.Color;
using Ship_Game.Ships;
using System;
using SDGraphics;
using Ship_Game.Audio;
using Vector2 = SDGraphics.Vector2;
using Rectangle = SDGraphics.Rectangle;
using SDUtils;
using Ship_Game.Fleets;
using Ship_Game.Multiplayer.Authoritative;
using Ship_Game.Ships.AI;

namespace Ship_Game
{
    public sealed class OrdersButton // Cleaned Up by Fat Bastard - May, 22 2019
    {
        private readonly OrderType OrderType;
        private readonly Ship Ship;
        private bool Hovering;
        public Ref<bool> ValueToModify;
        public Ref<bool> RightClickValueToModify;
        public Rectangle ClickRect;
        public bool SimpleToggle;
        public LocalizedText Tooltip;
        public Array<Ship> ShipList = new Array<Ship>();
        public bool Active;
        readonly Fleet Fleet;

        public OrdersButton(Ship ship, OrderType ot, LocalizedText tooltip)
        {
            Tooltip = tooltip;
            OrderType = ot;
            Ship      = ship;
            ClickRect = new Rectangle(0, 0, 48, 48);
        }

        public OrdersButton(Array<Ship> shipList, OrderType ot, LocalizedText tooltip)
        {
            Tooltip   = tooltip;
            ShipList  = shipList;
            Fleet     = ShipList.Count > 0 ? shipList.First.Fleet : null;
            OrderType = ot;
            ClickRect = new Rectangle(0, 0, 48, 48);
        }

        public void Draw(SpriteBatch batch, Vector2 cursor, Rectangle rect)
        {
            bool hovering = rect.HitTest(cursor);
            if (SimpleToggle)
            {
                batch.Draw(!hovering
                    ? ResourceManager.Texture("SelectionBox/button_action_disabled")
                    : ResourceManager.Texture("SelectionBox/button_action_hover"), rect, Color.White);
            }
            else
            {
                if (hovering)
                    batch.Draw(ResourceManager.Texture("SelectionBox/button_action_hover"), rect, Color.White);
                else if (RightClickValueToModify != null && !RightClickValueToModify.Value)
                    batch.Draw(ResourceManager.Texture("SelectionBox/button_action_disabled"), rect, Color.LightPink);
                else if (!ValueToModify.Value)
                    batch.Draw(ResourceManager.Texture("SelectionBox/button_action_disabled"), rect, Color.White);
                else
                    batch.Draw(ResourceManager.Texture("SelectionBox/button_action"), rect, Color.White);
            }

            switch (OrderType)
            {
                case OrderType.FighterToggle:      DrawButton(batch, rect, ResourceManager.Texture("OrderButtons/UI_Fighters"));      break;
                case OrderType.FighterRecall:      DrawButton(batch, rect, ResourceManager.Texture("OrderButtons/UI_FighterRecall")); break;
                case OrderType.SendTroops:         DrawButton(batch, rect, ResourceManager.Texture("NewUI/UI_SendTroops"));           break;
                case OrderType.TradeFood:          DrawButton(batch, rect, ResourceManager.Texture("NewUI/icon_food"));               break;
                case OrderType.TradeProduction:    DrawButton(batch, rect, ResourceManager.Texture("NewUI/icon_production"));         break;
                case OrderType.TransportColonists: DrawButton(batch, rect, ResourceManager.Texture("UI/icon_passtran"));              break;
                case OrderType.TroopToggle:        DrawButton(batch, rect, ResourceManager.Texture("UI/icon_troop"));                 break;
                case OrderType.Explore:            DrawButton(batch, rect, ResourceManager.Texture("UI/icon_explore"));               break;
                case OrderType.OrderResupply:      DrawButton(batch, rect, ResourceManager.Texture("Modules/Ordnance"));              break;
                case OrderType.Scrap:              DrawButton(batch, rect, ResourceManager.Texture("UI/icon_planetslist"));           break;
                case OrderType.Refit:              DrawButton(batch, rect, ResourceManager.Texture("UI/icon_dsbw"));                  break;
                case OrderType.AllowInterTrade:    DrawButton(batch, rect, ResourceManager.Texture("NewUI/icon_intertrade"));         break;
                case OrderType.DefineTradeRoutes:  DrawTradeRoutesButton(batch, rect, Ship);                                          break;
                case OrderType.DefineAO:           DrawAOButton(batch, rect, Ship);                                                   break;
                case OrderType.Patrol:             DrawPatrolButton(batch, rect, Fleet);                                              break;
            }
        }

        private void DrawTradeRoutesButton(SpriteBatch batch, Rectangle rect, Ship ship)
        {
            if (ship == null)
                return;

            DrawDynamicButton(batch, rect, ResourceManager.Texture("NewUI/icon_routes_Active"),
                                           ResourceManager.Texture("NewUI/icon_routes"),
                                           ship.TradeRoutes.Count);
        }

        private void DrawAOButton(SpriteBatch batch, Rectangle rect, Ship ship)
        {
            if (ship == null)
                return;

            DrawDynamicButton(batch, rect, ResourceManager.Texture("NewUI/UI_AO_Active"),
                                           ResourceManager.Texture("OrderButtons/UI_AO"),
                                           ship.AreaOfOperation.Count);
        }

        void DrawPatrolButton(SpriteBatch batch, Rectangle rect, Fleet fleet)
        {
            if (fleet == null)
                return;

            DrawDynamicButton(batch, rect, ResourceManager.Texture("SelectionBox/button_action"),
                                           ResourceManager.Texture("SelectionBox/button_action_disabled"),
                                           fleet.HasPatrolPlan ? 1 : 0);

            DrawButton(batch, rect, ResourceManager.Texture("UI/icon_shield"));
        }

        private void DrawDynamicButton(SpriteBatch batch, Rectangle rect, SubTexture activated, SubTexture deactivated, int counter)
        {
            SubTexture tex = counter > 0 ? activated : deactivated;
            DrawButton(batch, rect, tex);
        }

        private void DrawButton(SpriteBatch batch, Rectangle rect, SubTexture tex)
        {
            int texWidth       = Math.Min(32, tex.Width);
            int texHeight      = Math.Min(32, tex.Height);
            Rectangle iconRect = new Rectangle(rect.X + rect.Width / 2 - texWidth / 2, 
                                               rect.Y + rect.Height / 2 - texHeight / 2,
                                               texWidth,
                                               texHeight);

            batch.Draw(tex, iconRect, Color.White);
        }

        public bool HandleInput(InputState input)
        {
            if (!ClickRect.HitTest(input.CursorPosition))
            {
                Hovering = false;
                return Hovering;
            }

            ToolTip.CreateTooltip(Tooltip);
            if (SimpleToggle && (input.InGameSelect || input.RightMouseClick))
            {
                GameAudio.AcceptClick();
                for (int i = 0; i < ShipList.Count; i++)
                {
                    Ship ship = ShipList[i];
                    switch (OrderType)
                    {
                        case OrderType.Patrol:             OnPatrolClicked(input);                                 return true;
                        case OrderType.TradeFood:
                            SetTradePolicy(ship, AuthoritativeShipTradePolicyKind.Food,
                                !input.RightMouseClick, x => ship.TransportingFood = x);
                            break;
                        case OrderType.TradeProduction:
                            SetTradePolicy(ship, AuthoritativeShipTradePolicyKind.Production,
                                !input.RightMouseClick, x => ship.TransportingProduction = x);
                            break;
                        case OrderType.TransportColonists:
                            SetTradePolicy(ship, AuthoritativeShipTradePolicyKind.Colonists,
                                !input.RightMouseClick, x => ship.TransportingColonists = x);
                            break;
                        case OrderType.AllowInterTrade:
                            SetTradePolicy(ship, AuthoritativeShipTradePolicyKind.InterEmpire,
                                !input.RightMouseClick, x => ship.AllowInterEmpireTrade = x);
                            break;
                        case OrderType.FighterToggle:
                            SetCarrierPolicy(ship, AuthoritativeShipCarrierPolicyKind.FightersOut,
                                !input.RightMouseClick, x => ship.Carrier.FightersOut = x);
                            break;
                        case OrderType.TroopToggle:
                            SetCarrierPolicy(ship, AuthoritativeShipCarrierPolicyKind.TroopsOut,
                                !input.RightMouseClick, x => ship.Carrier.TroopsOut = x);
                            break;
                        case OrderType.Explore:
                            switch (Authoritative4XClientContext.TrySubmitShipSpecialOrder(ship,
                                        AuthoritativeShipSpecialOrderType.Explore))
                            {
                                case Authoritative4XUiCommandResult.Submitted:
                                case Authoritative4XUiCommandResult.Blocked:
                                    break;
                                default:
                                    ship.AI.OrderExplore();
                                    break;
                            }
                            break;
                        case OrderType.OrderResupply:
                            switch (Authoritative4XClientContext.TrySubmitShipSpecialOrder(ship,
                                        AuthoritativeShipSpecialOrderType.Resupply))
                            {
                                case Authoritative4XUiCommandResult.Submitted:
                                case Authoritative4XUiCommandResult.Blocked:
                                    break;
                                default:
                                    ship.Supply.ResupplyFromButton();
                                    break;
                            }
                            break;
                        case OrderType.Scrap:
                            switch (Authoritative4XClientContext.TrySubmitShipLifecycleOrder(ship,
                                        AuthoritativeShipLifecycleOrderType.Scrap))
                            {
                                case Authoritative4XUiCommandResult.Submitted:
                                case Authoritative4XUiCommandResult.Blocked:
                                    break;
                                default:
                                    ship.AI.OrderScrapShip();
                                    break;
                            }
                            break;
                        case OrderType.FighterRecall:
                            SetCarrierPolicy(ship, AuthoritativeShipCarrierPolicyKind.RecallFightersBeforeFTL,
                                !input.RightMouseClick, x => ship.Carrier.SetRecallFightersBeforeFTL(x));
                            break;
                        case OrderType.SendTroops:
                            SetCarrierPolicy(ship, AuthoritativeShipCarrierPolicyKind.SendTroopsToShip,
                                !input.RightMouseClick, x => ship.Carrier.SetSendTroopsToShip(x));
                            break;
                    }
                }
                return true;
            }

            if (input.InGameSelect)
            {
                GameAudio.AcceptClick();
                ValueToModify.Value = !ValueToModify.Value;
                return true;
            }

            if (input.RightMouseClick)
            {
                GameAudio.AcceptClick();
                if (RightClickValueToModify != null)
                    RightClickValueToModify.Value = !RightClickValueToModify.Value;
                else if (ValueToModify.Value)
                    ValueToModify.Value = !ValueToModify.Value; // this button has single functionality, so right click disables it as well

                return true;
            }
            return Hovering;
        }

        static void SetTradePolicy(Ship ship, AuthoritativeShipTradePolicyKind policy, bool enabled,
            Action<bool> applyLocal)
        {
            switch (Authoritative4XClientContext.TrySubmitSetShipTradePolicy(ship, policy, enabled))
            {
                case Authoritative4XUiCommandResult.Submitted:
                case Authoritative4XUiCommandResult.Blocked:
                    break;
                default:
                    applyLocal(enabled);
                    break;
            }
        }

        static void SetCarrierPolicy(Ship ship, AuthoritativeShipCarrierPolicyKind policy, bool enabled,
            Action<bool> applyLocal)
        {
            switch (Authoritative4XClientContext.TrySubmitSetShipCarrierPolicy(ship, policy, enabled))
            {
                case Authoritative4XUiCommandResult.Submitted:
                case Authoritative4XUiCommandResult.Blocked:
                    break;
                default:
                    applyLocal(enabled);
                    break;
            }
        }

        void OnPatrolClicked(InputState input)
        {
            if (Fleet == null || Fleet.Ships.Count == 0)
            {
                GameAudio.NegativeClick();
                return;
            }

            if (input.RightMouseClick)
            {
                if (Fleet.HasPatrolPlan)
                {
                    switch (Authoritative4XClientContext.TrySubmitClearFleetPatrol(Fleet))
                    {
                        case Authoritative4XUiCommandResult.Submitted:
                            GameAudio.EchoAffirmative();
                            return;
                        case Authoritative4XUiCommandResult.Blocked:
                            GameAudio.NegativeClick();
                            return;
                    }

                    Fleet.ClearPatrol();
                }

                return;
            }

            Ship firstShip = Fleet.Ships.First;
            if (Fleet.HasPatrolPlan || !firstShip.AI.HasValidPatrolWaypoints)
            {
                Fleet.Owner.Universe.Screen.ScreenManager.AddScreen(new ChoosePatrolPlan(Fleet.Owner.Universe.Screen, Fleet));
            }
            else
            {
                WayPoints waypoints = new WayPoints();
                var copiedWaypoints = firstShip.AI.CopyWayPoints();
                switch (Authoritative4XClientContext.TrySubmitCreateFleetPatrol(Fleet, copiedWaypoints))
                {
                    case Authoritative4XUiCommandResult.Submitted:
                        GameAudio.EchoAffirmative();
                        return;
                    case Authoritative4XUiCommandResult.Blocked:
                        GameAudio.NegativeClick();
                        return;
                }

                waypoints.Set(copiedWaypoints);
                Fleet.CreatePatrol(waypoints);
            }
        }
    }
}
