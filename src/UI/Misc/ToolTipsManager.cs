using HandyControl.Controls;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ListBox = System.Windows.Controls.ListBox;
using ListView = System.Windows.Controls.ListView;
using NumericUpDown = HandyControl.Controls.NumericUpDown;
using RadioButton = System.Windows.Controls.RadioButton;
using UserControl = System.Windows.Controls.UserControl;

namespace eft_dma_radar.UI.Misc
{
    public static class TooltipManager
    {

        public static void AssignLootTooltips(UserControl context)
        {
            if (context.FindName("chkShowLoot") is CheckBox chkShowLoot)
                chkShowLoot.ToolTip = "Toggle loot display on or off.";

            if (context.FindName("chkShowLootWishlist") is CheckBox chkShowLootWishlist)
                chkShowLootWishlist.ToolTip = "Tracks loot on your account's Loot Wishlist (Manual Adds Only, does not work for Automatically Added Items).";

            if (context.FindName("sldrPriceRange") is HandyControl.Controls.TextBox txtRegularValue)
                txtRegularValue.ToolTip = "Set the Minimum & Valuable ruble value for items to show";

            if (context.FindName("chkPricePerSlot") is CheckBox chkPricePerSlot)
                chkPricePerSlot.ToolTip = "Use price per slot instead of total item value.";

            if (context.FindName("rdbFleaPrices") is RadioButton rdbFleaPrices)
                rdbFleaPrices.ToolTip = "Loot prices use the optimal flea market price for the item based on ~realtime market value for displayed loot items.";

            if (context.FindName("rdbTraderPrices") is RadioButton rdbTraderPrices)
                rdbTraderPrices.ToolTip = "Loot prices use the highest trader price for displayed loot items.";

            if (context.FindName("chkHideCorpses") is CheckBox chkHideCorpses)
                chkHideCorpses.ToolTip = "Hide dead player & AI loot bodies.";

            if (context.FindName("chkShowMeds") is CheckBox chkShowMeds)
                chkShowMeds.ToolTip = "Only show medical items.";

            if (context.FindName("chkShowFood") is CheckBox chkShowFood)
                chkShowFood.ToolTip = "Only show food & drink items.";

            if (context.FindName("chkShowBackpacks") is CheckBox chkShowBackpacks)
                chkShowBackpacks.ToolTip = "Only show backpack loot.";

            if (context.FindName("txtLootToSearch") is HandyControl.Controls.TextBox txtLootToSearch)
                txtLootToSearch.ToolTip = "Comma-separated item names to search (e.g. 'GPU,keycard').";

            if (context.FindName("chkStaticContainers") is CheckBox chkStaticContainers)
                chkStaticContainers.ToolTip = "Shows static containers on the map. Due to recent Tarkov Anti-Cheat Measures, you cannot see what the contents are however.";

            if (context.FindName("sldrContainerDistance") is Slider sldrContainerDistance)
                sldrContainerDistance.ToolTip = "Maximum distance at which loot containers are rendered.";

            if (context.FindName("chkContainersSelectAll") is CheckBox chkContainersSelectAll)
                chkContainersSelectAll.ToolTip = "Toggle all containers on or off.";

            if (context.FindName("chkContainersHideSearched") is CheckBox chkContainersHideSearched)
                chkContainersHideSearched.ToolTip = "Hides containers that have already been searched by a networked entity (usually ONLY yourself).";
        }
        public static void AssignPlayerHistoryTooltips(UserControl context)
        {
            if (context.FindName("playerHistoryDataGrid") is DataGrid playerHistoryDataGrid)
                playerHistoryDataGrid.ToolTip = "Double click to add a recent player into the watchlist.";
        }

