#nullable enable
using eft_dma_radar.DMA.Features;
using eft_dma_radar.Misc.Data;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using eft_dma_radar.Tarkov.GameWorld;
using eft_dma_radar.Tarkov.GameWorld.Exits;
using eft_dma_radar.Tarkov.GameWorld.Explosives;
using eft_dma_radar.Tarkov.GameWorld.Interactables;
using eft_dma_radar.Tarkov.GameWorld.Loot;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.Tarkov.Unity;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using eft_dma_radar.UI.Misc;
using eft_dma_radar.UI.Pages;
using eft_dma_radar.UI.Radar.Maps;
using eft_dma_radar.UI.SKWidgetControl;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using static eft_dma_radar.Tarkov.EFTPlayer.Player;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Switch = eft_dma_radar.Tarkov.GameWorld.Interactables.Switch;

namespace eft_dma_radar
{
    public partial class MainWindow
    {
        #region Rendering
        /// <summary>
        /// GPU render event (non-RDP path).
        /// </summary>
        private void SkCanvas_PaintSurface(object? sender, SKPaintGLSurfaceEventArgs e)
        {
            DrawRadarCanvas(e.Surface.Canvas, skCanvas.CanvasSize);
        }

        /// <summary>
        /// CPU render event (RDP path).
        /// </summary>
        private void SkCanvasCpu_PaintSurface(object? sender, SKPaintSurfaceEventArgs e)
        {
            DrawRadarCanvas(e.Surface.Canvas, skCanvasCpu.CanvasSize);
        }

