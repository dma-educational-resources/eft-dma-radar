#pragma warning disable IDE0130
#pragma warning disable CA2211

// ──────────────────────────────────────────────────────────────────────────────
// Arena SDK offsets — fallback values overwritten at runtime by the IL2CPP dumper.
// The dumper resolves offsets via reflection on typeof(Offsets).
// All values below are placeholder stubs; populate after first successful dump.
// ──────────────────────────────────────────────────────────────────────────────

namespace SDK
{
    public readonly partial struct Offsets
    {
        // ── Il2CppClass layout (stable across EFT builds) ──
        public readonly partial struct Il2CppClass
        {
            public const uint Name        = 0x10;
            public const uint Namespace   = 0x18;
            public const uint Parent      = 0x58;
            public const uint Fields      = 0x80;
            public const uint StaticFields = 0xB8;
            public const uint Methods     = 0x98;
            public const uint MethodCount = 0x120;
            public const uint FieldCount  = 0x124;
        }

        // ── TypeInfoTable RVA within GameAssembly.dll ──
        // Populated by TypeInfoTableResolver sig-scan at startup.
        public readonly partial struct Special
        {
            public static ulong TypeInfoTableRva = 0x0;
            public static uint GamePlayerOwner_TypeIndex = 0;
        }

        // ── GameWorld / LocalGameWorld ──────────────────────────────────────
        public readonly partial struct ClientLocalGameWorld
        {
            public static uint RegisteredPlayers = 0x1B8;
            public static uint MainPlayer        = 0x218;
            public static uint LocationId        = 0xD0;
        }

        // ── ObservedPlayerView (EFT.NextObservedPlayer.ObservedPlayerView) ──
        // Field offsets verified against C:\Temp\il2cpp_full_dump.txt [14996]
        public readonly partial struct ObservedPlayerView
        {
            public static uint ObservedPlayerController = 0x28;   // <ObservedPlayerController>k__BackingField
            public static uint Voice                    = 0x40;   // <Voice>k__BackingField
            public static uint Id                       = 0x7C;   // <Id>k__BackingField
            public static uint Side                     = 0x9C;   // <Side>k__BackingField (EPlayerSide valuetype)
            public static uint IsAI                     = 0xA8;   // <IsAI>k__BackingField
            public static uint ProfileId                = 0xB0;   // <ProfileId>k__BackingField
            public static uint NickName                 = 0xC0;   // <NickName>k__BackingField
            // AccountId (0xC8): field exists in EFT.NextObservedPlayer.ObservedPlayerView but Arena's
            // server never sends it to other clients — always null in practice; not read.
            public static uint PlayerBody               = 0xE0;   // <PlayerBody>k__BackingField
            public static uint _playerLookRaycastTransform = 0x110; // _playerLookRaycastTransform
        }

        // ── ObservedPlayerController (EFT.NextObservedPlayer.ObservedPlayerController) ──
        // Field offsets verified against dump [14856]
        public readonly partial struct ObservedPlayerController
        {
            public static uint MovementController = 0x110; // <MovementController>k__BackingField
            public static uint HealthController   = 0x120; // <HealthController>k__BackingField
        }

        // ── ObservedMovementController rotation ──────────────────────────────
        // Verified from live Arena dumps:
        // EFT.NextObservedPlayer.ObservedPlayerMovementController:
        //   0x10 <Model>k__BackingField (valuetype — NOT used for rotation in Arena)
        //   0xB0 <ObservedPlayerStateContext>k__BackingField (class ptr)
        // EFT.NextObservedPlayer.ObservedPlayerStateContext:
        //   0x20 <Rotation>k__BackingField (Vector2: yaw X, pitch Y)
        public readonly partial struct ObservedMovementController
        {
            public static uint StateContext = 0xB0; // ptr → ObservedPlayerStateContext
        }

        public readonly partial struct ObservedPlayerStateContext
        {
            public static uint Rotation = 0x20; // valuetype Vector2 (yaw X, pitch Y)
        }

        // ── GamePlayerOwner (IL2CPP singleton static class) ────────────────────
        public readonly partial struct GamePlayerOwner
        {
            public static uint _myPlayer = 0x8;
        }

        // ── Player / FirstPersonCamera ──────────────────────────────────────
        public readonly partial struct Player
        {
            public static uint GameWorld        = 0x640;  // EFT.Player [12002] → GameWorld ptr
            public static uint MovementContext  = 0x70;   // <MovementContext>k__BackingField
            public static uint _playerLookRaycastTransform = 0xA88; // EFT.Player._playerLookRaycastTransform
        }

        // ── MovementContext (EFT.MovementContext [12338]) ────────────────────
        public readonly partial struct MovementContext
        {
            public static uint _rotation = 0xD4; // valuetype Vector2 (yaw X, pitch Y)
        }

        public readonly partial struct PlayerBody
        {
            public static uint SkeletonRootJoint = 0x28;
        }

        // ── Unity TrsX (Transform) ──────────────────────────────────────────
        public readonly partial struct TrsX
        {
            public const uint Position = 0x90;
        }

        // ── GOM (GameObjectManager) entry ───────────────────────────────────
        public readonly partial struct GameObjectManager
        {
            public const uint LastTaggedNode      = 0x0;
            public const uint TaggedNodes         = 0x8;
            public const uint LastMainCameraTaggedNode = 0x38;
            public const uint MainCameraTaggedNodes   = 0x40;
        }

        public readonly partial struct TaggedObject
        {
            public const uint Object = 0x10;
            public const uint Next   = 0x28;
        }

        // ── Unity string ────────────────────────────────────────────────────
        public readonly partial struct UnityString
        {
            public const uint Length = 0x10;
            public const uint Data   = 0x14;
        }

        // ── AssemblyCSharp ──────────────────────────────────────────────────
        public readonly partial struct AssemblyCSharp
        {
            public static uint TypeStart = 0;
            public static uint TypeCount = 0;
        }
    }

    // ── Memory layout structs ────────────────────────────────────────────────
    public readonly struct Types
    {
        [StructLayout(LayoutKind.Explicit)]
        public readonly struct Vector3
        {
            [FieldOffset(0x00)] public readonly float X;
            [FieldOffset(0x04)] public readonly float Y;
            [FieldOffset(0x08)] public readonly float Z;

            public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
        }
    }
}