        public static void AssignWatchlistTooltips(UserControl context)
        {
            if (context.FindName("txtAccountID") is HandyControl.Controls.TextBox txtAccountID)
                txtAccountID.ToolTip = "The Account ID of the player.";

            if (context.FindName("txtReason") is HandyControl.Controls.TextBox txtReason)
                txtReason.ToolTip = "The reason why they are being watched.";

            if (context.FindName("btnClearForm") is Button btnClearForm)
                btnClearForm.ToolTip = "Clears the selected entry/form.";

            if (context.FindName("btnAddEntry") is Button btnAddEntry)
                btnAddEntry.ToolTip = "Add a new entry or update an existing one.";

            if (context.FindName("btnRemoveEntry") is Button btnRemoveEntry)
                btnRemoveEntry.ToolTip = "Remove the selected watchlist entry.";

            if (context.FindName("watchlistListView") is ListView watchlistListView)
                watchlistListView.ToolTip = "Click to select & edit an existing watchlist entry.";
        }
        public static void AssignLootFilterTooltips(UserControl context)
        {
            if (context.FindName("cboLootFilters") is HandyControl.Controls.ComboBox cboLootFilters)
                cboLootFilters.ToolTip = "The loot filter to modify.";

            if (context.FindName("txtNewGroupName") is HandyControl.Controls.TextBox txtNewGroupName)
                txtNewGroupName.ToolTip = "The name of the new loot filter.";

            if (context.FindName("btnRemoveGroup") is Button btnRemoveGroup)
                btnRemoveGroup.ToolTip = "Remove the selected loot filter.";

            if (context.FindName("btnAddGroup") is Button btnAddGroup)
                btnAddGroup.ToolTip = "Add a new loot filter.";

            if (context.FindName("chkEnabled") is CheckBox chkEnabled)
                chkEnabled.ToolTip = "Toggle the loot filter on/off.";

            if (context.FindName("chkStatic") is CheckBox chkStatic)
                chkStatic.ToolTip = "Removes the loot filter if unticked after restarting/closing radar.";

            if (context.FindName("chkNotify") is CheckBox chkNotify)
                chkNotify.ToolTip = "Notifies you if a selected item in the loot filter is present.";

            if (context.FindName("nudNotifyTime") is HandyControl.Controls.NumericUpDown nudNotifyTime)
                nudNotifyTime.ToolTip = "The repetition delay for loot item notifications (0 to disable).";

            if (context.FindName("nudGroupIndex") is HandyControl.Controls.NumericUpDown nudGroupIndex)
                nudGroupIndex.ToolTip = "The priority of displaying items (lower = displayed first)";

            if (context.FindName("txtGroupName") is HandyControl.Controls.TextBox txtGroupName)
                txtGroupName.ToolTip = "The name of the selected loot filter.";

            if (context.FindName("txtItemSearch") is HandyControl.Controls.TextBox txtItemSearch)
                txtItemSearch.ToolTip = "Type to search & filter available items.";

            if (context.FindName("btnAddItem") is Button btnAddItem)
                btnAddItem.ToolTip = "Add the selected item to your loot filter.";

            if (context.FindName("cboItems") is HandyControl.Controls.ComboBox cboItems)
                cboItems.ToolTip = "Available loot items to add.";

            if (context.FindName("itemsListView") is ListView itemsListView)
                itemsListView.ToolTip = "Your current filtered loot entries. Enable/disable or edit colors & notification status.";

            if (context.FindName("btnRemoveItem") is Button btnRemoveItem)
                btnRemoveItem.ToolTip = "Remove the selected item from your loot filter.";
        }