        /// <summary>
        /// Main Render Event.
        /// </summary>
        private void DrawRadarCanvas(SKCanvas canvas, SKSize canvasSize)
        {
            var isStarting = Starting;
            var isReady = Ready;
            var inRaid = InRaid;
            var localPlayer = LocalPlayer;

            try
            {
                SkiaResourceTracker.TrackMainWindowFrame();

                SetFPS(inRaid, canvas);

                var mapID = MapID;
                if (string.IsNullOrWhiteSpace(mapID))
                    return;

                if (!mapID.Equals(XMMapManager.Map?.ID, StringComparison.OrdinalIgnoreCase))
                {
                    XMMapManager.LoadMap(mapID);
                    UpdateSwitches();
                }

                canvas.Clear(InterfaceColorOptions.RadarBackgroundColor);

                if (inRaid && localPlayer is not null)
                {
                    var map = XMMapManager.Map;
                    ArgumentNullException.ThrowIfNull(map);

                    var closestToMouse = _mouseOverItem;

                    var localPlayerPos = localPlayer.Position;
                    var localPlayerMapPos = localPlayerPos.ToMapPos(map.Config);

                    XMMapParams mapParams;
                    if (_freeMode)
                        mapParams = map.GetParameters(canvasSize, _zoom, ref _mapPanPosition);
                    else
                        mapParams = map.GetParameters(canvasSize, _zoom, ref localPlayerMapPos);

                    if (GeneralSettingsControl.chkMapSetup.IsChecked == true)
                        MapSetupControl.UpdatePlayerPosition(localPlayer);

                    var mapCanvasBounds = new SKRect
                    {
                        Left = 0,
                        Right = canvasSize.Width,
                        Top = 0,
                        Bottom = canvasSize.Height
                    };

                    var centerX = (mapCanvasBounds.Left + mapCanvasBounds.Right) / 2;
                    var centerY = (mapCanvasBounds.Top + mapCanvasBounds.Bottom) / 2;

                    canvas.RotateDegrees(_rotationDegrees, centerX, centerY);

                    // ---- Cached map rendering ----
                    var playerHeight = localPlayer.Position.Y;
                    int cacheW = (int)mapCanvasBounds.Width;
                    int cacheH = (int)mapCanvasBounds.Height;

                    if (cacheW > 0 && cacheH > 0)
                    {
                        bool needsMapRebuild =
                            _mapCacheImage is null ||
                            _lastCachedMapID != mapID ||
                            _lastMapZoom != _zoom ||
                            _lastMapRotation != _rotationDegrees ||
                            _lastMapCacheWidth != cacheW ||
                            _lastMapCacheHeight != cacheH ||
                            Math.Abs(_lastMapPlayerHeight - playerHeight) > 0.5f ||
                            Math.Abs(_lastMapBounds.Left - mapParams.Bounds.Left) > 0.25f ||
                            Math.Abs(_lastMapBounds.Top - mapParams.Bounds.Top) > 0.25f ||
                            Math.Abs(_lastMapBounds.Width - mapParams.Bounds.Width) > 0.1f ||
                            Math.Abs(_lastMapBounds.Height - mapParams.Bounds.Height) > 0.1f;

                        long nowTick = Environment.TickCount64;

                        if (needsMapRebuild && nowTick - _lastMapRebuildTick >= MapRebuildMinIntervalMs)
                        {
                            _lastMapRebuildTick = nowTick;

                            // Reuse surface when dimensions match, only recreate on resize
                            if (_mapCacheSurface is null || _lastMapCacheWidth != cacheW || _lastMapCacheHeight != cacheH)
                            {
                                _mapCacheImage?.Dispose();
                                _mapCacheImage = null;
                                _mapCacheSurface?.Dispose();
                                var info = new SKImageInfo(cacheW, cacheH, SKColorType.Rgba8888, SKAlphaType.Premul);
                                _mapCacheSurface = SKSurface.Create(info);
                            }

                            if (_mapCacheSurface is not null)
                            {
                                var cacheCanvas = _mapCacheSurface.Canvas;
                                cacheCanvas.Clear(SKColors.Transparent);
                                map.Draw(cacheCanvas, playerHeight, mapParams.Bounds, mapCanvasBounds);

                                _mapCacheImage?.Dispose();
                                _mapCacheImage = _mapCacheSurface.Snapshot();
                            }

                            _lastCachedMapID = mapID;
                            _lastMapZoom = _zoom;
                            _lastMapRotation = _rotationDegrees;
                            _lastMapCacheWidth = cacheW;
                            _lastMapCacheHeight = cacheH;
                            _lastMapPlayerHeight = playerHeight;
                            _lastMapBounds = mapParams.Bounds;
                        }

                        if (_mapCacheImage is not null)
                            canvas.DrawImage(_mapCacheImage, mapCanvasBounds);
                        else
                            map.Draw(canvas, playerHeight, mapParams.Bounds, mapCanvasBounds);
                    }
                    else
                    {
                        map.Draw(canvas, playerHeight, mapParams.Bounds, mapCanvasBounds);
                    }

                    SKPaints.UpdatePulsingAsteriskColor();

                    localPlayer.Draw(canvas, mapParams, localPlayer);

                    // -----------------------------
                    // SNAPSHOT ALL COLLECTIONS ONCE
                    // -----------------------------
                    var allPlayersSnapshot = AllPlayers;
                    var lootSnapshot = Loot;
                    var containersSnapshot = Containers;
                    var explosivesSnapshot = Explosives;
                    var exitsSnapshot = Exits;
                    var switchesSnapshot = Switches;
                    var doorsSnapshot = Memory.Game?.Interactables._Doors?.ToList();

                    // Build filtered player lists in a single pass (avoid repeated .ToList()/.Where()/.ToList())
                    List<Player> normalPlayers = null;
                    List<BtrOperator> btrs = null;
                    if (allPlayersSnapshot is not null)
                    {
                        normalPlayers = new List<Player>(allPlayersSnapshot.Count);
                        btrs = new List<BtrOperator>(2);
                        foreach (var p in allPlayersSnapshot)
                        {
                            if (p.HasExfild)
                                continue;
                            if (p is BtrOperator btr)
                                btrs.Add(btr);
                            else
                                normalPlayers.Add(p);
                        }
                        normalPlayers.Sort(static (a, b) => DrawPriority(a.Type).CompareTo(DrawPriority(b.Type)));
                    }

                    var battleMode = Config.BattleMode;

                    // -----------------------------
                    // GROUP CONNECTORS
                    // -----------------------------
                    if (Config.ConnectGroups && normalPlayers is not null)
                    {
                        DrawGroupConnectors(canvas, normalPlayers, map, mapParams);
                    }


                    // -----------------------------
                    // PLAYERS (BOTTOM)
                    // -----------------------------
                    if (!Config.PlayersOnTop && normalPlayers is not null)
                    {
                        foreach (var player in normalPlayers)
                        {
                            if (player != localPlayer)
                                player.Draw(canvas, mapParams, localPlayer);
                        }
                    }

                    if (btrs is not null)
                    {
                        foreach (var btr in btrs)
                            btr.Draw(canvas, mapParams, localPlayer);
                    }

                    // -----------------------------
                    // CONTAINERS
                    // -----------------------------
                    if (!battleMode && Config.Containers.Show && StaticLootContainer.Settings.Enabled)
                    {
                        if (containersSnapshot is not null)
                        {
                            foreach (var container in containersSnapshot)
                            {
                                if (!LootSettingsControl.ContainerIsTracked(container.ID ?? "NULL"))
                                    continue;

                                if (Config.Containers.HideSearched && container.Searched)
                                    continue;

                                container.Draw(canvas, mapParams, localPlayer);
                            }
                        }
                    }

                    // -----------------------------
                    // LOOT
                    // -----------------------------
                    if (!battleMode && Config.ProcessLoot &&
                        (LootItem.CorpseSettings.Enabled ||
                         LootItem.LootSettings.Enabled ||
                         LootItem.ImportantLootSettings.Enabled ||
                         LootItem.QuestItemSettings.Enabled))
                    {
                        if (lootSnapshot is not null)
                        {
                            foreach (var item in lootSnapshot)
                            {
                                if (item is QuestItem)
                                    continue;

                                if (!LootItem.CorpseSettings.Enabled && item is LootCorpse)
                                    continue;

                                item.CheckNotify();
                                item.Draw(canvas, mapParams, localPlayer);
                            }
                        }
                    }

                    // -----------------------------
                    // QUEST ITEMS & LOCATIONS
                    // -----------------------------
                    if (!battleMode && (Config.QuestHelper.Enabled || LootFilterControl.ShowQuestItems) && !localPlayer.IsScav)
                    {
                        if (LootItem.QuestItemSettings.Enabled)
                        {
                            if (lootSnapshot is not null)
                            {
                                foreach (var item in lootSnapshot)
                                {
                                    if (item is QuestItem)
                                        item.Draw(canvas, mapParams, localPlayer);
                                }
                            }
                        }

                        if (QuestManager.Settings.Enabled)
                        {
                            var questLocations = Memory.QuestManager?.LocationConditions?.ToList();
                            if (questLocations is not null)
                                foreach (var loc in questLocations)
                                    loc.Draw(canvas, mapParams, localPlayer);
                        }
                    }

                    // -----------------------------
                    // EXPLOSIVES / EXITS / SWITCHES
                    // -----------------------------
                    if (explosivesSnapshot is not null)
                        foreach (var explosive in explosivesSnapshot)
                            explosive.Draw(canvas, mapParams, localPlayer);

                    if (!battleMode && exitsSnapshot is not null)
                    {
                        foreach (var exit in exitsSnapshot)
                        {
                            if (exit is Exfil ex && !localPlayer.IsPmc && ex.Status is Exfil.EStatus.Closed)
                                continue;

                            exit.Draw(canvas, mapParams, localPlayer);
                        }
                    }

                    if (!battleMode && Switch.Settings.Enabled && switchesSnapshot is not null)
                        foreach (var sw in switchesSnapshot)
                            sw.Draw(canvas, mapParams, localPlayer);

                    // -----------------------------
                    // PLAYERS ON TOP
                    // -----------------------------
                    if (Config.PlayersOnTop && normalPlayers is not null)
                    {
                        foreach (var player in normalPlayers)
                        {
                            if (player != localPlayer)
                                player.Draw(canvas, mapParams, localPlayer);
                        }
                    }

                    closestToMouse?.DrawMouseover(canvas, mapParams, localPlayer);
                    // -----------------------------
                    // DOORS
                    // -----------------------------
                    if (!battleMode && Door.Settings.Enabled && doorsSnapshot is not null)
                    {
                        foreach (var door in doorsSnapshot)
                            door.Draw(canvas, mapParams, localPlayer);
                    }
                    // -----------------------------
                    // PINGS
                    // -----------------------------
                    if (_activePings.Count > 0)
                    {
                        var now = DateTime.UtcNow;

                        foreach (var ping in _activePings.ToList())
                        {
                            var elapsed = (float)(now - ping.StartTime).TotalSeconds;
                            if (elapsed > ping.DurationSeconds)
                            {
                                _activePings.Remove(ping);
                                continue;
                            }

                            float progress = elapsed / ping.DurationSeconds;
                            float radius = 10 + 50 * progress;
                            float alpha = 1f - progress;

                            var center = ping.Position.ToMapPos(map.Config).ToZoomedPos(mapParams);

                            _pingPaint.Color = new SKColor(0, 255, 255, (byte)(alpha * 255));
                            canvas.DrawCircle(center.X, center.Y, radius, _pingPaint);
                        }
                    }

                    if (normalPlayers is not null && Config.ShowInfoTab)
                        _playerInfo?.Draw(canvas, localPlayer, normalPlayers);

                    if (Config.AimviewWidgetEnabled)
                        _aimview?.Draw(canvas);

                    if (Config.ShowDebugWidget)
                        _debugInfo?.Draw(canvas);

                    if (Config.ShowLootInfoWidget)
                        _lootInfo?.Draw(canvas, UnfilteredLoot);

                    if (Config.ShowQuestInfoWidget)
                        _questInfo?.Draw(canvas);


                }
                else
                {
                    if (!isStarting)
                        GameNotRunningStatus(canvas, canvasSize);
                    else if (isStarting && !isReady)
                        StartingUpStatus(canvas, canvasSize);
                    else if (!inRaid)
                        WaitingForRaidStatus(canvas, canvasSize);
                }

                SetStatusText(canvas);
                canvas.Flush();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"CRITICAL RENDER ERROR: {ex}");
            }
        }

