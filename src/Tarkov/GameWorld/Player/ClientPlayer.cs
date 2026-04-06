#pragma warning disable IDE0130
using eft_dma_radar.Tarkov.Unity;
using static SDK.Enums;
using eft_dma_radar.Tarkov.EFTPlayer.Plugins;
using eft_dma_radar.DMA.ScatterAPI;

namespace eft_dma_radar.Tarkov.EFTPlayer
{
    public class ClientPlayer : Player
    {
        /// <summary>
        /// EFT.Profile Address
        /// </summary>
        public ulong Profile { get; }
        /// <summary>
        /// ICharacterController
        /// </summary>
        public ulong CharacterController { get; }
        /// <summary>
        /// Procedural Weapon Animation
        /// </summary>
        public new ulong PWA { get; }
        /// <summary>
        /// PlayerInfo Address (GClass1044)
        /// </summary>
        public ulong Info { get; }
        /// <summary>
        /// Player name.
        /// </summary>
        public override string Name { get; set; }
        /// <summary>
        /// Group that the player belongs to.
        /// </summary>
        public override int NetworkGroupID { get; } = -1;
        /// <summary>
        /// Player's Faction.
        /// </summary>
        public override Enums.EPlayerSide PlayerSide { get; }
        /// <summary>
        /// Player is Human-Controlled.
        /// </summary>
        public override bool IsHuman { get; }
        /// <summary>
        /// MovementContext / StateContext
        /// </summary>
        public override ulong MovementContext { get; }
        /// <summary>
        /// EFT.PlayerBody
        /// </summary>
        public override ulong Body { get; }
        /// <summary>
        /// Inventory Controller field address.
        /// </summary>
        public override ulong InventoryControllerAddr { get; }
        /// <summary>
        /// Hands Controller field address.
        /// </summary>
        public override ulong HandsControllerAddr { get; }
        /// <summary>
        /// Corpse field address..
        /// </summary>
        public override ulong CorpseAddr { get; }
        /// <summary>
        /// Player Rotation Field Address (view angles).
        /// </summary>
        public override ulong RotationAddress { get; }
        /// <summary>
        /// Player's Skeleton Bones.
        /// </summary>
        public override Skeleton Skeleton
        {
            get
            {
                TryEnsureSkeleton();
                return _skeleton;
            }
        }
        private Skeleton _skeleton;
        private bool _skeletonFailed;
        public override int VoipId { get; }

