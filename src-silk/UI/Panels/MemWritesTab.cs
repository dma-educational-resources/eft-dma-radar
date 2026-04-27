using ImGuiNET;

namespace eft_dma_radar.Silk.UI.Panels
{
    internal static partial class SettingsPanel
    {
        private static void DrawMemWritesTab()
        {
            if (!ImGui.BeginTabItem("\u270F Mem Writes"))
                return;

            ImGui.Spacing();

            bool masterEnabled = Config.MemWritesEnabled;
            if (ImGui.Checkbox("Enable Memory Writes", ref masterEnabled))
            {
                Config.MemWritesEnabled = masterEnabled;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Master toggle — enables all active memory write features");

            if (!masterEnabled)
                ImGui.BeginDisabled();

            // ═══════════════════════════════════════════════════════════════
            // Weapons
            // ═══════════════════════════════════════════════════════════════
            ImGui.Spacing();
            ImGui.SeparatorText("Weapons");

            float halfWidth = ImGui.GetContentRegionAvail().X * 0.5f;

            // ── No Recoil ──
            bool noRecoil = Config.MemWrites.NoRecoil;
            if (ImGui.Checkbox("No Recoil", ref noRecoil))
            {
                Config.MemWrites.NoRecoil = noRecoil;
                Config.MarkDirty();
            }
            if (noRecoil)
            {
                ImGui.Indent(16);
                ImGui.SetNextItemWidth(180);
                int recoilAmt = Config.MemWrites.NoRecoilAmount;
                if (ImGui.SliderInt("Recoil Amount %##nr", ref recoilAmt, 0, 100))
                {
                    Config.MemWrites.NoRecoilAmount = recoilAmt;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("0 = no recoil, 100 = full recoil");

                ImGui.SetNextItemWidth(180);
                int swayAmt = Config.MemWrites.NoSwayAmount;
                if (ImGui.SliderInt("Sway Amount %##ns", ref swayAmt, 0, 100))
                {
                    Config.MemWrites.NoSwayAmount = swayAmt;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("0 = no sway, 100 = full sway");
                ImGui.Unindent(16);
            }

            // ── Mag Drills ──
            ImGui.SameLine(halfWidth);
            bool magDrills = Config.MemWrites.MagDrills;
            if (ImGui.Checkbox("Mag Drills", ref magDrills))
            {
                Config.MemWrites.MagDrills = magDrills;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Fast magazine load/unload speed");

            // ── Disable Weapon Collision ──
            bool weapCol = Config.MemWrites.DisableWeaponCollision;
            if (ImGui.Checkbox("Disable Weapon Collision", ref weapCol))
            {
                Config.MemWrites.DisableWeaponCollision = weapCol;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Prevent weapon from folding when near walls");

            // ═══════════════════════════════════════════════════════════════
            // Movement
            // ═══════════════════════════════════════════════════════════════
            ImGui.Spacing();
            ImGui.SeparatorText("Movement");

            // Row 1: Infinite Stamina | Fast Duck
            bool infStamina = Config.MemWrites.InfStamina;
            if (ImGui.Checkbox("Infinite Stamina", ref infStamina))
            {
                Config.MemWrites.InfStamina = infStamina;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Refill stamina and oxygen when they drop below 33%");

            ImGui.SameLine(halfWidth);
            bool fastDuck = Config.MemWrites.FastDuck;
            if (ImGui.Checkbox("Fast Duck", ref fastDuck))
            {
                Config.MemWrites.FastDuck = fastDuck;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Instant crouch/stand transitions");

            // Row 2: No Inertia | Mule Mode
            bool noInertia = Config.MemWrites.NoInertia;
            if (ImGui.Checkbox("No Inertia", ref noInertia))
            {
                Config.MemWrites.NoInertia = noInertia;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove movement inertia for instant direction changes");

            ImGui.SameLine(halfWidth);
            bool mule = Config.MemWrites.MuleMode;
            if (ImGui.Checkbox("M.U.L.E Mode", ref mule))
            {
                Config.MemWrites.MuleMode = mule;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove overweight penalties (movement, sprint, inertia)");

            // Row 3: Wide Lean | Long Jump
            bool wideLean = Config.MemWrites.WideLean.Enabled;
            if (ImGui.Checkbox("Wide Lean", ref wideLean))
            {
                Config.MemWrites.WideLean.Enabled = wideLean;
                Config.MarkDirty();
            }
            if (wideLean)
            {
                ImGui.Indent(16);
                ImGui.SetNextItemWidth(180);
                float wlAmt = Config.MemWrites.WideLean.Amount;
                if (ImGui.SliderFloat("Amount##wl", ref wlAmt, 0.1f, 5f, "%.1f"))
                {
                    Config.MemWrites.WideLean.Amount = wlAmt;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Lean offset amount (higher = wider lean)");

                var dirNames = _wideLeanDirNames;
                int dirIdx = (int)WideLean.Direction;
                ImGui.SetNextItemWidth(180);
                if (ImGui.Combo("Direction##wl", ref dirIdx, dirNames, dirNames.Length))
                {
                    WideLean.Direction = (WideLean.EWideLeanDirection)dirIdx;
                }
                ImGui.Unindent(16);
            }

            ImGui.SameLine(halfWidth);
            bool longJump = Config.MemWrites.LongJump.Enabled;
            if (ImGui.Checkbox("Long Jump", ref longJump))
            {
                Config.MemWrites.LongJump.Enabled = longJump;
                Config.MarkDirty();
            }
            if (longJump)
            {
                ImGui.Indent(16);
                ImGui.SetNextItemWidth(180);
                float ljMult = Config.MemWrites.LongJump.Multiplier;
                if (ImGui.SliderFloat("Multiplier##lj", ref ljMult, 1f, 10f, "%.1fx"))
                {
                    Config.MemWrites.LongJump.Multiplier = ljMult;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Air control multiplier (higher = longer jumps)");
                ImGui.Unindent(16);
            }

            // Row 4: Move Speed
            bool moveSpeed = Config.MemWrites.MoveSpeed.Enabled;
            if (ImGui.Checkbox("Move Speed", ref moveSpeed))
            {
                Config.MemWrites.MoveSpeed.Enabled = moveSpeed;
                Config.MarkDirty();
            }
            if (moveSpeed)
            {
                ImGui.Indent(16);
                ImGui.SetNextItemWidth(180);
                float mult = Config.MemWrites.MoveSpeed.Multiplier;
                if (ImGui.SliderFloat("Multiplier##ms", ref mult, 0.5f, 3.0f, "%.2fx"))
                {
                    Config.MemWrites.MoveSpeed.Multiplier = mult;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Animator speed multiplier (1.0 = normal, disabled when overweight)");
                ImGui.Unindent(16);
            }

            // ═══════════════════════════════════════════════════════════════
            // World
            // ═══════════════════════════════════════════════════════════════
            ImGui.Spacing();
            ImGui.SeparatorText("World");

            // Row 1: Full Bright | Extended Reach
            bool fb = Config.MemWrites.FullBright.Enabled;
            if (ImGui.Checkbox("Full Bright", ref fb))
            {
                Config.MemWrites.FullBright.Enabled = fb;
                Config.MarkDirty();
            }
            if (fb)
            {
                ImGui.Indent(16);
                ImGui.SetNextItemWidth(180);
                float brightness = Config.MemWrites.FullBright.Brightness;
                if (ImGui.SliderFloat("Brightness##fb", ref brightness, 0f, 2f, "%.2f"))
                {
                    Config.MemWrites.FullBright.Brightness = brightness;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Ambient light intensity (1.0 = full white)");
                ImGui.Unindent(16);
            }

            ImGui.SameLine(halfWidth);
            bool reach = Config.MemWrites.ExtendedReach.Enabled;
            if (ImGui.Checkbox("Extended Reach", ref reach))
            {
                Config.MemWrites.ExtendedReach.Enabled = reach;
                Config.MarkDirty();
            }
            if (reach)
            {
                ImGui.Indent(16);
                ImGui.SetNextItemWidth(180);
                float reachDist = Config.MemWrites.ExtendedReach.Distance;
                if (ImGui.SliderFloat("Distance##er", ref reachDist, 1f, 20f, "%.1fm"))
                {
                    Config.MemWrites.ExtendedReach.Distance = reachDist;
                    Config.MarkDirty();
                }
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("Loot/door interaction distance (default ~1.3m)");
                ImGui.Unindent(16);
            }

            // ═══════════════════════════════════════════════════════════════
            // Camera
            // ═══════════════════════════════════════════════════════════════
            ImGui.Spacing();
            ImGui.SeparatorText("Camera");

            // Row 1: No Visor | Night Vision
            bool noVisor = Config.MemWrites.NoVisor;
            if (ImGui.Checkbox("No Visor", ref noVisor))
            {
                Config.MemWrites.NoVisor = noVisor;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove visor overlay effect (e.g. face shield darkening)");

            ImGui.SameLine(halfWidth);
            bool nv = Config.MemWrites.NightVision;
            if (ImGui.Checkbox("Night Vision", ref nv))
            {
                Config.MemWrites.NightVision = nv;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Force NightVision component on (no NVG required)");

            // Row 2: Thermal Vision | Third Person
            bool thermal = Config.MemWrites.ThermalVision;
            if (ImGui.Checkbox("Thermal Vision", ref thermal))
            {
                Config.MemWrites.ThermalVision = thermal;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Force ThermalVision component on (auto-disables while ADS)");

            ImGui.SameLine(halfWidth);
            bool thirdPerson = Config.MemWrites.ThirdPerson;
            if (ImGui.Checkbox("Third Person", ref thirdPerson))
            {
                Config.MemWrites.ThirdPerson = thirdPerson;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Move camera behind player for third-person view");

            // Row 3: Owl Mode | Disable Frostbite
            bool owl = Config.MemWrites.OwlMode;
            if (ImGui.Checkbox("Owl Mode", ref owl))
            {
                Config.MemWrites.OwlMode = owl;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove mouse look limits (360° head rotation)");

            ImGui.SameLine(halfWidth);
            bool frostbite = Config.MemWrites.DisableFrostbite;
            if (ImGui.Checkbox("Disable Frostbite", ref frostbite))
            {
                Config.MemWrites.DisableFrostbite = frostbite;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove frostbite screen overlay effect");

            // ═══════════════════════════════════════════════════════════════
            // Misc
            // ═══════════════════════════════════════════════════════════════
            ImGui.Spacing();
            ImGui.SeparatorText("Misc");

            // Row 1: Instant Plant | Med Panel
            bool instantPlant = Config.MemWrites.InstantPlant;
            if (ImGui.Checkbox("Instant Plant", ref instantPlant))
            {
                Config.MemWrites.InstantPlant = instantPlant;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Near-instant planting (e.g. quest items)");

            ImGui.SameLine(halfWidth);
            bool medPanel = Config.MemWrites.MedPanel;
            if (ImGui.Checkbox("Med Panel", ref medPanel))
            {
                Config.MemWrites.MedPanel = medPanel;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show med effect using panel (health effects UI)");

            // Row 2: Disable Inventory Blur
            bool invBlur = Config.MemWrites.DisableInventoryBlur;
            if (ImGui.Checkbox("Disable Inventory Blur", ref invBlur))
            {
                Config.MemWrites.DisableInventoryBlur = invBlur;
                Config.MarkDirty();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Remove background blur when inventory is open");

            if (!masterEnabled)
                ImGui.EndDisabled();

            ImGui.EndTabItem();
        }
    }
}