        private static int DrawPriority(PlayerType t) => t switch
        {
            PlayerType.SpecialPlayer => 7,
            PlayerType.USEC or PlayerType.BEAR => 5,
            PlayerType.PScav => 4,
            PlayerType.AIBoss => 3,
            PlayerType.AIRaider => 2,
            _ => 1

        };

        /// <summary>
        /// Draws group connectors between grouped hostile players in a single pass
        /// without allocating intermediate LINQ collections.
        /// </summary>
        private static void DrawGroupConnectors(SKCanvas canvas, List<Player> players, IXMMap map, XMMapParams mapParams)
        {
            // Single-pass: bucket grouped players by SpawnGroupID
            Dictionary<int, List<SKPoint>>? groups = null;
            foreach (var p in players)
            {
                if (p.IsHumanHostileActive && p.SpawnGroupID != -1)
                {
                    groups ??= new Dictionary<int, List<SKPoint>>(8);
                    if (!groups.TryGetValue(p.SpawnGroupID, out var list))
                    {
                        list = new List<SKPoint>(4);
                        groups[p.SpawnGroupID] = list;
                    }
                    list.Add(p.Position.ToMapPos(map.Config).ToZoomedPos(mapParams));
                }
            }
            if (groups is null)
                return;
            foreach (var grp in groups.Values)
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

        public static void PingItem(string itemName)
        {
            var matchingLootItems = Loot?.Where(x => x?.Name?.IndexOf(itemName, StringComparison.OrdinalIgnoreCase) >= 0);

            if (matchingLootItems != null && matchingLootItems.Any())
            {
                foreach (var lootItem in matchingLootItems)
                {
                    _activePings.Add(new PingEffect
                    {
                        Position = lootItem.Position,
                        StartTime = DateTime.UtcNow
                    });
                    Log.WriteLine($"[Ping] Pinged item: {lootItem.Name} at {lootItem.Position}");
                }
            }
            else
            {
                Log.WriteLine($"[Ping] Item '{itemName}' not found.");
            }
        }

        private void SkCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            NotifyUIActivity();

            if (!InRaid)
                return;

            _mouseDown = true;
            _lastMousePosition = e.GetPosition(skCanvas);

            var shouldCheckMouseover = e.RightButton != MouseButtonState.Pressed;
            if (shouldCheckMouseover)
                CheckMouseoverItems(e.GetPosition(skCanvas));

            if (e.RightButton != MouseButtonState.Pressed ||
                _mouseOverItem is not Player player)
                return;

            // CTRL + Right Click → Toggle teammate (NO hostile check)
            if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                ToggleTeammateFromUI(player);
                return;
            }

