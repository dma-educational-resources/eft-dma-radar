using eft_dma_radar.Silk.UI.Panels;
using eft_dma_radar.Silk.UI.Widgets;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.Maths;

namespace eft_dma_radar.Silk.UI
{
    internal static partial class RadarWindow
    {
        #region Input Handling

        private static void OnMouseDown(IMouse mouse, MouseButton button)
        {
            if (!InRaid)
                return;

            var pos = new Vector2(mouse.Position.X, mouse.Position.Y);
            var sScale = UIScale;
            float scenePx = pos.X / sScale;
            float scenePy = pos.Y / sScale;

            // Killfeed overlay drag takes priority over map pan
            if (button == MouseButton.Left && Config.ShowKillFeed
                && KillfeedBounds.Width > 0 && KillfeedBounds.Contains(scenePx, scenePy))
            {
                _killfeedDragging = true;
                _killfeedDragOffset = new Vector2(scenePx - KillfeedBounds.Left, scenePy - KillfeedBounds.Top);
                _lastMousePosition = pos;
                return;
            }

            _mouseDown = true;
            _lastMousePosition = pos;
        }

        private static void OnMouseUp(IMouse mouse, MouseButton button)
        {
            if (_killfeedDragging)
            {
                _killfeedDragging = false;
                Config.Save();
            }
            _mouseDown = false;
        }

        private static void OnMouseMove(IMouse mouse, Vector2 position)
        {
            if (_killfeedDragging)
            {
                var scale = UIScale;
                Config.KillFeedPosX = (position.X / scale) - _killfeedDragOffset.X;
                Config.KillFeedPosY = (position.Y / scale) - _killfeedDragOffset.Y;
                _lastMousePosition = position;
                return;
            }

            if (_mouseDown && _freeMode)
            {
                var deltaX = position.X - _lastMousePosition.X;
                var deltaY = position.Y - _lastMousePosition.Y;

                _mapPanPosition.X -= deltaX;
                _mapPanPosition.Y -= deltaY;

                _lastMousePosition = position;
                return;
            }

            if (!InRaid)
            {
                ClearMouseoverState();
                return;
            }

            // Dead zone — skip expensive hit-testing when mouse barely moved
            float dx = position.X - _lastHitTestMousePos.X;
            float dy = position.Y - _lastHitTestMousePos.Y;
            if (dx * dx + dy * dy < HitTestDeadZone * HitTestDeadZone)
                return;
            _lastHitTestMousePos = position;

            var curParams = GetCurrentMapParams();
            if (curParams is null)
            {
                ClearMouseoverState();
                return;
            }

            var mp = curParams.Value;
            var mousePos = position;
            float hitRadius = 12f * UIScale;

            // Pre-compute world bounds for culling — entities off-screen can't be hovered
            var worldBounds = mp.GetWorldBounds(0f);

            // Check players (highest priority)
            if (TryHitTestPlayers(mp, worldBounds, mousePos, hitRadius))
                return;

            // Check loot + corpses (only when loot is visible)
            if (!Config.BattleMode && Config.ShowLoot)
            {
                float closestLootDist = TryFindClosestLoot(mp, worldBounds, mousePos, out var closestLoot);
                float closestCorpseDist = TryFindClosestCorpse(mp, worldBounds, mousePos, out var closestCorpse);

                // Pick the closest between loot and corpse
                if (closestCorpseDist < hitRadius && closestCorpse is not null
                    && closestCorpseDist <= closestLootDist)
                {
                    SetMouseover(corpse: closestCorpse);
                    return;
                }

                if (closestLootDist < hitRadius && closestLoot is not null)
                {
                    SetMouseover(loot: closestLoot);
                    return;
                }
            }

            // Check exfils
            if (Config.ShowExfils && TryHitTestExfils(mp, worldBounds, mousePos, hitRadius))
                return;

            // Check transits (lowest priority)
            if (Config.ShowTransits && TryHitTestTransits(mp, worldBounds, mousePos, hitRadius))
                return;

            ClearMouseoverState();
        }