        public static void AssignESPTips(UserControl context)
        {
            if (context.FindName("chkEnableChams") is CheckBox chkEnableChams)
                chkEnableChams.ToolTip = "Enables the Chams feature. This will enable Chams on ALL players except yourself and teammates.";

            if (context.FindName("chkImportantItemChams") is CheckBox chkImportantItemChams)
                chkImportantItemChams.ToolTip = "Apply chams to important loot items.";

            if (context.FindName("chkQuestItemChams") is CheckBox chkQuestItemChams)
                chkQuestItemChams.ToolTip = "Apply chams to quest-related items.";

            if (context.FindName("chkContainerChams") is CheckBox chkContainerChams)
                chkContainerChams.ToolTip = "Apply chams to static containers.";

            if (context.FindName("cboChamsEntityType") is System.Windows.Controls.ComboBox cboChamsEntityType)
                cboChamsEntityType.ToolTip = "Select which entity to customize chams for.";

            if (context.FindName("rdbBasic") is RadioButton rdbBasic)
                rdbBasic.ToolTip = "These basic chams will only show when a target is VISIBLE. Cannot change color (always White).";

            if (context.FindName("rdbVisible") is RadioButton rdbVisible)
                rdbVisible.ToolTip = "These advanced chams will only show when a target is VISIBLE. You can change the color(s).";

            if (context.FindName("rdbVisCheckGlow") is RadioButton rdbVisCheckGlow)
                rdbVisCheckGlow.ToolTip = "Render glowing chams based on visibility check.";

            if (context.FindName("rdbVisCheckFlat") is RadioButton rdbVisCheckFlat)
                rdbVisCheckFlat.ToolTip = "Render flat chams with visibility checking.";

            if (context.FindName("rdbWireFrame") is RadioButton rdbWireFrame)
                rdbWireFrame.ToolTip = "Render wireframe-style chams.";

            if (context.FindName("btnchamsVisibleColor") is Button btnchamsVisibleColor)
                btnchamsVisibleColor.ToolTip = "Set chams color for visible targets.";

            if (context.FindName("btnchamsInvisibleColor") is Button btnchamsInvisibleColor)
                btnchamsInvisibleColor.ToolTip = "Set chams color for invisible targets.";

            if (context.FindName("chkEnableFuser") is CheckBox chkEnableFuser)
                chkEnableFuser.ToolTip = "Starts the ESP Window. This will render ESP over a black background. Move this window to the screen that is being fused.";

            if (context.FindName("chkAutoFullscreen") is CheckBox chkAutoFullscreen)
                chkAutoFullscreen.ToolTip = "Sets 'Auto Fullscreen' for the ESP Window.\nWhen set this will automatically go into full screen mode on the selected screen when the application starts.";

            if (context.FindName("cboHighAlert") is HandyControl.Controls.ComboBox cboHighAlert)
                cboHighAlert.ToolTip = "Enables the 'High Alert' ESP Feature. This will activate when you are being aimed at for longer than 0.5 seconds.\n" +
                    "Targets in your FOV (in front of you) will draw an aimline towards your character.\nTargets outside your FOV will draw the border of your screen red.\n" +
                    "None = Feature Disabled\nAllPlayers = Enabled for both players and bots (AI)\nHumansOnly = Enabled only for human-controlled players.";

            if (context.FindName("nudFPSCap") is NumericUpDown nudFPSCap)
                nudFPSCap.ToolTip = "Sets an FPS Cap for the ESP Window. Generally this can be the refresh rate of your Game PC Monitor. This also helps reduce resource usage on your Radar PC.";

            if (context.FindName("sldrFuserFontScale") is Slider sldrFuserFontScale)
                sldrFuserFontScale.ToolTip = "Sets the font scaling factor for the ESP Window.\nIf you are rendering at a really high resolution, you may want to increase this.";

            if (context.FindName("sldrFuserLineScale") is Slider sldrFuserLineScale)
                sldrFuserLineScale.ToolTip = "Sets the lines scaling factor for the ESP Window.\nIf you are rendering at a really high resolution, you may want to increase this.";

            if (context.FindName("chkCrosshairEnabled") is CheckBox chkCrosshairEnabled)
                chkCrosshairEnabled.ToolTip = "Toggles rendering a Crosshair on the ESP.";

            if (context.FindName("cboCrosshairType") is HandyControl.Controls.ComboBox cboCrosshairType)
                cboCrosshairType.ToolTip = "The type of Crosshair to display.";

            if (context.FindName("sldrFuserCrosshairScale") is Slider sldrFuserCrosshairScale)
                sldrFuserCrosshairScale.ToolTip = "Adjust the crosshair scale.";

            if (context.FindName("cboPlayerRenderMode") is HandyControl.Controls.ComboBox cboPlayerRenderMode)
                cboPlayerRenderMode.ToolTip = "Choose how players are displayed (e.g., Skeleton, Box or Head Dot).";

            if (context.FindName("chkFuserPlayerLabels") is CheckBox chkFuserPlayerLabels)
                chkFuserPlayerLabels.ToolTip = "Display entity label/name.";

            if (context.FindName("chkFuserPlayerWeapons") is CheckBox chkFuserPlayerWeapons)
                chkFuserPlayerWeapons.ToolTip = "Display entity's held weapon/ammo.";

            if (context.FindName("chkFuserPlayerDistance") is CheckBox chkFuserPlayerDistance)
                chkFuserPlayerDistance.ToolTip = "Display entity distance from LocalPlayer.";

            if (context.FindName("cboAIRenderMode") is HandyControl.Controls.ComboBox cboAIRenderMode)
                cboAIRenderMode.ToolTip = "Choose how AI are displayed (e.g., Skeleton, Box or Head Dot).";

            if (context.FindName("chkFuserAILabels") is CheckBox chkFuserAILabels)
                chkFuserAILabels.ToolTip = "Display entity label/name.";

            if (context.FindName("chkFuserAIWeapons") is CheckBox chkFuserAIWeapons)
                chkFuserAIWeapons.ToolTip = "Display entity's held weapon/ammo.";

            if (context.FindName("chkFuserAIDistance") is CheckBox chkFuserAIDistance)
                chkFuserAIDistance.ToolTip = "Display entity distance from LocalPlayer.";

            if (context.FindName("chkFuserLoot") is CheckBox chkFuserLoot)
                chkFuserLoot.ToolTip = "Enables the rendering of loot items in the ESP Window.";

            if (context.FindName("chkFuserExfils") is CheckBox chkFuserExfils)
                chkFuserExfils.ToolTip = "Enables the rendering of Exfil Points in the ESP Window.";

            if (context.FindName("chkFuserExplosives") is CheckBox chkFuserExplosives)
                chkFuserExplosives.ToolTip = "Enables the rendering of Grenades in the ESP Window.";

            if (context.FindName("chkFuserMagazine") is CheckBox chkFuserMagazine)
                chkFuserMagazine.ToolTip = "Shows your currently loaded Magazine Ammo Count/Type.";

            if (context.FindName("chkFuserDistances") is CheckBox chkFuserDistances)
                chkFuserDistances.ToolTip = "Enables the rendering of 'Distance' below ESP Entities. This is the In-Game distance from yourself and the entity.";

            if (context.FindName("chkFuserMines") is CheckBox chkFuserMines)
                chkFuserMines.ToolTip = "Display landmines.";

            if (context.FindName("chkFuserFireportAim") is CheckBox chkFuserFireportAim)
                chkFuserFireportAim.ToolTip = "Shows the base fireport trajectory on screen so you can see where bullets will go. Disappears when ADS.";

            if (context.FindName("chkFuserAimbotFOV") is CheckBox chkFuserAimbotFOV)
                chkFuserAimbotFOV.ToolTip = "Enables the rendering of an 'Aim FOV Circle' in the center of your ESP Window. This is used for Aimbot Targeting.";

            if (context.FindName("chkFuserRaidStats") is CheckBox chkFuserRaidStats)
                chkFuserRaidStats.ToolTip = "Displays Raid Stats (Player counts, etc.) in top right corner of ESP window.";

            if (context.FindName("chkFuserAimbotLock") is CheckBox chkFuserAimbotLock)
                chkFuserAimbotLock.ToolTip = "Enables the rendering of a line between your Fireport and your currently locked Aimbot Target.";

            if (context.FindName("chkFuserStatusText") is CheckBox chkFuserStatusText)
                chkFuserStatusText.ToolTip = "Displays status text in the top center of the screen (Aimbot Status, Wide Lean, etc.)";

            if (context.FindName("chkFuserFPS") is CheckBox chkFuserFPS)
                chkFuserFPS.ToolTip = "Enables the display of the ESP Rendering Rate (FPS) in the Top Left Corner of your ESP Window.";

            if (context.FindName("sldrFuserLootDistance") is Slider sldrFuserLootDistance)
                sldrFuserLootDistance.ToolTip = "Sets the Maximum Distance from LocalPlayer for regular loot to be rendered.";

            if (context.FindName("sldrFuserImportantLootDistance") is Slider sldrFuserImportantLootDistance)
                sldrFuserImportantLootDistance.ToolTip = "Sets the Maximum Distance from LocalPlayer for important loot to be rendered.";

            if (context.FindName("sldrFuserContainerDistance") is Slider sldrFuserContainerDistance)
                sldrFuserContainerDistance.ToolTip = "Sets the Maximum Distance from LocalPlayer for containers to be rendered.";

            if (context.FindName("sldrFuserQuestDistance") is Slider sldrFuserQuestDistance)
                sldrFuserQuestDistance.ToolTip = "Sets the Maximum Distance from LocalPlayer for Static Quest Items/Locations to be rendered. Quest Helper must be on.";

            if (context.FindName("sldrFuserExplosivesDistance") is Slider sldrFuserExplosivesDistance)
                sldrFuserExplosivesDistance.ToolTip = "Sets the Maximum Distance from LocalPlayer for explosives to be rendered.";
        }

