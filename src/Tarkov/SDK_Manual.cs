using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;

namespace SDK
{
    public readonly partial struct Offsets
    {
        public readonly partial struct Player
        {
            /// <summary>
            /// Returns a Transform Address Chain for specified Bone Index.
            /// </summary>
            /// <param name="index">Bone index to return.</param>
            /// <returns>Pointer chain.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static uint[] GetTransformChain(Bones index)
            {
                return new uint[] { _playerBody, Offsets.PlayerBody.SkeletonRootJoint, Offsets.DizSkinningSkeleton._values, MemList<byte>.ArrOffset, MemList<byte>.ArrStartOffset + (uint)index * 0x8, 0x10 };
            }
        }

        public readonly struct BodyAnimator // \uE670 : IAnimator, IAnimatorNotificator
        {
            public const uint UnityAnimator = 0x10; // Type: UnityEngine.Animator
        }

        public readonly partial struct FirearmController
        {
            public static readonly uint[] To_FirePortTransformInternal = new uint[] { Fireport, 0x10, 0x10 };
            public static readonly uint[] To_FirePortVertices = To_FirePortTransformInternal.Concat(new uint[] { UnityOffsets.TransformInternal.TransformAccess, UnityOffsets.TransformAccess.Vertices }).ToArray();
        }

        public readonly partial struct ObservedPlayerView
        {
            /// <summary>
            /// Returns a Transform Address Chain for specified Bone Index.
            /// </summary>
            /// <param name="index">Bone index to return.</param>
            /// <returns>Pointer chain.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static uint[] GetTransformChain(Bones index)
            {
                return new uint[] { PlayerBody, Offsets.PlayerBody.SkeletonRootJoint, Offsets.DizSkinningSkeleton._values, MemList<byte>.ArrOffset, MemList<byte>.ArrStartOffset + (uint)index * 0x8, 0x10 };
            }
        }

        public readonly partial struct HealthSystem
        {
            public const uint Energy = 0x38; // EFT.HealthSystem.HealthValue
            public const uint Hydration = 0x40; // EFT.HealthSystem.HealthValue
        }

        public readonly partial struct HealthValue
        {
            public const uint Value = 0x10; // Value : EFT.HealthSystem.ValueStruct
        }
    }

    public readonly partial struct Enums
    {
        public enum EPlayerSide : int
        {
            [Description("USEC")]
            Usec = 0x1,
            [Description("BEAR")]
            Bear = 0x2,
            [Description("SCAV")]
            Savage = 0x4
        }

        [Flags]
        public enum ETagStatus : int
        {
            // Token: 0x04002525 RID: 9509
            [Description("Unaware")]
            Unaware = 1,
            // Token: 0x04002526 RID: 9510
            [Description("Aware")]
            Aware = 2,
            // Token: 0x04002527 RID: 9511
            [Description("Combat")]
            Combat = 4,
            // Token: 0x04002528 RID: 9512
            [Description("Solo")]
            Solo = 8,
            // Token: 0x04002529 RID: 9513
            [Description("Coop")]
            Coop = 16,
            // Token: 0x0400252A RID: 9514
            [Description("Bear")]
            Bear = 32,
            // Token: 0x0400252B RID: 9515
            [Description("Usec")]
            Usec = 64,
            // Token: 0x0400252C RID: 9516
            [Description("Scav")]
            Scav = 128,
            // Token: 0x0400252D RID: 9517
            [Description("TargetSolo")]
            TargetSolo = 256,
            // Token: 0x0400252E RID: 9518
            [Description("TargetMultiple")]
            TargetMultiple = 512,
            // Token: 0x0400252F RID: 9519
            [Description("Healthy")]
            Healthy = 1024,
            // Token: 0x04002530 RID: 9520
            [Description("Injured")]
            Injured = 2048,
            // Token: 0x04002531 RID: 9521
            [Description("Badly Injured")]
            BadlyInjured = 4096,
            // Token: 0x04002532 RID: 9522
            [Description("Dying")]
            Dying = 8192,
            // Token: 0x04002533 RID: 9523
            [Description("Birdeye")]
            Birdeye = 16384,
            // Token: 0x04002534 RID: 9524
            [Description("Knight")]
            Knight = 32768,
            // Token: 0x04002535 RID: 9525
            [Description("BigPipe")]
            BigPipe = 65536
        }

        public enum InventoryBlurDimensions
        {
            _128 = 128,
            _256 = 256,
            _512 = 512,
            _1024 = 1024,
            _2048 = 2048,
        }
    }

    public readonly struct Types
    {
        /// <summary>
        /// EFT.MongoID Struct
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Pack = 8)]
        public readonly struct MongoID
        {
            [FieldOffset(0x0)]
            private readonly uint _timeStamp;
            [FieldOffset(0x8)]
            private readonly ulong _counter;
            [FieldOffset(0x10)]
            private readonly ulong _stringID;

            public readonly ulong StringID => _stringID;
        }

        /// <summary>
        /// EFT.HealthSystem.Value Struct
        /// </summary>
        [StructLayout(LayoutKind.Explicit, Pack = 8)]
        public readonly struct HealthSystem
        {
            [FieldOffset(0x0)]
            private readonly float _current;
            [FieldOffset(0x04)]
            private readonly float _maximum;
            [FieldOffset(0x08)]
            private readonly float _minimum;
            [FieldOffset(0x0C)]
            private readonly float _overDamageReceivedMultiplier;
            [FieldOffset(0x10)]
            private readonly float _environmentDamageMultiplier;

            public readonly float Current => _current;
        }

        /// <summary>
        /// Check _bodyRenderers type to see if struct changed.
        /// </summary>
        [StructLayout(LayoutKind.Explicit)]
        public readonly struct BodyRendererContainer
        {
            [FieldOffset(0x0)]
            private readonly int DecalType;
            [FieldOffset(0x8)]
            public readonly ulong Renderers;
        }
    }

    public static class SDKExtensions
    {
        /// <summary>
        /// Checks if the Player State is a valid enum.
        /// </summary>
        /// <param name="state"></param>
        /// <returns>True if valid, otherwise False.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidState(this Enums.EPlayerState state)
        {
            return state is >= Enums.EPlayerState.None and <= Enums.EPlayerState.IdleWeaponMounting;
        }
    }
}