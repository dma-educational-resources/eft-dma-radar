﻿using eft_dma_radar.Common.Maps;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.Tarkov.Loot;
using eft_dma_radar.UI.Misc;
using MessagePack;
using System.Collections.Generic;
using System.Linq;

namespace eft_dma_radar.Tarkov.WebRadar.Data
{
    [MessagePackObject]
    public readonly struct WebRadarPlayer
    {
        // ============================================================
        // CORE DATA
        // ============================================================

        [Key(0)]  public string Name { get; init; }
        [Key(1)]  public WebPlayerType Type { get; init; }
        [Key(2)]  public bool IsActive { get; init; }
        [Key(3)]  public bool IsAlive { get; init; }
        [Key(4)]  public int Value { get; init; }

        [Key(5)]  public string PrimaryWeapon { get; init; }
        [Key(6)]  public string SecondaryWeapon { get; init; }
        [Key(7)]  public string Armor { get; init; }
        [Key(8)]  public string Helmet { get; init; }
        [Key(9)]  public string Backpack { get; init; }
        [Key(10)] public string Rig { get; init; }

        [Key(11)] public float KD { get; init; }
        [Key(12)] public float TotalHoursPlayed { get; init; }
        [Key(13)] public bool IsAiming { get; init; }
        [Key(14)] public float ZoomLevel { get; init; }

        [Key(15)] public IEnumerable<WebRadarLoot> Loot { get; init; }
        [Key(16)] public int GroupId { get; init; }

        // ============================================================
        // DERIVED FLAGS
        // ============================================================

        [Key(17)] public bool IsLocal { get; init; }
        [Key(18)] public bool IsFriendly { get; init; }
        [Key(19)] public bool IsHuman { get; init; }
        [Key(20)] public string TypeName { get; init; }

        // ============================================================
        // FLATTENED TRANSFORM (JS SAFE)
        // ============================================================

        [Key(21)] public float X { get; init; }
        [Key(22)] public float Y { get; init; }
        [Key(23)] public float Z { get; init; }
        [Key(24)] public float Yaw { get; init; }

        // ============================================================
        // FACTORY
        // ============================================================

        public static WebRadarPlayer CreateFromPlayer(Player player)
        {
            if (player == null)
            {
                return new WebRadarPlayer
                {
                    Name = "Unknown",
                    Type = WebPlayerType.Bot,
                    TypeName = "Bot",
                    IsActive = false,
                    IsAlive = false
                };
            }

            bool isLocal = player is LocalPlayer;
            bool isHuman = player.IsHuman;
            bool isFriendly = player.IsFriendly;

            WebPlayerType type =
                isLocal ? WebPlayerType.LocalPlayer :
                isFriendly ? WebPlayerType.Teammate :
                isHuman ?
                    (player.IsScav ? WebPlayerType.PlayerScav : WebPlayerType.Player)
                    : WebPlayerType.Bot;

            float kd = 0f;
            float hours = 0f;
            bool isAiming = false;

            if (player is ObservedPlayer op)
            {
                kd = op.Profile?.Overall_KD ?? 0f;
                hours = op.Profile?.Hours ?? 0f;
                isAiming = op.IsAiming;
            }
            else if (player is LocalPlayer lp)
            {
                isAiming = lp.CheckIfADS();
            }
            var map = XMMapManager.Map;
            var mappos = player.Position.ToMapPos(map.Config);
            var pos = player.Position;
            var yaw = player.MapRotation;

            return new WebRadarPlayer
            {
                Name = player.Name ?? "Unknown",
                Type = type,
                TypeName = type.ToString(),

                IsActive = player.IsActive,
                IsAlive = player.IsAlive,
                IsLocal = isLocal,
                IsFriendly = isFriendly,
                IsHuman = isHuman,

                X = mappos.X,
                Y = mappos.Y,
                Z = pos.Z,
                Yaw = yaw,

                Value = player.Gear?.Value ?? 0,
                KD = kd,
                TotalHoursPlayed = hours,
                IsAiming = isAiming,
                ZoomLevel = player.ZoomLevel,
                Loot = player.Gear?.Loot?.Select(WebRadarLoot.CreateFromLoot),
                GroupId = player.NetworkGroupID,

                PrimaryWeapon = player.Gear?.Equipment?.TryGetValue("FirstPrimaryWeapon", out var p) == true ? p.Long : "None",
                SecondaryWeapon = player.Gear?.Equipment?.TryGetValue("SecondPrimaryWeapon", out var s) == true ? s.Long : "None",
                Armor = player.Gear?.Equipment?.TryGetValue("ArmorVest", out var a) == true ? a.Long : "None",
                Helmet = player.Gear?.Equipment?.TryGetValue("Headwear", out var h) == true ? h.Long : "None",
                Backpack = player.Gear?.Equipment?.TryGetValue("Backpack", out var b) == true ? b.Long : "None",
                Rig = player.Gear?.Equipment?.TryGetValue("TacticalVest", out var r) == true ? r.Long : "None"
            };
        }
    }
}