        public static void AssignMemoryWritingTooltips(UserControl context)
        {
            if (context.FindName("chkMasterSwitch") is CheckBox chkMasterSwitch)
                chkMasterSwitch.ToolTip = "Enables/Disables the ability to use Memory Write Features. When this is disabled, it prevents any Memory Writing from occurring in the application.\n\n" +
                "Regarding 'Risk'\n" +
                "- The majority of risk stems from the fact that most of these features increase your power greatly, making other players more likely to report you.\n" +
                "- Player reports are the #1 risk to getting banned.\n" +
                "- None of these features are currently 'detected', but there is a VERY small risk that they could be in the future.";

            if (context.FindName("chkAdvancedWrites") is CheckBox chkAdvancedWrites)
                chkAdvancedWrites.ToolTip = "Enables Advanced Memory Writing Features. Includes (but not limited to):\n" +
                "- AntiPage Feature.\n" +
                "- Disable screen effects/Streamer Mode/Hide Raid Code\n" +
                "- Advanced Chams Options.\n" +
                "- Show proper AI Enemy Types (Passive).\n" +
                "- Enhanced reliability of some features (Passive)." +
                "\n\nWARNING: These features use a riskier injection technique. Use at your own risk.";

            if (context.FindName("btnAntiAFK") is Button btnAntiAFK)
                btnAntiAFK.ToolTip = "Enables the Anti-AFK Feature. Prevents the game from closing due to inactivity.\n" +
                "NOTE: Set this *right before* you go AFK while you are on the Tarkov Main Menu.\n" +
                "NOTE: If you leave the Main Menu, you may need to re-set this.\n" +
                "NOTE: If you have trouble setting this, your memory may be paged out. Try close/reopen the game.";

            if (context.FindName("btnGymHack") is Button btnGymHack)
                btnGymHack.ToolTip = "Enables the Gym Hack Feature which causes your workouts always to succeed.\n" +
                "NOTE: After enabling this feature you must start a workout within 15 seconds for the hack to be applied. Complete your first rep normally, and then it should activate for following reps.\n" +
                "NOTE: You must still 'left click' on each repetition.";

            if (context.FindName("chkRageMode") is CheckBox chkRageMode)
                chkRageMode.ToolTip = "Enables the Rage Mode feature. While enabled sets Recoil/Sway to 0% and Aimbot Bone is overriden to 'Head' for all targets.\nThis setting does not save on program exit.\n" +
                "WARNING: This is marked as a RISKY feature since it sets your recoil to 0% and you will always headshot, other players could notice.";

            if (context.FindName("chkEnableAimbot") is CheckBox chkEnableAimbot)
                chkEnableAimbot.ToolTip = "Enables the Aimbot (Silent Aim) Feature. We employ Aim Prediction (Online Raids Only) with our Aimbot to compensate for bullet drop/ballistics and target movement.\n" +
                "WARNING: This is marked as a RISKY feature since it makes it more likely for other players to report you. Use with care.";

            if (context.FindName("rdbFOV") is RadioButton rdbFOV)
                rdbFOV.ToolTip = "Enables the FOV (Field of View) Targeting Mode for Aimbot. This will prefer the target closest to the center of your screen within your FOV.";

            if (context.FindName("rdbCQB") is RadioButton rdbCQB)
                rdbCQB.ToolTip = "Enables the CQB (Close Quarters Battle) Targeting Mode for Aimbot.\nThis will prefer the target closest to your player *within your FOV*.";

            if (context.FindName("cboTargetBone") is HandyControl.Controls.ComboBox cboTargetBone)
                cboTargetBone.ToolTip = "Sets the Bone to target with the Aimbot.";

            if (context.FindName("sldrAimbotFOV") is Slider sldrAimbotFOV)
                sldrAimbotFOV.ToolTip = "Sets the FOV for Aimbot Targeting. Increase/Lower this to your preference. Please note when you ADS/Scope in, the FOV field becomes narrower.";

            if (context.FindName("chkAimbotSafeLock") is CheckBox chkAimbotSafeLock)
                chkAimbotSafeLock.ToolTip = "Unlocks the aimbot if your target leaves your FOV Radius.\n" +
                "NOTE: It is possible to 're-lock' another target (or the same target) after unlocking.";

            if (context.FindName("chkAimbotDisableReLock") is CheckBox chkAimbotDisableReLock)
                chkAimbotDisableReLock.ToolTip = "Disables 're-locking' onto a new target with aimbot when the current target dies/is no longer valid.\n Prevents accidentally killing multiple targets in quick succession before you can react.";

            if (context.FindName("chkAimbotAutoBone") is CheckBox chkAimbotAutoBone)
                chkAimbotAutoBone.ToolTip = "Automatically selects best bone target based on where you are aiming.";

            if (context.FindName("chkHeadshotAI") is CheckBox chkHeadshotAI)
                chkHeadshotAI.ToolTip = "Always headshot AI Targets regardless of other settings.";

            if (context.FindName("chkAimbotRandomBone") is CheckBox chkAimbotRandomBone)
                chkAimbotRandomBone.ToolTip = "Will select a random aimbot bone after each shot. You can set custom percentage values for body zones.\nNOTE: This will supersede silent aim 'auto bone'.";

            if (context.FindName("sldrAimbotRNGHead") is Slider sldrAimbotRNGHead)
                sldrAimbotRNGHead.ToolTip = "Chance of targeting the head.";

            if (context.FindName("sldrAimbotRNGTorso") is Slider sldrAimbotRNGTorso)
                sldrAimbotRNGTorso.ToolTip = "Chance of targeting the torso.";

            if (context.FindName("sldrAimbotRNGArms") is Slider sldrAimbotRNGArms)
                sldrAimbotRNGArms.ToolTip = "Chance of targeting the arms.";

            if (context.FindName("sldrAimbotRNGLegs") is Slider sldrAimbotRNGLegs)
                sldrAimbotRNGLegs.ToolTip = "Chance of targeting the legs.";

            // Weapon
            if (context.FindName("chkNoWeaponMalfunctions") is CheckBox chkNoWeaponMalfunctions)
                chkNoWeaponMalfunctions.ToolTip = "Enables the No Weapons Malfunction feature. This prevents your gun from failing to fire due to misfires/overheating/etc.\n" +
                "Once enabled this feature will remain enabled until you restart your game.\n" +
                "Stream Safe!";

            if (context.FindName("chkFastMags") is CheckBox chkFastMags)
                chkFastMags.ToolTip = "Allows you to pack/unpack magazines super fast.";

            if (context.FindName("chkFastWeaponOps") is CheckBox chkFastWeaponOps)
                chkFastWeaponOps.ToolTip = "Makes weapon operations (instant ADS, reloading mag,etc.) faster for your player.\n" +
                "NOTE: Trying to heal or do other actions while reloading a mag can cause the 'hands busy' bug.";

            if (context.FindName("chkNoRecoil") is CheckBox chkNoRecoil)
                chkNoRecoil.ToolTip = "Enables the No Recoil/Sway Write Feature. Mouseover the Recoil/Sway sliders for more info.\n" +
                "WARNING: This is marked as a RISKY feature since it reduces your recoil/sway, other players could notice your abnormal spray patterns.";

            if (context.FindName("sldrNoRecoilAmt") is Slider sldrNoRecoilAmt)
                sldrNoRecoilAmt.ToolTip = "Sets the percentage of normal recoil to apply (ex: 0 = 0% or no recoil). This affects the up/down motion of a gun while firing.";

            if (context.FindName("sldrNoSwayAmt") is Slider sldrNoSwayAmt)
                sldrNoSwayAmt.ToolTip = "Sets the percentage of scope sway to apply (ex: 0 = 0% or no sway). This affects the swaying motion when looking down your sights/scope.";

            // Movement
            if (context.FindName("chkInfiniteStamina") is CheckBox chkInfiniteStamina)
                chkInfiniteStamina.ToolTip = "Enables the Infinite Stamina feature. Prevents you from running out of stamina/breath, and bypasses the Fatigue debuff. Due to safety reasons you can only disable this after the raid has ended.\n" +
                "NOTE: Your footsteps will be silent, this is normal.\n" +
                "NOTE: You will not gain endurance/strength xp with this on.\n" +
                "NOTE: At higher weights you may get server desync. You can try disabling 1.2 Move Speed, or reducing your weight. MULE stims help here too.\n" +
                "WARNING: This is marked as a RISKY feature since other players can see you 'gliding' instead of running and is visually noticeable.";

            if (context.FindName("chkMoveSpeed") is CheckBox chkMoveSpeed)
                chkMoveSpeed.ToolTip = "Enables/Disables 1.2x Move Speed Feature. This causes your player to move 1.2 times faster.\n" +
                "NOTE: When used in conjunction with Infinite Stamina this can contribute to Server Desync at higher carry weights. Turn this off to reduce desync.\n" +
                "WARNING: This is marked as a RISKY feature since other players can see you moving faster than normal.";

            if (context.FindName("chkWideLean") is CheckBox chkWideLean)
                chkWideLean.ToolTip = "Enables/Disables Wide Lean Globally. You still need to set hotkeys in Hotkey Manager.\nWARNING: This is overall a riskier write feature.";

            if (context.FindName("sldrLeanAmt") is Slider sldrLeanAmt)
                sldrLeanAmt.ToolTip = "Sets the amount of lean to apply when using the Wide Lean feature. You may need to lower this if shots fail.";

            // World
            if (context.FindName("chkAlwaysDay") is CheckBox chkAlwaysDay)
                chkAlwaysDay.ToolTip = "Enables the Always Day/Sunny feature. This sets the In-Raid time to always 12 Noon (day), and sets the weather to sunny/clear.";

            if (context.FindName("chkFullBright") is CheckBox chkFullBright)
                chkFullBright.ToolTip = "Enables the Full Bright Feature. This will make the game world brighter.";

            if (context.FindName("chkLTW") is CheckBox chkLTW)
                chkLTW.ToolTip = "Enables Loot Through Walls Feature. This allows you to loot items through walls.\n" +
                "* You can loot most quest items / container items normally up to 3.8m.\n" +
                "* You can loot loose loot up to ~1m (may not always work either).\n" +
                "* To loot loose loot, some items you will need to 'ADS' with your firearm (Use the 'Toggle LTW Zoom' Hotkey), and it will zoom the camera through the wall. Find your item and loot it.\n" +
                "WARNING: Due to the complex nature of this feature, and the presence of Server-Side checks, it is marked as Risky.";

            if (context.FindName("sldrLTWZoom") is Slider sldrLTWZoom)
                sldrLTWZoom.ToolTip = "Sets the Zoom Amount for Loot Through Walls. This is how far the camera will zoom through the wall.";

            // Camera
            if (context.FindName("chkNoVisor") is CheckBox chkNoVisor)
                chkNoVisor.ToolTip = "Enables the No Visor feature. This removes the view obstruction from certain faceshields (like the Altyn/Killa Helmet) and gives you a clear view.";

            if (context.FindName("chkNightVision") is CheckBox chkNightVision)
                chkNightVision.ToolTip = "Enables the Night Vision feature. This allows you to see at night without the use of night vision gear.";

            if (context.FindName("chkThermalVision") is CheckBox chkThermalVision)
                chkThermalVision.ToolTip = "Enables the Thermal Vision feature. This allows you to see with the vision of clear T-7's.";

            if (context.FindName("chkThirdPerson") is CheckBox chkThirdPerson)
                chkThirdPerson.ToolTip = "Switch to third person view.";

            if (context.FindName("chkOwlMode") is CheckBox chkOwlMode)
                chkOwlMode.ToolTip = "360° camera with unlimited pitch and yaw.";

            if (context.FindName("chkDisableScreenEffects") is CheckBox chkDisableScreenEffects)
                chkDisableScreenEffects.ToolTip = "Disable blur, blood, flash & shaking screen effects.";

            if (context.FindName("chkFOVChanger") is CheckBox chkFOVChanger)
                chkFOVChanger.ToolTip = "Allows modifying your Field of View.";

            if (context.FindName("sldrFOVBase") is Slider sldrFOVBase)
                sldrFOVBase.ToolTip = "Sets the FOV for first person view";

            if (context.FindName("sldrADSFOV") is Slider sldrADSFOV)
                sldrADSFOV.ToolTip = "Sets the FOV for Aiming Down Sights (ADS)";

            if (context.FindName("sldrTPPFOV") is Slider sldrTPPFOV)
                sldrTPPFOV.ToolTip = "Sets the FFOV for third person view";

            // Misc
            if (context.FindName("chkStreamerMode") is CheckBox chkStreamerMode)
                chkStreamerMode.ToolTip = "Hide potentially sensitive content.";

            if (context.FindName("chkHideRaidCode") is CheckBox chkHideRaidCode)
                chkHideRaidCode.ToolTip = "Hide raid code from display and logs.";

            if (context.FindName("chkInstantPlant") is CheckBox chkInstantPlant)
                chkInstantPlant.ToolTip = "Instantly plant objectives without delay.";
        }