        private static int ParseVoipId(ulong baseAddr)
        {
            if (!Memory.TryReadPtr(baseAddr + Offsets.Player.VoipID, out var strPtr) || strPtr == 0)
                return -1;
            if (!Memory.TryReadUnityString(strPtr, out var s) || string.IsNullOrWhiteSpace(s))
                return -1;
            return int.TryParse(s, out int id) ? id : -1;
        }
        internal ClientPlayer(ulong playerBase) : base(playerBase)
        {
            if (!Memory.TryReadPtr(this + Offsets.Player.Profile, out var profile))
                throw new InvalidOperationException("Player class not ready");
            Profile = profile;
            if (!Memory.TryReadPtr(this + Offsets.Player._characterController, out var charController))
                throw new InvalidOperationException("Player class not ready");
            CharacterController = charController;
            if (!Memory.TryReadPtr(Profile + Offsets.Profile.Info, out var info))
                throw new InvalidOperationException("Player class not ready");
            Info = info;
            if (!Memory.TryReadPtr(this + Offsets.Player.ProceduralWeaponAnimation, out var pwa))
                throw new InvalidOperationException("Player class not ready");
            PWA = pwa;
            if (!Memory.TryReadPtr(this + Offsets.Player._playerBody, out var body))
                throw new InvalidOperationException("Player class not ready");
            Body = body;
            InventoryControllerAddr = this + Offsets.Player._inventoryController;
            HandsControllerAddr = this + Offsets.Player._handsController;
            CorpseAddr = this + Offsets.Player.Corpse;
            VoipId = ParseVoipId(this);

            NetworkGroupID = GetGroupID();
            MovementContext = GetMovementContext();
            RotationAddress = ValidateRotationAddr(MovementContext + Offsets.MovementContext._rotation);

            /// Determine Player Type
            if (!Memory.TryReadValue<int>(Info + Offsets.PlayerInfo.Side, out var sideVal))
                throw new InvalidOperationException("Player class not ready");
            PlayerSide = (Enums.EPlayerSide)sideVal; // Usec,Bear,Scav,etc.
            if (!Enum.IsDefined(PlayerSide)) // Make sure PlayerSide is valid
                throw new Exception("Invalid Player Side/Faction!");
            if (this is LocalPlayer) // Handled in derived class
                return;

            if (!Memory.TryReadValue<int>(Info + Offsets.PlayerInfo.RegistrationDate, out var regDate))
                throw new InvalidOperationException("Player class not ready");
            bool isAI = regDate == 0;
            if (IsScav)
            {
                if (isAI)
                {
                    IsHuman = false;
                    Name = "AI";
                    Type = PlayerType.AIScav;
                }
                else
                {
                    IsHuman = true;

                    string nickname = null;
                    if (Memory.TryReadPtr(Info + Offsets.PlayerInfo.Nickname, out var nickPtr) && nickPtr != 0)
                        Memory.TryReadUnityString(nickPtr, out nickname);

                    Name = !string.IsNullOrWhiteSpace(nickname) ? nickname : "PScav";
                    Type = PlayerType.PScav;
                }
            }
            else if (IsPmc)
            {
                IsHuman = true;

                string nickname = null;
                if (Memory.TryReadPtr(Info + Offsets.PlayerInfo.Nickname, out var nickPtr) && nickPtr != 0)
                    Memory.TryReadUnityString(nickPtr, out nickname);

                Name = !string.IsNullOrWhiteSpace(nickname)
                    ? nickname
                    : (PlayerSide == EPlayerSide.Usec ? "USEC" : "BEAR");

                Type = (PlayerSide == EPlayerSide.Usec) ? PlayerType.USEC : PlayerType.BEAR;
            }
            else
                throw new NotImplementedException(nameof(PlayerSide));
        }


        private void TryEnsureSkeleton()
        {
            if (_skeleton != null || _skeletonFailed)
                return;

            try
            {
                _skeleton = new Skeleton(this, GetTransformInternalChain);
            }
            catch (Exception ex)
            {
                _skeletonFailed = true;
                Log.WriteLine($"[Skeleton] LocalPlayer not ready yet: {ex.Message}");
            }
        }
        public override void OnRealtimeLoop(ScatterReadIndex index)
        {
            _skeletonFailed = false; // allow retry
            base.OnRealtimeLoop(index);
        }
        /// <summary>
        /// Gets player's Group Number.
        /// </summary>
        private int GetGroupID()
        {
            if (!Memory.TryReadPtr(Info + Offsets.PlayerInfo.GroupId, out var grpIdPtr))
                return -1;
            if (!Memory.TryReadUnityString(grpIdPtr, out var grp) || grp is null)
                return -1;
            return _groups.GetGroup(grp);
        }

        /// <summary>
        /// Get Movement Context Instance.
        /// </summary>
        private ulong GetMovementContext()
        {
            if (!Memory.TryReadPtr(this + Offsets.Player.MovementContext, out var movementContext))
                throw new InvalidOperationException("Player class not ready");
            if (!Memory.TryReadPtr(movementContext + Offsets.MovementContext.Player, out var player, false))
                throw new InvalidOperationException("Player class not ready");
            if (player != this)
                throw new ArgumentOutOfRangeException(nameof(movementContext));
            return movementContext;
        }

        /// <summary>
        /// Get the Transform Internal Chain for this Player.
        /// </summary>
        /// <param name="bone">Bone to lookup.</param>
        /// <returns>Array of offsets for transform internal chain.</returns>
        public override uint[] GetTransformInternalChain(Bones bone) =>
            Offsets.Player.GetTransformChain(bone);
    }
}
