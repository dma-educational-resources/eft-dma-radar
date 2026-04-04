/*  
 *  VmmSharpEx by Lone (Lone DMA)
 *  Copyright (C) 2025 AGPL-3.0
*/

namespace VmmSharpEx.Options
{
    /// <summary>
    /// Pool map flags returned by VMMDLL.  
    /// Used to filter pool allocations.
    /// </summary>
    [Flags]
    public enum VmmPoolMapFlags : uint
    {
        /// <summary>
        /// Include all pools.
        /// </summary>
        ALL = 0,

        /// <summary>
        /// Include only big pool allocations.
        /// </summary>
        BIG = 1
    }
}
