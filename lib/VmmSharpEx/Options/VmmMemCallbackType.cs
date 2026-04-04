/*  
 *  VmmSharpEx by Lone (Lone DMA)
 *  Copyright (C) 2025 AGPL-3.0
*/

namespace VmmSharpEx.Options
{
    /// <summary>
    /// Callback types for VMM memory callbacks.
    /// </summary>
    public enum VmmMemCallbackType : uint
    {
        /// <summary>
        /// Before physical read.
        /// </summary>
        READ_PHYSICAL_PRE = 1,
        /// <summary>
        /// After physical read.
        /// </summary>
        READ_PHYSICAL_POST = 2,
        /// <summary>
        /// Before physical write.
        /// </summary>
        WRITE_PHYSICAL_PRE = 3,
        /// <summary>
        /// Before virtual read.
        /// </summary>
        READ_VIRTUAL_PRE = 4,
        /// <summary>
        /// After virtual read.
        /// </summary>
        READ_VIRTUAL_POST = 5,
        /// <summary>
        /// Before virtual write.
        /// </summary>
        WRITE_VIRTUAL_PRE = 6,
    }
}