            // Normal Right Click → Toggle focus (hostile only)
            if (player.IsHostileActive)
            {
                player.IsFocused = !player.IsFocused;
            }
        }

        private static void ToggleTeammateFromUI(Player player)
        {
            if (player == null || player.VoipId <= 0)
                return;

            if (TeammatesWorker.IsTeammate(player))
            {
                // Removing teammate — restore handled by worker auto-flag
                TeammatesWorker.ForceRemove(player.VoipId);
            }
            else
            {
                TeammatesWorker.ForceAdd(player);
            }
        }

        private void SkCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
                NotifyUIActivity();

            var currentPos = e.GetPosition(skCanvas);

            if (_mouseDown && _freeMode && e.LeftButton == MouseButtonState.Pressed)
            {
                var deltaX = (float)(currentPos.X - _lastMousePosition.X);
                var deltaY = (float)(currentPos.Y - _lastMousePosition.Y);

                _mapPanPosition.X -= deltaX;
                _mapPanPosition.Y -= deltaY;

                _lastMousePosition = currentPos;
                ActiveCanvas.InvalidateVisual();
                return;
            }

            if (!InRaid)
            {
                ClearRefs();
                return;
            }

            var items = MouseOverItems;
            if (items?.Any() != true)
            {
                ClearRefs();
                return;
            }

            var mouse = new Vector2((float)currentPos.X, (float)currentPos.Y);
            var closest = items.Aggregate(
                (x1, x2) => Vector2.Distance(x1.MouseoverPosition, mouse)
                            < Vector2.Distance(x2.MouseoverPosition, mouse)
                        ? x1
                        : x2); // Get object 'closest' to mouse position

            if (Vector2.Distance(closest.MouseoverPosition, mouse) >= 12)
            {
                ClearRefs();
                return;
            }

            switch (closest)
            {
                case Player player:
                    _mouseOverItem = player;
                    if (player.IsHumanHostile
                        && player.SpawnGroupID != -1)
                        MouseoverGroup = player.SpawnGroupID; // Set group ID for closest player(s)
                    else
                        MouseoverGroup = null; // Clear Group ID
                    break;
                case LootCorpse corpseObj:
                    _mouseOverItem = corpseObj;
                    var corpse = corpseObj.PlayerObject;
                    if (corpse is not null)
                    {
                        if (corpse.IsHumanHostile && corpse.SpawnGroupID != -1)
                            MouseoverGroup = corpse.SpawnGroupID; // Set group ID for closest player(s)
                    }
                    else
                    {
                        MouseoverGroup = null;
                    }
                    break;
                case LootContainer ctr:
                    _mouseOverItem = ctr;
                    break;
                case LootItem ctr:
                    _mouseOverItem = ctr;
                    break;
                case IExitPoint exit:
                    _mouseOverItem = exit;
                    MouseoverGroup = null;
                    break;
                case Switch swtch:
                    _mouseOverItem = swtch;
                    MouseoverGroup = null;
                    break;
                case QuestLocation quest:
                    _mouseOverItem = quest;
                    MouseoverGroup = null;
                    break;
                case Door door:
                    _mouseOverItem = door;
                    MouseoverGroup = null;
                    break;
                default:
                    ClearRefs();
                    break;
            }
        }

        private void SkCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _mouseDown = false;

            if (_freeMode)
                ActiveCanvas.InvalidateVisual();
        }

        private void SkCanvas_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!InRaid)
                return;

            var mousePosition = e.GetPosition(skCanvas);

            int zoomChange = e.Delta > 0 ? -ZOOM_STEP : ZOOM_STEP;
            var newZoom = Math.Max(1, Math.Min(200, _zoom + zoomChange));

            if (newZoom == _zoom)
                return;

            if (_freeMode && zoomChange < 0)
            {
                var zoomFactor = (float)newZoom / _zoom;
                var canvasCenter = new Vector2((float)ActiveCanvas.ActualWidth / 2, (float)ActiveCanvas.ActualHeight / 2);
                var mouseOffset = new Vector2((float)mousePosition.X - canvasCenter.X, (float)mousePosition.Y - canvasCenter.Y);

                var panAdjustment = mouseOffset * (1 - zoomFactor) * ZOOM_TO_MOUSE_STRENGTH;
                _mapPanPosition.X += panAdjustment.X;
                _mapPanPosition.Y += panAdjustment.Y;
            }

            _zoom = newZoom;
            ActiveCanvas.InvalidateVisual();
        }

        private void ClearRefs()
        {
            _mouseOverItem = null;
            MouseoverGroup = null;
        }

        private void CheckMouseoverItems(Point mousePosition)
        {
            var mousePos = new Vector2((float)mousePosition.X, (float)mousePosition.Y);
            IMouseoverEntity? closest = null;
            var closestDist = float.MaxValue;
            int? mouseoverGroup = null;

            var items = MouseOverItems;
            if (items != null)
            {
                foreach (var item in items)
                {
                    float dist = Vector2.Distance(mousePos, item.MouseoverPosition);
                    if (dist < closestDist && dist < 10f * UIScale)
                    {
                        closestDist = dist;
                        closest = item;

                        if (item is Player player)
                            mouseoverGroup = player.SpawnGroupID;
                    }
                }
            }

            _mouseOverItem = closest;
            MouseoverGroup = mouseoverGroup;
            ActiveCanvas.InvalidateVisual();
        }

        private void IncrementStatus()
        {
            if (_statusSw.Elapsed.TotalSeconds >= 1d)
            {
                if (_statusOrder == 3)
                    _statusOrder = 1;
                else
                    _statusOrder++;
                _statusSw.Restart();
            }
        }

        private void GameNotRunningStatus(SKCanvas canvas, SKSize canvasSize)
        {
            const string notRunning = "Game Process Not Running!";
            float textWidth = SKPaints.RadarFontRegular48.MeasureText(notRunning);
            canvas.DrawText(notRunning, (canvasSize.Width / 2) - textWidth / 2f, canvasSize.Height / 2,
                SKTextAlign.Left, SKPaints.RadarFontRegular48, SKPaints.TextRadarStatus);
            IncrementStatus();
        }

        private void StartingUpStatus(SKCanvas canvas, SKSize canvasSize)
        {
            const string startingUp1 = "Starting Up.";
            const string startingUp2 = "Starting Up..";
            const string startingUp3 = "Starting Up...";
            string status = _statusOrder == 1 ?
                startingUp1 : _statusOrder == 2 ?
                startingUp2 : startingUp3;
            float textWidth = SKPaints.RadarFontRegular48.MeasureText(startingUp1);
            canvas.DrawText(status, (canvasSize.Width / 2) - textWidth / 2f, canvasSize.Height / 2,
                SKTextAlign.Left, SKPaints.RadarFontRegular48, SKPaints.TextRadarStatus);
            IncrementStatus();
        }

        private void WaitingForRaidStatus(SKCanvas canvas, SKSize canvasSize)
        {
            string dots = _statusOrder == 1 ? "." : _statusOrder == 2 ? ".." : "...";
            string stageText = "Waiting for Raid Start";

            var stage = MatchingProgressResolver.GetCachedStage();
            if (stage != Enums.EMatchingStage.None)
                stageText = stage.ToDisplayString();

            string status = stageText + dots;
            float textWidth = SKPaints.RadarFontRegular48.MeasureText(stageText + "...");
            canvas.DrawText(status, (canvasSize.Width / 2) - textWidth / 2f, canvasSize.Height / 2,
                SKTextAlign.Left, SKPaints.RadarFontRegular48, SKPaints.TextRadarStatus);
            IncrementStatus();
        }

        private void SetFPS(bool inRaid, SKCanvas canvas)
        {
            Interlocked.Increment(ref _fpsCounter);

            if (_fpsSw.ElapsedMilliseconds >= 1000)
            {
                _lastReportedFps = Interlocked.Exchange(ref _fpsCounter, 0);
                _fpsSw.Restart();

                if (Config.ShowDebugWidget)
                    _debugInfo?.UpdateFps(_lastReportedFps);
            }
        }

        /// <summary>
        /// Set the status text in the top middle of the radar window.
        /// </summary>
        /// <param name="canvas"></param>
        private void SetStatusText(SKCanvas canvas)
        {
            try
            {
                var memWritesEnabled = MemWrites.Enabled;
                var aimEnabled = Aimbot.Config.Enabled;
                var mode = Aimbot.Config.TargetingMode;
                string? label = null;

                if (memWritesEnabled && Config.MemWrites.RageMode)
                    label = MemWriteFeature<Aimbot>.Instance.Enabled ? $"{mode.GetDescription()}: RAGE MODE" : "RAGE MODE";

                if (memWritesEnabled && aimEnabled)
                {
                    if (Aimbot.Config.RandomBone.Enabled)
                        label = $"{mode.GetDescription()}: Random Bone";
                    else if (Aimbot.Config.SilentAim.AutoBone)
                        label = $"{mode.GetDescription()}: Auto Bone";
                    else
                    {
                        var defaultBone = MemoryWritingControl.cboTargetBone.Text;
                        label = $"{mode.GetDescription()}: {defaultBone}";
                    }
                }

                if (memWritesEnabled)
                {
                    if (MemWriteFeature<WideLean>.Instance.Enabled)
                    {
                        if (label is null)
                            label = "Lean";
                        else
                            label += " (Lean)";
                    }

                }

                if (label is null)
                    return;

                var width = _useRdpMode ? (float)skCanvasCpu.CanvasSize.Width : (float)skCanvas.CanvasSize.Width;
                var height = _useRdpMode ? (float)skCanvasCpu.CanvasSize.Height : (float)skCanvas.CanvasSize.Height;
                var labelWidth = SKPaints.RadarFontMedium13.MeasureText(label);
                var spacing = 1f * UIScale;
                var top = spacing; // Start from top of the canvas
                var labelHeight = SKPaints.RadarFontMedium13.Spacing;
                var bgRect = new SKRect(
                    width / 2 - labelWidth / 2,
                    top,
                    width / 2 + labelWidth / 2,
                    top + labelHeight + spacing);
                canvas.DrawRect(bgRect, SKPaints.PaintTransparentBacker);
                var textLoc = new SKPoint(width / 2, top + labelHeight);
                canvas.DrawText(label, textLoc, SKTextAlign.Center, SKPaints.RadarFontMedium13, SKPaints.TextStatusSmall);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"ERROR Setting Aim UI Text: {ex}");
            }
        }

        public void PurgeSKResources()
        {
            if (_useRdpMode) return;
            Dispatcher.Invoke(() =>
            {
                skCanvas?.GRContext?.PurgeResources();
            });
        }

        private void RenderTimer_Elapsed(object? sender, EventArgs e)
        {
            if (Interlocked.CompareExchange(ref _isRenderingFlag, 1, 0) != 0)
                return;

            try
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        UpdateQuestPlannerRaidState();
                        ActiveCanvas.InvalidateVisual();
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _isRenderingFlag, 0);
                    }
                }), DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Render timer error: {ex.Message}");
                Interlocked.Exchange(ref _isRenderingFlag, 0);
            }
        }

        private async void InitializeCanvas()
        {
            _renderTimer.Start();
            _fpsSw.Start();

            if (_useRdpMode)
            {
                // RDP session: GL acceleration is unavailable; use CPU (SKElement) renderer.
                skCanvas.Visibility = Visibility.Collapsed;
                skCanvasCpu.Visibility = Visibility.Visible;

                SetupWidgets();

                skCanvasCpu.PaintSurface += SkCanvasCpu_PaintSurface;
                skCanvasCpu.MouseDown += SkCanvas_MouseDown;
                skCanvasCpu.MouseMove += SkCanvas_MouseMove;
                skCanvasCpu.MouseUp += SkCanvas_MouseUp;
                skCanvasCpu.MouseWheel += SkCanvas_MouseWheel;
            }
            else
            {
                // Wait for the GL context to become ready (timeout after 5 s → fall-through with no limit set).
                var sw = Stopwatch.StartNew();
                while (skCanvas.GRContext is null && sw.Elapsed.TotalSeconds < 5)
                    await Task.Delay(25);

                if (skCanvas.GRContext is not null)
                    skCanvas.GRContext.SetResourceCacheLimit(536870912); // 512 MB

                SetupWidgets();

                skCanvas.PaintSurface += SkCanvas_PaintSurface;
                skCanvas.MouseDown += SkCanvas_MouseDown;
                skCanvas.MouseMove += SkCanvas_MouseMove;
                skCanvas.MouseUp += SkCanvas_MouseUp;
                skCanvas.MouseWheel += SkCanvas_MouseWheel;
            }

            _renderTimer.Elapsed += RenderTimer_Elapsed;

            MineEntitySettings = MainWindow.Config.EntityTypeSettings.GetSettings("Mine");
        }

        /// <summary>
        /// Setup Widgets after SKElement is fully loaded and window sized properly.
        /// </summary>
        private void SetupWidgets()
        {
            var left = 2;
            var top = 0;
            var right = (float)ActiveCanvas.ActualWidth;
            var bottom = (float)ActiveCanvas.ActualHeight;

            if (Config.Widgets.AimviewLocation == default)
            {
                Config.Widgets.AimviewLocation = new SKRect(left, bottom - 200, left + 200, bottom);
            }
            if (Config.Widgets.PlayerInfoLocation == default)
            {
                Config.Widgets.PlayerInfoLocation = new SKRect(right - 1, top + 45, right, top + 1);
            }
            if (Config.Widgets.DebugInfoLocation == default)
            {
                Config.Widgets.DebugInfoLocation = new SKRect(left, top, left, top);
            }
            if (Config.Widgets.LootInfoLocation == default)
            {
                Config.Widgets.LootInfoLocation = new SKRect(left, top + 45, left, top);
            }
            if (Config.Widgets.QuestInfoLocation == default)
            {
                Config.Widgets.QuestInfoLocation = new SKRect(left, top + 50, left + 500, top);
            }

            if (!_useRdpMode)
            {
                _aimview = new AimviewWidget(skCanvas, Config.Widgets.AimviewLocation, Config.Widgets.AimviewMinimized, UIScale);
                _playerInfo = new PlayerInfoWidget(skCanvas, Config.Widgets.PlayerInfoLocation, Config.Widgets.PlayerInfoMinimized, UIScale);
                _debugInfo = new DebugInfoWidget(skCanvas, Config.Widgets.DebugInfoLocation, Config.Widgets.DebugInfoMinimized, UIScale);
                _lootInfo = new LootInfoWidget(skCanvas, Config.Widgets.LootInfoLocation, Config.Widgets.LootInfoMinimized, UIScale);
                _questInfo = new QuestInfoWidget(skCanvas, Config.Widgets.QuestInfoLocation, Config.Widgets.QuestInfoMinimized, UIScale);
            }

        }

        public void UpdateRenderTimerInterval(int targetFPS)
        {
            var interval = TimeSpan.FromMilliseconds(1000d / targetFPS);
            _renderTimer.Interval = interval;
        }
        #endregion
    }
}