        /// <summary>Returns the current map params (approximate — for mouseover hit-testing only).</summary>
        private static MapParams? GetCurrentMapParams()
        {
            var map = MapManager.Map;
            if (map is null || LocalPlayer is null)
                return null;
            var scale = UIScale;
            var canvasSize = new SKSize(_window.Size.X / scale, _window.Size.Y / scale);
            var lp = MapParams.ToMapPos(LocalPlayer.Position, map.Config);
            if (_freeMode)
            {
                var pan = _mapPanPosition;
                return map.GetParameters(canvasSize, _zoom, ref pan);
            }
            return map.GetParameters(canvasSize, _zoom, ref lp);
        }

        private static void OnMouseScroll(IMouse mouse, ScrollWheel scroll)
        {
            if (!InRaid)
                return;

            int zoomChange = scroll.Y > 0 ? -ZOOM_STEP : ZOOM_STEP;
            var newZoom = Math.Max(1, Math.Min(200, _zoom + zoomChange));

            if (newZoom == _zoom)
                return;

            if (_freeMode && zoomChange < 0)
            {
                var zoomFactor = (float)newZoom / _zoom;
                var canvasCenter = new Vector2(_window.Size.X / 2f, _window.Size.Y / 2f);
                var mouseOffset = new Vector2(mouse.Position.X - canvasCenter.X, mouse.Position.Y - canvasCenter.Y);

                var panAdjustment = mouseOffset * (1 - zoomFactor) * ZOOM_TO_MOUSE_STRENGTH;
                _mapPanPosition.X += panAdjustment.X;
                _mapPanPosition.Y += panAdjustment.Y;
            }

            _zoom = newZoom;
        }

        private static void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
        {
            // Don't handle shortcuts when ImGui text inputs have focus
            if (ImGui.GetIO().WantCaptureKeyboard)
                return;

            switch (key)
            {
                case Key.F:
                    _freeMode = !_freeMode;
                    if (!_freeMode)
                        _mapPanPosition = default;
                    break;
                case Key.B:
                    Config.BattleMode = !Config.BattleMode;
                    break;
                case Key.S:
                    SettingsPanel.IsOpen = !SettingsPanel.IsOpen;
                    break;
                case Key.L:
                    LootFiltersPanel.IsOpen = !LootFiltersPanel.IsOpen;
                    break;
                case Key.P:
                    PlayerInfoWidget.IsOpen = !PlayerInfoWidget.IsOpen;
                    break;
                case Key.T:
                    LootWidget.IsOpen = !LootWidget.IsOpen;
                    break;
                case Key.A:
                    AimviewWidget.IsOpen = !AimviewWidget.IsOpen;
                    break;
                case Key.H:
                    HideoutPanel.IsOpen = !HideoutPanel.IsOpen;
                    break;
                case Key.Q:
                    QuestPanel.IsOpen = !QuestPanel.IsOpen;
                    break;
                case Key.Escape:
                    SettingsPanel.IsOpen = false;
                    LootFiltersPanel.IsOpen = false;
                    HotkeyManagerPanel.IsOpen = false;
                    HideoutPanel.IsOpen = false;
                    QuestPanel.IsOpen = false;
                    QuestPlannerPanel.IsOpen = false;
                    PlayerInfoWidget.IsOpen = false;
                    LootWidget.IsOpen = false;
                    AimviewWidget.IsOpen = false;
                    break;
            }
        }

        #endregion

        #region Hit-Testing Helpers

        private static void ClearMouseoverState()
        {
            _mouseOverPlayer = null;
            _mouseOverLoot = null;
            _mouseOverCorpse = null;
            _mouseOverExfil = null;
            _mouseOverTransit = null;
            MouseoverGroup = null;
        }

        private static void SetMouseover(
            Player? player = null,
            LootItem? loot = null,
            LootCorpse? corpse = null,
            Exfil? exfil = null,
            TransitPoint? transit = null,
            int? group = null)
        {
            _mouseOverPlayer = player;
            _mouseOverLoot = loot;
            _mouseOverCorpse = corpse;
            _mouseOverExfil = exfil;
            _mouseOverTransit = transit;
            MouseoverGroup = group;
        }

