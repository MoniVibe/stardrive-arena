using System;
using Microsoft.Xna.Framework.Graphics;
using Color = Microsoft.Xna.Framework.Color;
using SDGraphics;
using SDUtils;
using Ship_Game.AI;
using Ship_Game.Audio;
using Vector2 = SDGraphics.Vector2;
using Rectangle = SDGraphics.Rectangle;

namespace Ship_Game
{
    /// <summary>
    /// In GalaxyView, draws an overlay of orbits and planets on top of selected solar system
    /// </summary>
    public sealed class SolarsystemOverlay : UIElement
    {
        public static Graphics.Font SysFont => Fonts.Arial12Bold;
        public static Graphics.Font DataFont => Fonts.Arial10;
        public float SelectionTimer;

        UniverseScreen Universe;
        SolarSystem Sys;
        float ClickTimer;
        float TimerDelay = 0.25f;
        bool Hovering;
        float HoverTimer;
        new Color tColor = Colors.Cream;

        Planet CurrentlyHoveredPlanet;

        static Texture2D BackdropTex;

        // Procedural anti-aliased filled-circle alpha texture, cached once.
        // Drawn black-tinted under the orbital diagram so background star
        // systems don't bleed through the overlay.
        static Texture2D GetBackdropTexture(GraphicsDevice device)
        {
            if (BackdropTex != null && !BackdropTex.IsDisposed)
                return BackdropTex;
            const int Size = 128;
            var pixels = new Color[Size * Size];
            float center = (Size - 1) * 0.5f;
            float radius = Size * 0.5f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    float dx = x - center, dy = y - center;
                    float d = (float)Math.Sqrt(dx * dx + dy * dy);
                    float a = Math.Clamp(radius - d, 0f, 1f); // 1px AA edge
                    byte b = (byte)(a * 255f);
                    pixels[y * Size + x] = new Color(b, b, b, b);
                }
            }
            var tex = new Texture2D(device, Size, Size);
            tex.SetData(pixels);
            BackdropTex = tex;
            return tex;
        }

        public SolarsystemOverlay(Rectangle r, ScreenManager sm, UniverseScreen universe)
        {
            Universe = universe;
            ScreenManager = sm;
            ElementRect = r;
            TransitionOnTime = TimeSpan.FromSeconds(0.25);
            TransitionOffTime = TimeSpan.FromSeconds(0.25);
        }

        public override void Draw(SpriteBatch batch, DrawTimes elapsed)
        {
            if (Sys == null)
                return;

            Empire player = Universe.Player;

            ClickTimer += elapsed.RealTime.Seconds;
            SelectionTimer += elapsed.RealTime.Seconds;

            Vector2d pPos = Universe.ProjectToScreenPosition(Sys.Position.ToVec3());
            Vector2 radialPos = new(Sys.Position.X + 4500f, Sys.Position.Y);
            Vector2d insetRadialSS = Universe.ProjectToScreenPosition(radialPos.ToVec3());
            double pRadius = insetRadialSS.Distance(pPos).LowerBound(5);

            // Backdrop: dim background star systems so they don't visually
            // compete with the orbital overlay. Sized to cover the bracket
            // (pRadius) plus the outermost orbit ring (40 + 40*i) plus a small
            // icon pad, whichever is larger. Falls back to bracket-only for
            // unexplored systems where planet count is unknown.
            int planetCount = Sys.IsExploredBy(player) ? Sys.PlanetList.Count : 0;
            float orbitRadius = 40f + 40f * planetCount + 20f;
            float backdropRadius = Math.Max(orbitRadius, (float)pRadius + 12f);
            float fadeIn = Math.Clamp(SelectionTimer / 0.4f, 0f, 1f);
            byte backdropAlpha = (byte)(200f * fadeIn);
            if (backdropAlpha > 0)
            {
                var backdrop = GetBackdropTexture(batch.GraphicsDevice);
                var backdropRect = new RectF(
                    (float)pPos.X - backdropRadius, (float)pPos.Y - backdropRadius,
                    backdropRadius * 2f, backdropRadius * 2f);
                batch.Draw(backdrop, backdropRect, new Color((byte)0, (byte)0, (byte)0, backdropAlpha));
            }

            batch.BracketRectangle(pPos, pRadius, Color.White);

            if (SelectionTimer > 0.4f)
                SelectionTimer = 0.4f;

            Hovering = false;
            CurrentlyHoveredPlanet = null;

            float transitionPos = 1f - (SelectionTimer / 0.4f);
            float transitionOffset = transitionPos*transitionPos;

            if (Sys.IsExploredBy(player))
            {
                for (int i = 0; i < Sys.PlanetList.Count; i++)
                {
                    Planet p = Sys.PlanetList[i];
                    Vector2d planetPos = pPos.PointFromAngle(p.OrbitalAngle, 40 + 40 * i);
                    planetPos -= ((planetPos - pPos).Normalized() * (40 + 40 * i) * transitionOffset);

                    Color color = p.Owner == null ? new Color(50, 50, 50, 90).Premultiplied() : new Color(p.Owner.EmpireColor, 100).Premultiplied();
                    batch.DrawCircle(pPos, pPos.Distance(planetPos), color, 2f);
                }

                for (int i = 0; i < Sys.PlanetList.Count; i++)
                {
                    Planet p = Sys.PlanetList[i];
                    Vector2d planetPos = pPos.PointFromAngle(p.OrbitalAngle, 40 + 40 * i);
                    planetPos -= ((planetPos - pPos).Normalized() * (40 + 40 * i) * transitionOffset);

                    float fIconScale = 1f + (float)Math.Log(p.Scale);
                    float iconSize = (20f * fIconScale);
                    RectF planetR = new(planetPos.X - (iconSize*0.5f), planetPos.Y - (iconSize*0.5f), iconSize, iconSize);
                    if (planetR.HitTest(Universe.Input.CursorPosition))
                    {
                        Hovering = true;
                        CurrentlyHoveredPlanet = p;

                        // animate the planet texture while player is hovering over it
                        float relHoveredTime = Math.Min(HoverTimer, 0.1f) / 0.1f;
                        float sizeModWhileHovered = 0f.LerpTo(32, relHoveredTime);
                        planetR = planetR.Bevel(sizeModWhileHovered);
                    }
                    
                    batch.Draw(p.PlanetTexture, planetR, Color.White);

                    if (Universe.SelectedPlanet == p)
                    {
                        batch.BracketRectangle(planetR, p.Owner?.EmpireColor ?? Color.Gray, 3);
                    }

                    Vector2 planetTypeCursor = new(planetR.X + planetR.W / 2 - SysFont.TextWidth(p.Name) / 2f, planetR.Y + planetR.H + 4);
                    planetTypeCursor = planetTypeCursor.Rounded();
                    int playerTroops = 0;
                    float sideSpacing = 0;

                    if (p.IsExploredBy(player))
                    {
                        bool hasAnomaly = p.HasAnomaly;
                        bool hasCommodities = p.HasCommodities;
                        bool hasTroops = false;

                        if (p.OwnerIsPlayer)
                        {
                            playerTroops += p.Troops.NumTroopsHere(p.Owner);
                            hasTroops |= playerTroops > 0;
                        }

                        if (hasAnomaly)
                        {
                            sideSpacing += 4;
                            RectF flashRect = new(planetR.X + planetR.W + sideSpacing, planetR.Y + planetR.H / 2 - 7, 14, 14);
                            batch.Draw(ResourceManager.Texture("UI/icon_anomaly_small"), flashRect, Universe.CurrentFlashColor);
                            if (flashRect.HitTest(Universe.Input.CursorPosition))
                            {
                                ToolTip.CreateTooltip(GameText.IndicatesThatAnAnomalyWas);
                            }
                            sideSpacing += flashRect.W;
                        }
                        if (hasCommodities)
                        {
                            sideSpacing += 4;
                            RectF flashRect = new(planetR.X + planetR.W + sideSpacing, planetR.Y + planetR.H / 2 - 7, 14, 14);
                            batch.Draw(ResourceManager.Texture("UI/marketIcon"), flashRect, Universe.CurrentFlashColor);
                            if (flashRect.HitTest(Universe.Input.CursorPosition))
                            {
                                ToolTip.CreateTooltip(GameText.IndicatesThatAnAnomalyWas);
                            }
                            sideSpacing += flashRect.W;
                        }
                        if (hasTroops)
                        {
                            sideSpacing += 4;
                            RectF flashRect = new(planetR.X + planetR.W + sideSpacing, planetR.Y + planetR.H / 2 - 7, 14, 14);
                            batch.Draw(ResourceManager.Texture("UI/icon_troop"), flashRect, Universe.CurrentFlashColor);
                            if (flashRect.HitTest(Universe.Input.CursorPosition))
                            {
                                ToolTip.CreateTooltip(GameText.IndicatesThatAnAnomalyWas);
                            }
                            sideSpacing += flashRect.W;
                        }
                        if (p.IsResearchable && !p.IsResearchStationDeployedBy(player))
                        {
                            sideSpacing += 4;
                            RectF flashRect = new(planetR.X + planetR.W + sideSpacing, planetR.Y + planetR.H / 2 - 7, 14, 14);
                            batch.Draw(ResourceManager.Texture("NewUI/icon_science"), flashRect, Universe.CurrentFlashColor);
                            if (flashRect.HitTest(Universe.Input.CursorPosition))
                            {
                                ToolTip.CreateTooltip(GameText.ResearchStationCanBePlaced);
                            }
                            sideSpacing += flashRect.W;
                        }

                        if (p.IsMineable)
                        {
                            sideSpacing += 4;
                            RectF flashRect = new(planetR.X + planetR.W + sideSpacing, planetR.Y + planetR.H / 2 - 7, 17, 14);
                            if (!p.Mining.AreMiningOpsPresent()) 
                            {
                                batch.Draw(Mineable.Icon, flashRect, Universe.CurrentFlashColor);
                                if (flashRect.HitTest(Universe.Input.CursorPosition))
                                    ToolTip.CreateTooltip(GameText.MiningStationsCanBePlaced);
                            }
                            else
                            {
                                var flag = p.Mining.Owner.data.Traits.FlagIndex;
                                batch.Draw(ResourceManager.Flag(flag), flashRect, p.Mining.Owner.EmpireColor);
                                if (flashRect.HitTest(Universe.Input.CursorPosition))
                                    ToolTip.CreateTooltip(GameText.MiningStationsOpsOwned);
                            }

                            sideSpacing += flashRect.W + 4;
                            RectF resourceRect = new(planetR.X + planetR.W + sideSpacing, planetR.Y + planetR.H / 2 - 7, 14, 14);
                            batch.Draw(p.Mining.ExoticResourceIcon, resourceRect, Universe.CurrentFlashColor);
                            if (resourceRect.HitTest(Universe.Input.CursorPosition))
                                ToolTip.CreateTooltip(p.Mining.ResourceDescription);
                        }

                        if (p.Owner == null)
                        {
                            batch.DrawDropShadowText1(p.Name, planetTypeCursor, SysFont, p.Habitable ? tColor : Color.LightPink);
                        }
                        else
                        {
                            batch.DrawDropShadowText1(p.Name, planetTypeCursor, SysFont, (p.Habitable ? p.Owner.EmpireColor : Color.LightPink));
                        }

                        if (p.Habitable)
                        {
                            int Spacing = DataFont.LineSpacing;
                            planetTypeCursor.Y += (Spacing + 4);
                            p.UpdateMaxPopulation();
                            string popString = p.PopulationStringForPlayer;
                            planetTypeCursor.X = planetR.X + planetR.W / 2 - DataFont.TextWidth(popString) / 2f;
                            batch.DrawString(DataFont, popString, planetTypeCursor, tColor);
                            RectF flagRect = new(planetR.X + planetR.W / 2 - 10, planetR.Y - 20, 20, 20);
                            if (p.Owner != null)
                            {
                                batch.DrawDropShadowImage(flagRect, ResourceManager.Flag(p.Owner), p.Owner.EmpireColor);
                            }
                            RectF fIcon = new(planetR.X + planetR.W / 2 - 15, (int)planetTypeCursor.Y + Spacing, 10, 10);
                            RectF pIcon = new(planetR.X + planetR.W / 2 - 15, (int)(planetTypeCursor.Y + 2 * Spacing), 10, 10);
                            RectF rIcon = new(planetR.X + planetR.W / 2 - 15, (int)(planetTypeCursor.Y + 3 * Spacing), 10, 10);
                            RectF tIcon = new(planetR.X + planetR.W / 2 - 15, (int)(planetTypeCursor.Y + 4 * Spacing), 10, 10);
                            batch.Draw(ResourceManager.Texture("NewUI/icon_food"), fIcon, Color.White);
                            batch.Draw(ResourceManager.Texture("NewUI/icon_production"), pIcon, Color.White);

                            if (p.Owner != null && p.Owner == player)
                            {
                                batch.Draw(ResourceManager.Texture("NewUI/icon_science"), rIcon, Color.White);
                                batch.Draw(ResourceManager.Texture("UI/icon_troop"), tIcon, Color.White);
                            }

                            if (p.Owner == null || p.Owner != player)
                            {
                                batch.DrawString(DataFont, p.FertilityFor(player).String(), new Vector2(fIcon.X + 12, fIcon.Y).Rounded(), tColor);
                                batch.DrawString(DataFont, p.MineralRichness.String(), new Vector2(pIcon.X + 12, pIcon.Y).Rounded(), tColor);
                            }
                            else
                            {
                                p.UpdateIncomes();
                                batch.DrawString(DataFont, p.Food.NetIncome.String(), new Vector2(fIcon.X + 12, fIcon.Y).Rounded(), tColor);
                                batch.DrawString(DataFont, p.Prod.NetIncome.String(), new Vector2(pIcon.X + 12, pIcon.Y).Rounded(), tColor);
                                batch.DrawString(DataFont, p.Res.NetIncome.String(), new Vector2(rIcon.X + 12, rIcon.Y).Rounded(), tColor);
                                batch.DrawString(DataFont, playerTroops.ToString(), new Vector2(rIcon.X + 12, tIcon.Y).Rounded(), tColor);
                            }
                        }

                        foreach (Goal g in player.AI.Goals)
                        {
                            if (g.IsColonizationGoal(p))
                            {
                                RectF flag = new(planetR.X + planetR.W / 2 - 6, planetR.Y - 17, 13, 17);
                                batch.Draw(ResourceManager.Texture("UI/flagicon"), flag, player.EmpireColor);
                                if (flag.HitTest(Universe.Input.CursorPosition))
                                    ToolTip.CreateTooltip(GameText.IndicatesThatYourEmpireHas);
                            }
                        }
                    }
                }
            }

            if (Hovering)
                HoverTimer += elapsed.RealTime.Seconds;
            else
                HoverTimer = 0f;
        }

        public override bool HandleInput(InputState input)
        {
            if (Sys == null)
                return false;

            if (input.InGameSelect && CurrentlyHoveredPlanet != null)
            {
                Planet p = CurrentlyHoveredPlanet;
                if (ClickTimer < (double)TimerDelay)
                {
                    Universe.SnapViewColony(p, p.Owner != Universe.Player);
                }
                else
                {
                    GameAudio.MouseOver();
                    Universe.SetSelectedSystem(Sys, p);
                    ClickTimer = 0.0f;
                }
                return true;
            }
            return ElementRect.HitTest(input.CursorPosition);
        }

        public void SetSystem(SolarSystem s)
        {
            if (Sys != s)
                SelectionTimer = 0f;
            Sys = s;
        }
    }
}