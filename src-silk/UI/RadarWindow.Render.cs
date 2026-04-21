using eft_dma_radar.Silk.Tarkov;
using eft_dma_radar.Silk.Tarkov.Unity.IL2CPP;
using Silk.NET.Maths;
using Silk.NET.OpenGL;

namespace eft_dma_radar.Silk.UI
{
    internal static partial class RadarWindow
    {
        private static void OnRender(double delta)
        {
            if (_grContext is null || _skSurface is null)
                return;

            try
            {
                // Frame setup
                Interlocked.Increment(ref _fpsCounter);

                // Only reset GL state that ImGui touched — much cheaper than a full reset
                _grContext.ResetContext(
                    GRGlBackendState.RenderTarget |
                    GRGlBackendState.TextureBinding |
                    GRGlBackendState.View |
                    GRGlBackendState.Blend |
                    GRGlBackendState.Vertex |
                    GRGlBackendState.Program |
                    GRGlBackendState.PixelStore);

                // Periodic resource purge
                long now = Environment.TickCount64;
                if (now - _lastPurgeTick >= PurgeIntervalMs)
                {
                    _lastPurgeTick = now;
                    _grContext.PurgeUnlockedResources(false);
                }

                // Skia scene render
                var fbSize = _window.FramebufferSize;
                DrawSkiaScene(ref fbSize);

                // ImGui UI render
                DrawImGuiUI(ref fbSize, delta);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"***** CRITICAL RENDER ERROR: {ex}");
            }
        }

        private static void DrawSkiaScene(ref Vector2D<int> fbSize)
        {
            _gl.Viewport(0, 0, (uint)fbSize.X, (uint)fbSize.Y);
            _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

            _gl.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.StencilBufferBit);

