/*  
 *  VmmSharpEx by Lone (Lone DMA)
 *  Copyright (C) 2025 AGPL-3.0
*/

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VmmSharpEx.Extensions;

namespace VmmSharpEx
{
    /// <summary>
    /// Represents a pointer in the target x64 Windows System.
    /// Can be implicitly casted to/from <see cref="ulong"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8, Size = sizeof(ulong))]
    public readonly struct VmmPointer
    {
        public static implicit operator VmmPointer(ulong uint64) => new(uint64);
        public static implicit operator ulong(VmmPointer ptr) => ptr.Value;
        public readonly ulong Value;

        private VmmPointer(ulong value)
        {
            Value = value;
        }

        /// <summary>
        /// True if the pointer is a valid virtual address, otherwise False.
        /// </summary>
        public readonly bool IsValidVA
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Value.IsValidVA();
        }

        /// <summary>
        /// True if the pointer is a valid usermode virtual address, otherwise False.
        /// </summary>
        public readonly bool IsValidUserVA
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Value.IsValidUserVA();
        }

        /// <summary>
        /// True if the pointer is a valid kernel virtual address, otherwise False.
        /// </summary>
        public readonly bool IsValidKernelVA
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Value.IsValidKernelVA();
        }

        /// <summary>
        /// Throws a <see cref="VmmException"/> if the pointer is not a valid virtual address.
        /// </summary>
        /// <exception cref="VmmException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void ThrowIfInvalidVA() => Value.ThrowIfInvalidVA();

        /// <summary>
        /// Throws a <see cref="VmmException"/> if the pointer is not a valid usermode virtual address.
        /// </summary>
        /// <exception cref="VmmException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void ThrowIfInvalidUserVA() => Value.ThrowIfInvalidUserVA();

        /// <summary>
        /// Throws a <see cref="VmmException"/> if the pointer is not a valid kernel virtual address.
        /// </summary>
        /// <exception cref="VmmException"></exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void ThrowIfInvalidKernelVA() => Value.ThrowIfInvalidKernelVA();
    }
}
