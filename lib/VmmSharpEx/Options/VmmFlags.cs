/*  
 *  VmmSharpEx by Lone (Lone DMA)
 *  Copyright (C) 2025 AGPL-3.0
*/

namespace VmmSharpEx.Options
{

    /// <summary>
    /// Flags used to control memory read/write behavior in the VMM API.
    /// </summary>
    [Flags]
    public enum VmmFlags : uint
    {
        /// <summary>
        /// No Flags Set.
        /// This is the default value and indicates no special options are applied.
        /// </summary>
        NONE = 0,

        /// <summary>
        /// Do not use the data cache (force reading directly from the memory acquisition device).
        /// </summary>
        NOCACHE = 0x0001,

        /// <summary>
        /// Zero pad failed physical memory reads and report success if the read is within
        /// the range of physical memory.
        /// </summary>
        ZEROPAD_ON_FAIL = 0x0002,

        /// <summary>
        /// Force use of cache. Fail on non-cached pages.
        /// Only valid for reads. Cannot be combined with <see cref="NOCACHE"/> or <see cref="ZEROPAD_ON_FAIL"/>.
        /// </summary>
        FORCECACHE_READ = 0x0008,

        /// <summary>
        /// Do not attempt to retrieve memory from paged-out memory sources
        /// (pagefile/compressed), even if possible.
        /// </summary>
        NOPAGING = 0x0010,

        /// <summary>
        /// Do not attempt to retrieve memory from paged-out memory if it would require
        /// additional I/O operations, even if possible.
        /// </summary>
        NOPAGING_IO = 0x0020,

        /// <summary>
        /// Do not write back to the data cache after a successful read from the memory acquisition device.
        /// </summary>
        NOCACHEPUT = 0x0100,

        /// <summary>
        /// Only fetch from the most recent active cache region when reading.
        /// </summary>
        CACHE_RECENT_ONLY = 0x0200,

        /// <summary>
        /// Disable predictive read-ahead when reading memory.
        /// </summary>
        NO_PREDICTIVE_READ = 0x0400,

        /// <summary>
        /// Force cache read disable. Only recommended for local files,
        /// improves forensic artifact ordering.
        /// </summary>
        FORCECACHE_READ_DISABLE = 0x0800,

        /// <summary>
        /// do not zero out the memory buffer when preparing a scatter read.
        /// </summary>
        SCATTER_PREPAREEX_NOMEMZERO = 0x1000,

        /// <summary>
        /// do not call user-set memory callback functions when reading memory (even if active).
        /// </summary>
        NOMEMCALLBACK = 0x2000,

        /// <summary>
        /// force page-sized reads when using scatter functionality.
        /// </summary>
        SCATTER_FORCE_PAGEREAD = 0x4000
    }

}