            var canvas = _skSurface.Canvas;
            canvas.Save();
            try
            {
                var scale = UIScale;
                canvas.Scale(scale, scale);

                if (InRaid && LocalPlayer is Player localPlayer)
                {
                    var mapID = MapID;
                    if (!mapID.Equals(MapManager.Map?.ID, StringComparison.OrdinalIgnoreCase))
                        MapManager.LoadMap(mapID);

                    var map = MapManager.Map;
                    if (map is not null && localPlayer.HasValidPosition)
                    {
                        DrawRadar(canvas, localPlayer, map, scale);
                    }
                    else if (MapManager.IsLoading)
                    {
                        DrawStatusMessage(canvas, "Loading Map", scale, animated: true);
                    }
                    else
                    {
                        DrawStatusMessage(canvas, "Waiting for Raid Start", scale);
                    }
                }
                else if (Memory.InHideout)
                {
                    DrawStatusMessage(canvas, "In Hideout", scale);
                }
                else if (!Ready)
                {
                    DrawStatusMessage(canvas, "Starting Up", scale, animated: true);
                }
                else if (!InRaid)
                {
                    var matchingStage = MatchingProgressResolver.GetCachedStage();
                    string statusMsg;
                    if (matchingStage != EMatchingStage.None)
                    {
                        statusMsg = matchingStage.ToDisplayString();
                    }
                    else
                    {
                        statusMsg = "Waiting for Raid Start";
                    }
                    DrawStatusMessage(canvas, statusMsg, scale, animated: true);
                }
            }
            finally
            {
                canvas.Restore();
                _grContext.Flush();
            }
        }

        private static void DrawRadar(SKCanvas canvas, Player localPlayer, RadarMap map, float scale)
        {
            var localPlayerPos    = localPlayer.Position;
            var localPlayerMapPos = MapParams.ToMapPos(localPlayerPos, map.Config);

            var canvasSize = new SKSize(_window.Size.X / scale, _window.Size.Y / scale);
            MapParams mapParams;

            if (_freeMode)
            {
                if (_mapPanPosition == default)
                    _mapPanPosition = localPlayerMapPos;
                mapParams = map.GetParameters(canvasSize, _zoom, ref _mapPanPosition);
            }
            else
            {
                _mapPanPosition = default;
                mapParams = map.GetParameters(canvasSize, _zoom, ref localPlayerMapPos);
            }

            var mapCanvasBounds = new SKRect(0, 0, canvasSize.Width, canvasSize.Height);

            map.Draw(canvas, localPlayerPos.Y, mapParams.Bounds, mapCanvasBounds);

            // Viewport culling — world-space pre-cull avoids coordinate transforms for off-screen entities
            const float CullMargin = 120f;
            var worldBounds = mapParams.GetWorldBounds(CullMargin);
            var mapCfg = map.Config;

            // Snapshot players
            var allPlayersSnapshot = AllPlayers;

            List<Player>? normalPlayers = null;
            if (allPlayersSnapshot is not null)
            {
                _renderPlayers.Clear();
                foreach (var p in allPlayersSnapshot)
                {
                    if (!p.HasValidPosition)
                        continue; // Skip players that haven't had a valid position read yet

                    if (p.IsActive || !p.IsAlive) // Active players + dead players (for death markers)
                        _renderPlayers.Add(p);
                }
                _renderPlayers.Sort(static (a, b) => a.DrawPriority.CompareTo(b.DrawPriority));
                normalPlayers = _renderPlayers;
            }

            // Loot (skip in battle mode or if loot is disabled)
            if (!Config.BattleMode && Config.ShowLoot)
            {
                var loot = Memory.Loot;

                if (loot is not null)
                {
                    float playerY = localPlayerPos.Y;

                    int visibleCount = 0;
                    foreach (var item in loot)
                    {
                        int price = item.DisplayPrice;
                        var result = item.Evaluate(price);
                        if (!result.Visible)
                            continue;
                        if (!worldBounds.Contains(item.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(item.Position, mapCfg));
                        float dy = item.Position.Y - playerY;
                        bool underMap = dy < -15f;
                        item.Draw(canvas, sp, price, result, underMap, dy);
                        visibleCount++;
                    }
                    LootFilter.SetCounts(visibleCount, loot.Count);
                }
                else
                {
                    LootFilter.SetCounts(0, 0);
                }
            }
            else
            {
                LootFilter.SetCounts(0, 0);
            }

            // Corpses
            if (!Config.BattleMode && Config.ShowLoot && Config.ShowCorpses)
            {
                var corpses = Memory.Corpses;
                if (corpses is not null)
                {
                    foreach (var corpse in corpses)
                    {
                        if (!worldBounds.Contains(corpse.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(corpse.Position, mapCfg));
                        corpse.Draw(canvas, sp);
                    }
                }
            }

            // Static containers
            if (!Config.BattleMode && Config.ShowLoot && Config.ShowContainers)
            {
                var containers = Memory.Containers;
                if (containers is not null)
                {
                    float playerY = localPlayerPos.Y;
                    bool showNames = Config.ShowContainerNames;
                    bool hideSearched = Config.HideSearchedContainers;
                    var selectedIds = Config.SelectedContainers;

                    foreach (var container in containers)
                    {
                        if (hideSearched && container.Searched)
                            continue;
                        if (!selectedIds.Contains(container.Id))
                            continue;
                        if (!worldBounds.Contains(container.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(container.Position, mapCfg));
                        container.Draw(canvas, sp, showNames, false, 0f);
                    }
                }
            }

            // Exfils (drawn before players so player dots render on top)
            if (Config.ShowExfils)
            {
                var exfils = Memory.Exfils;
                if (exfils is not null)
                {
                    var lp = localPlayer as Tarkov.GameWorld.Player.LocalPlayer;
                    foreach (var exfil in exfils)
                    {
                        if (Config.HideInactiveExfils && lp is not null && !exfil.IsAvailableFor(lp))
                            continue;
                        if (!worldBounds.Contains(exfil.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(exfil.Position, mapCfg));
                        exfil.Draw(canvas, sp, localPlayer);
                    }
                }
            }

            // Transit points (drawn alongside exfils)
            if (Config.ShowTransits)
            {
                var transits = Memory.Transits;
                if (transits is not null)
                {
                    foreach (var transit in transits)
                    {
                        if (!worldBounds.Contains(transit.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(transit.Position, mapCfg));
                        transit.Draw(canvas, sp, localPlayer);
                    }
                }
            }

            // Doors (keyed doors with state)
            if (!Config.BattleMode && Config.ShowDoors)
            {
                var doors = Memory.Doors;
                if (doors is not null)
                {
                    bool filterByLoot = Config.DoorsOnlyNearLoot;

                    foreach (var door in doors)
                    {
                        if (!door.ShouldDraw())
                            continue;
                        if (filterByLoot && !door.IsNearLoot)
                            continue;
                        if (!worldBounds.Contains(door.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(door.Position, mapCfg));
                        door.Draw(canvas, sp, localPlayer);
                    }
                }
            }

            // Quest zones
            if (!Config.BattleMode && Config.ShowQuests)
            {
                var questLocations = Memory.QuestLocations;
                if (questLocations is not null)
                {
                    bool showOptional = Config.QuestShowOptional;

                    foreach (var loc in questLocations)
                    {
                        if (!showOptional && loc.Optional)
                            continue;
                        if (!worldBounds.Contains(loc.Position))
                            continue;

                        // Draw outline polygon first (behind marker)
                        loc.DrawOutlineProjected(canvas, mapParams, mapCfg);

                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(loc.Position, mapCfg));
                        loc.Draw(canvas, sp, localPlayer);
                    }
                }
            }

            // Explosives (grenades, tripwires, mortar projectiles)
            if (Config.ShowExplosives)
            {
                var explosives = Memory.Explosives;
                if (explosives is not null)
                {
                    foreach (var item in explosives)
                    {
                        if (!item.IsActive)
                            continue;
                        if (!worldBounds.Contains(item.Position))
                            continue;
                        item.Draw(canvas, mapParams, mapCfg, localPlayer);
                    }
                }
            }

            // BTR vehicle
            if (Config.ShowBTR)
            {
                var btr = Memory.Btr;
                if (btr is not null && btr.IsActive)
                {
                    if (worldBounds.Contains(btr.Position))
                        btr.Draw(canvas, mapParams, mapCfg, localPlayer);
                }
            }

            // Airdrops
            if (Config.ShowAirdrops)
            {
                var airdrops = Memory.Airdrops;
                if (airdrops is not null)
                {
                    foreach (var drop in airdrops)
                    {
                        if (!worldBounds.Contains(drop.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(drop.Position, mapCfg));
                        float dist = Vector3.Distance(localPlayer.Position, drop.Position);
                        drop.Draw(canvas, sp, dist);
                    }
                }
            }

            // Switches (static map data)
            if (Config.ShowSwitches)
            {
                var switches = Memory.Switches;
                if (switches is not null)
                {
                    foreach (var sw in switches)
                    {
                        if (!worldBounds.Contains(sw.Position))
                            continue;
                        var sp = mapParams.ToScreenPos(MapParams.ToMapPos(sw.Position, mapCfg));
                        float dist = Vector3.Distance(localPlayer.Position, sw.Position);
                        sw.Draw(canvas, sp, dist);
                    }
                }
            }

            // Group connectors
            if (Config.ConnectGroups && normalPlayers is not null)
                DrawGroupConnectors(canvas, normalPlayers, map, mapParams);

            // Draw local player + other players
            var localScreenPos = mapParams.ToScreenPos(MapParams.ToMapPos(localPlayer.Position, mapCfg));
            localPlayer.Draw(canvas, localScreenPos, localPlayer);

            if (normalPlayers is not null)
            {
                var btr = Memory.Btr;
                foreach (var player in normalPlayers)
                {
                    if (player.IsLocalPlayer)
                        continue;
                    if (!worldBounds.Contains(player.Position))
                        continue;

                    // Snap BTR passengers (turret operator / "scav on top") to the BTR's
                    // own XZ so they stop jittering relative to the moving vehicle.
                    var drawPos = player.Position;
                    btr?.TrySnapPassengerXZ(ref drawPos);

                    var sp = mapParams.ToScreenPos(MapParams.ToMapPos(drawPos, mapCfg));
                    player.Draw(canvas, sp, localPlayer);
                }
            }

            // Mouseover tooltips — drawn last so they're always on top
            DrawMouseoverTooltip(canvas, mapParams, map.Config, localPlayer);

            // Killfeed overlay — screen-space, top-right corner
            if (Config.ShowKillFeed)
                DrawKillfeed(canvas, canvasSize);
        }

        /// <summary>
        /// Draws the killfeed overlay in the top-right corner of the radar canvas.
        /// Uses a lock-free snapshot from <see cref="KillfeedManager"/>; no alloc per frame.
        /// </summary>
        private static void DrawKillfeed(SKCanvas canvas, SKSize canvasSize)
        {
            KillfeedManager.PruneExpired();
            var entries = KillfeedManager.Entries;

            const float LineHeight   = 17f;
            const float PadX         = 6f;
            const float PadY         = 4f;
            const float RightMargin  = 8f;
            const float TopMargin    = 8f;

            float ttl = Config.KillFeedTtlSeconds;

            // Placeholder when empty so users can confirm the overlay toggle is active.
            const string EmptyText = "Killfeed — waiting for kills…";

            // Measure the widest entry (or placeholder) to size the background panel
            float maxW = entries.Length == 0
                ? SKPaints.FontKillfeed.MeasureText(EmptyText)
                : 0f;
            for (int i = 0; i < entries.Length; i++)
            {
                float w = SKPaints.FontKillfeed.MeasureText(entries[i].FormatDisplay());
                if (w > maxW) maxW = w;
            }

            int lines = Math.Max(entries.Length, 1);
            float panelW = maxW + PadX * 2f;
            float panelH = lines * LineHeight + PadY * 2f;
            float panelX, panelY;
            if (Config.KillFeedPosX < 0f || Config.KillFeedPosY < 0f)
            {
                // Default anchor: top-right corner
                panelX = canvasSize.Width - panelW - RightMargin;
                panelY = TopMargin;
            }
            else
            {
                // User-placed — clamp to canvas
                panelX = Math.Clamp(Config.KillFeedPosX, 0f, Math.Max(0f, canvasSize.Width - panelW));
                panelY = Math.Clamp(Config.KillFeedPosY, 0f, Math.Max(0f, canvasSize.Height - panelH));
            }

            // Publish bounds for input hit-testing (drag handle)
            KillfeedBounds = new SKRect(panelX, panelY, panelX + panelW, panelY + panelH);

            // Background panel
            canvas.DrawRect(panelX, panelY, panelW, panelH, SKPaints.KillfeedBackground);

            if (entries.Length == 0)
            {
                float tx0 = panelX + PadX;
                float ty0 = panelY + PadY + LineHeight - 3f;
                canvas.DrawText(EmptyText, tx0 + 1, ty0 + 1, SKPaints.FontKillfeed, SKPaints.TextShadow);
                using var placeholder = SKPaints.TextScav.Clone();
                placeholder.Color = placeholder.Color.WithAlpha(180);
                canvas.DrawText(EmptyText, tx0, ty0, SKPaints.FontKillfeed, placeholder);
                return;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                float alpha = ttl > 0
                    ? Math.Clamp(1f - (float)(entry.AgeSec / ttl), 0.15f, 1f)
                    : 1f;

                // Pick colour by killer side
                SKPaint textPaint = entry.KillerSide switch
                {
                    Tarkov.GameWorld.Player.PlayerType.Teammate     => SKPaints.TextTeammate,
                    Tarkov.GameWorld.Player.PlayerType.USEC         => SKPaints.TextUSEC,
                    Tarkov.GameWorld.Player.PlayerType.BEAR         => SKPaints.TextBEAR,
                    Tarkov.GameWorld.Player.PlayerType.PScav        => SKPaints.TextPScav,
                    _                                               => SKPaints.TextScav,
                };

                float tx = panelX + PadX;
                float ty = panelY + PadY + LineHeight * i + LineHeight - 3f;

                // Modulate alpha
                byte a = (byte)(alpha * 255f);
                var col = textPaint.Color.WithAlpha(a);

                // Shadow
                canvas.DrawText(entry.FormatDisplay(), tx + 1, ty + 1,
                    SKPaints.FontKillfeed, SKPaints.TextShadow);

                // Text (reuse existing paint with alpha modulated via temporary color override)
                using var paint = textPaint.Clone();
                paint.Color = col;
                canvas.DrawText(entry.FormatDisplay(), tx, ty, SKPaints.FontKillfeed, paint);
            }
        }

        private static void DrawGroupConnectors(SKCanvas canvas, List<Player> players, RadarMap map, MapParams mapParams)
        {
            // Reset pooled collections instead of allocating new ones each frame
            _connectorGroups.Clear();
            _connectorPoolIndex = 0;

            foreach (var p in players)
            {
                if (p.IsHuman && p.IsHostile && p.SpawnGroupID != -1)
                {
                    if (!_connectorGroups.TryGetValue(p.SpawnGroupID, out var list))
                    {
                        // Reuse pooled list or create a new one
                        if (_connectorPoolIndex < _connectorPointPool.Count)
                        {
                            list = _connectorPointPool[_connectorPoolIndex];
                            list.Clear();
                        }
                        else
                        {
                            list = new List<SKPoint>(4);
                            _connectorPointPool.Add(list);
                        }
                        _connectorPoolIndex++;
                        _connectorGroups[p.SpawnGroupID] = list;
                    }
                    list.Add(mapParams.ToScreenPos(MapParams.ToMapPos(p.Position, map.Config)));
                }
            }
            if (_connectorGroups.Count == 0)
                return;
            foreach (var grp in _connectorGroups.Values)
            {
                if (grp.Count <= 1)
                    continue;
                for (int i = 0; i < grp.Count - 1; i++)
                {
                    canvas.DrawLine(
                        grp[i].X, grp[i].Y,
                        grp[i + 1].X, grp[i + 1].Y,
                        SKPaints.PaintConnectorGroup);
                }
            }
        }

        private static void DrawStatusMessage(SKCanvas canvas, string message, float scale, bool animated = false)
        {
            var bounds = new SKRect(0, 0, _window.Size.X / scale, _window.Size.Y / scale);

            string dots = "";
            if (animated)
            {
                if (_statusSw.ElapsedMilliseconds > 500)
                {
                    _statusOrder = (_statusOrder % 3) + 1;
                    _statusSw.Restart();
                }
                dots = _statusDots[_statusOrder];
            }

            if (!ReferenceEquals(message, _cachedStatusMessage) || _statusOrder != _cachedStatusOrder)
            {
                _cachedStatusMessage = message;
                _cachedStatusOrder = _statusOrder;
                _cachedStatusComposite = message + dots;
            }

            float textWidth = SKPaints.FontRegular48.MeasureText(_cachedStatusComposite);
            float x = (bounds.Width - textWidth) / 2f;
            float y = bounds.Height / 2f;

            canvas.DrawText(_cachedStatusComposite, x, y, SKPaints.FontRegular48, SKPaints.TextRadarStatus);
        }
    }
}