        private static bool TryHitTestPlayers(MapParams mp, WorldBounds worldBounds, Vector2 mousePos, float hitRadius)
        {
            Player? closest = null;
            float closestDist = float.MaxValue;

            var players = AllPlayers;
            if (players is not null)
            {
                foreach (var p in players)
                {
                    if (p.IsLocalPlayer || !p.IsActive || !p.IsAlive)
                        continue;
                    if (!worldBounds.Contains(p.Position))
                        continue;
                    var screenPos = mp.ToScreenPos(MapParams.ToMapPos(p.Position, mp.Config));
                    float dist = Vector2.Distance(new Vector2(screenPos.X, screenPos.Y), mousePos);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest = p;
                    }
                }
            }

            if (closestDist < hitRadius && closest is not null)
            {
                int? group = closest.IsHuman && closest.IsHostile && closest.SpawnGroupID != -1
                    ? closest.SpawnGroupID
                    : null;
                SetMouseover(player: closest, group: group);
                return true;
            }

            return false;
        }

        private static float TryFindClosestLoot(MapParams mp, WorldBounds worldBounds, Vector2 mousePos, out LootItem? closest)
        {
            closest = null;
            float closestDist = float.MaxValue;

            var loot = Memory.Loot;
            if (loot is not null)
            {
                foreach (var item in loot)
                {
                    if (!item.ShouldDraw())
                        continue;
                    if (!worldBounds.Contains(item.Position))
                        continue;
                    var screenPos = mp.ToScreenPos(MapParams.ToMapPos(item.Position, mp.Config));
                    float dist = Vector2.Distance(new Vector2(screenPos.X, screenPos.Y), mousePos);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest = item;
                    }
                }
            }

            return closestDist;
        }

        private static float TryFindClosestCorpse(MapParams mp, WorldBounds worldBounds, Vector2 mousePos, out LootCorpse? closest)
        {
            closest = null;
            float closestDist = float.MaxValue;

            var corpses = Memory.Corpses;
            if (corpses is not null)
            {
                foreach (var c in corpses)
                {
                    if (!worldBounds.Contains(c.Position))
                        continue;
                    var screenPos = mp.ToScreenPos(MapParams.ToMapPos(c.Position, mp.Config));
                    float dist = Vector2.Distance(new Vector2(screenPos.X, screenPos.Y), mousePos);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest = c;
                    }
                }
            }

            return closestDist;
        }

        private static bool TryHitTestExfils(MapParams mp, WorldBounds worldBounds, Vector2 mousePos, float hitRadius)
        {
            Exfil? closest = null;
            float closestDist = float.MaxValue;

            var exfils = Memory.Exfils;
            if (exfils is not null)
            {
                foreach (var e in exfils)
                {
                    if (!worldBounds.Contains(e.Position))
                        continue;
                    var screenPos = mp.ToScreenPos(MapParams.ToMapPos(e.Position, mp.Config));
                    float dist = Vector2.Distance(new Vector2(screenPos.X, screenPos.Y), mousePos);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest = e;
                    }
                }
            }

            if (closestDist < hitRadius && closest is not null)
            {
                SetMouseover(exfil: closest);
                return true;
            }

            return false;
        }

        private static bool TryHitTestTransits(MapParams mp, WorldBounds worldBounds, Vector2 mousePos, float hitRadius)
        {
            TransitPoint? closest = null;
            float closestDist = float.MaxValue;

            var transits = Memory.Transits;
            if (transits is not null)
            {
                foreach (var t in transits)
                {
                    if (!worldBounds.Contains(t.Position))
                        continue;
                    var screenPos = mp.ToScreenPos(MapParams.ToMapPos(t.Position, mp.Config));
                    float dist = Vector2.Distance(new Vector2(screenPos.X, screenPos.Y), mousePos);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        closest = t;
                    }
                }
            }

            if (closestDist < hitRadius && closest is not null)
            {
                SetMouseover(transit: closest);
                return true;
            }

            return false;
        }

        #endregion
    }
}