        public static void AssignGeneralSettingsTooltips(UserControl context)
        {
            if (context.FindName("chkMapSetup") is CheckBox chkMapSetup)
                chkMapSetup.ToolTip = "Toggles the 'Map Setup Helper' to assist with getting Map Bounds/Scaling";

            if (context.FindName("chkESPWidget") is CheckBox chkESPWidget)
                chkESPWidget.ToolTip = "Toggles the ESP 'Widget' that gives you a Mini ESP in the radar window. Can be moved.";

            if (context.FindName("chkPlayerInfoWidget") is CheckBox chkPlayerInfoWidget)
                chkPlayerInfoWidget.ToolTip = "Toggles the Player Info 'Widget' that gives you information about the players/bosses in your raid. Can be moved.";

            if (context.FindName("chkConnectGroups") is CheckBox chkConnectGroups)
                chkConnectGroups.ToolTip = "Connects players that are grouped up via semi-transparent green lines. Does not apply to your own party.";

            if (context.FindName("chkHideNames") is CheckBox chkHideNames)
                chkHideNames.ToolTip = chkHideNames.ToolTip = "Hides all player names from ESP overlays.";

            if (context.FindName("chkMines") is CheckBox chkMines)
                chkMines.ToolTip = "Shows proximity mines on the map and ESP.";

            if (context.FindName("chkTeammateAimlines") is CheckBox chkTeammateAimlines)
                chkTeammateAimlines.ToolTip = "When enabled makes teammate aimlines the same length as the main player";

            if (context.FindName("chkAIAimlines") is CheckBox chkAIAimlines)
                chkAIAimlines.ToolTip = "Enables dynamic aimlines for AI Players. When you are being aimed at the aimlines will extend.";

            if (context.FindName("chkDebugWidget") is CheckBox chkDebugWidget)
                chkDebugWidget.ToolTip = "Toggles the Debug 'Widget' (only draws radar fps). Can be moved.";

            if (context.FindName("chkLootInfoWidget") is CheckBox chkLootInfoWidget)
                chkLootInfoWidget.ToolTip = "Toggles the Loot 'Widget' that shows top items in the match as well as their quantity. Can be moved.";

            if (context.FindName("nudFPSLimit") is NumericUpDown nudFPSLimit)
                nudFPSLimit.ToolTip = "Sets an FPS Limit for the Radar. This also helps reduce resource usage on your Radar PC.";

            if (context.FindName("sldrUIScale") is Slider sldrUIScale)
                sldrUIScale.ToolTip = "Sets the scaling factor for the Radar/User Interface. For high resolution monitors you may want to increase this.";

            if (context.FindName("sldrMaxDistance") is Slider sldrMaxDistance)
                sldrMaxDistance.ToolTip = "Sets the 'Maximum Distance' for the Radar and many of it's features. This will affect Hostile Aimlines, Aimview, ESP, and Aimbot.\nIn most cases you don't need to set this over 500.";

            if (context.FindName("sldrAimlineLength") is Slider sldrAimlineLength)
                sldrAimlineLength.ToolTip = "Sets the Aimline Length for Local Player/Teammates";

            if (context.FindName("sldrContainerDistance") is Slider sldrContainerDistance)
                sldrContainerDistance.ToolTip = "Distance at which containers are displayed on the ESP.";

            if (context.FindName("cboMonitor") is HandyControl.Controls.ComboBox cboMonitor)
                cboMonitor.ToolTip = "Select which monitor to render ESP on.";

            if (context.FindName("btnRefreshMonitors") is Button btnRefreshMonitors)
                btnRefreshMonitors.ToolTip = "Automatically detects the resolution of your Game PC Monitor that Tarkov runs on, and sets the Width/Height fields. Game must be running.";

            if (context.FindName("txtGameWidth") is HandyControl.Controls.TextBox txtFuserWidth)
                txtFuserWidth.ToolTip = "The resolution Width of your Game PC Monitor that Tarkov runs on. This must be correctly set for Aimview/Aimbot/ESP to function properly.";

            if (context.FindName("txtGameHeight") is HandyControl.Controls.TextBox txtFuserHeight)
                txtFuserHeight.ToolTip = "The resolution Height of your Game PC Monitor that Tarkov runs on. This must be correctly set for Aimview/Aimbot/ESP to function properly.";

            if (context.FindName("chkQuestHelper") is CheckBox chkQuestHelper)
                chkQuestHelper.ToolTip = "Toggles the Quest Helper feature. This will display Items and Zones that you need to pickup/visit for quests that you currently have active.";

            if (context.FindName("listQuests") is ListBox listQuests)
                listQuests.ToolTip = "Active Quest List (populates once you are in raid). Uncheck a quest to untrack it.";

            if (context.FindName("btnWebRadarStart") is Button btnWebRadarStart)
                btnWebRadarStart.ToolTip = "Starts the Web Radar server.";

            if (context.FindName("chkWebRadarUPnP") is CheckBox chkWebRadarUPnP)
                chkWebRadarUPnP.ToolTip = "Attempts to open the port automatically using UPnP.";

            if (context.FindName("lblWebRadarLink") is TextBlock lblWebRadarLink)
                lblWebRadarLink.ToolTip = "Click to open your Web Radar URL in the browser.";

            if (context.FindName("txtWebRadarBindIP") is HandyControl.Controls.TextBox txtWebRadarBindIP)
                txtWebRadarBindIP.ToolTip = "IP address the server will bind to.";

            if (context.FindName("txtWebRadarPort") is HandyControl.Controls.TextBox txtWebRadarPort)
                txtWebRadarPort.ToolTip = "Port number for the Web Radar server.";

            if (context.FindName("txtWebRadarTickRate") is HandyControl.Controls.TextBox txtWebRadarTickRate)
                txtWebRadarTickRate.ToolTip = "How often the Web Radar sends updates (in Hz).";

            if (context.FindName("txtWebRadarPassword") is HandyControl.Controls.TextBox txtWebRadarPassword)
                txtWebRadarPassword.ToolTip = "Password required to connect to the Web Radar.";

            if (context.FindName("cboTheme") is HandyControl.Controls.ComboBox cboTheme)
                cboTheme.ToolTip = "Choose between Dark and Light themes.";

            if (context.FindName("hotkeyListView") is ListView hotkeyListView)
                hotkeyListView.ToolTip = "Displays all assigned hotkeys.";

            if (context.FindName("btnAddHotkey") is Button btnAddHotkey)
                btnAddHotkey.ToolTip = "Add a new hotkey binding.";

            if (context.FindName("btnRemoveHotkey") is Button btnRemoveHotkey)
                btnRemoveHotkey.ToolTip = "Remove the selected hotkey.";

            if (context.FindName("cboAction") is HandyControl.Controls.ComboBox cboAction)
                cboAction.ToolTip = "Select an action to assign a hotkey for.";

            if (context.FindName("cboKey") is HandyControl.Controls.ComboBox cboKey)
                cboKey.ToolTip = "Select the key that will trigger the selected action.";

            if (context.FindName("rdbOnKey") is RadioButton rdbOnKey)
                rdbOnKey.ToolTip = "Trigger the action while holding the key down.";

            if (context.FindName("rdbToggle") is RadioButton rdbToggle)
                rdbToggle.ToolTip = "Toggle the action on and off with the key.";
        }
    }
}
