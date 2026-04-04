using System.Runtime.CompilerServices;

namespace VmmSharpEx.Extensions
{
    /// <summary>
    /// Contains Vmm/memory-related utility methods.
    /// </summary>
    public static class VmmUtilities
    {
        /// <summary>
        /// Checks if a Virtual Address is valid.
        /// </summary>
        /// <param name="va">Virtual Address to validate.</param>
        /// <returns>True if valid, otherwise False.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidVA(ulong va)
        {
            ulong high = va >> 47;
            return va >= 0x10000 && (high == 0 || high == 0x1FFFF);
        }

        /// <summary>
        /// Checks if a Virtual Address is a valid User-Mode Virtual Address.
        /// </summary>
        /// <param name="va">Virtual Address to validate.</param>
        /// <returns>True if valid, otherwise False.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidUserVA(ulong va)
        {
            ulong high = va >> 47;
            return va >= 0x10000 && high == 0;
        }

        /// <summary>
        /// Checks if a Virtual Address is a valid Kernel-Mode Virtual Address.
        /// </summary>
        /// <param name="va">Virtual Address to validate.</param>
        /// <returns>True if valid, otherwise False.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsValidKernelVA(ulong va)
        {
            ulong high = va >> 47;
            return va >= 0xFFFF800000000000 && high == 0x1FFFF;
        }

        /// <summary>
        /// The PAGE_ALIGN macro returns a page-aligned virtual address for a given virtual address.
        /// </summary>
        /// <remarks>
        /// <see href="https://learn.microsoft.com/windows-hardware/drivers/ddi/wdm/nf-wdm-page_align"/>
        /// </remarks>
        /// <param name="va">Virtual address.</param>
        /// <returns>Page-aligned virtual address.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong PAGE_ALIGN(ulong va) => va & ~(0x1000ul - 1);

        /// <summary>
        /// The BYTE_OFFSET macro takes a virtual address and returns the byte offset of that address within the page.
        /// </summary>
        /// <remarks>
        /// <see href="https://learn.microsoft.com/windows-hardware/drivers/ddi/wdm/nf-wdm-byte_offset"/>
        /// </remarks>
        /// <param name="va">virtual address.</param>
        /// <returns>Offset portion of the virtual address within the page.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint BYTE_OFFSET(ulong va) => (uint)(va & (0x1000ul - 1));

        /// <summary>
        /// The ADDRESS_AND_SIZE_TO_SPAN_PAGES macro returns the number of pages that a virtual range spans.
        /// The virtual range is defined by a virtual address and the size in bytes of a transfer request.
        /// </summary>
        /// <remarks>
        /// <see href="https://learn.microsoft.com/windows-hardware/drivers/ddi/wdm/nf-wdm-address_and_size_to_span_pages"/>
        /// </remarks>
        /// <param name="va">Virtual address that is the base of the range.</param>
        /// <param name="size">Specifies the size in bytes.</param>
        /// <returns>Returns the number of pages spanned by the virtual range starting at Va.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong ADDRESS_AND_SIZE_TO_SPAN_PAGES(ulong va, ulong size) =>
            (BYTE_OFFSET(va) + size + (0x1000ul - 1)) >> 12;

        /// <summary>
        /// Returns a length aligned to the specified alignment.
        /// Always rounds up.
        /// </summary>
        /// <param name="length">The length to align.</param>
        /// <param name="alignment">The alignment boundary (must be a power of 2).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint AlignLength(uint length, uint alignment = 8) => (length + alignment - 1) & ~(alignment - 1);

        /// <summary>
        /// Returns an address aligned to the specified alignment.
        /// Always the current/next aligned address.
        /// </summary>
        /// <param name="address">The address to align.</param>
        /// <param name="alignment">The alignment boundary (must be a power of 2).</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong AlignAddress(ulong address, ulong alignment = 8) => (address + alignment - 1) & ~(alignment - 1);
    }
}
