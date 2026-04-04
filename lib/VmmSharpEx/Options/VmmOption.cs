/*  
 *  VmmSharpEx by Lone (Lone DMA)
 *  Copyright (C) 2025 AGPL-3.0
*/

namespace VmmSharpEx.Options
{
    /// <summary>
    /// Configuration options for VMM.  
    /// Values are used to query or set various runtime and system parameters.
    /// </summary>
    public enum VmmOption : ulong
    {
        /// <summary>
        /// Enable or disable core printf output. (RW)
        /// </summary>
        CORE_PRINTF_ENABLE = 0x4000000100000000,

        /// <summary>
        /// Enable verbose output. (RW)
        /// </summary>
        CORE_VERBOSE = 0x4000000200000000,

        /// <summary>
        /// Enable extra verbose output. (RW)
        /// </summary>
        CORE_VERBOSE_EXTRA = 0x4000000300000000,

        /// <summary>
        /// Enable verbose output for TLP (extra trace-level). (RW)
        /// </summary>
        CORE_VERBOSE_EXTRA_TLP = 0x4000000400000000,

        /// <summary>
        /// Maximum supported native address. (R)
        /// </summary>
        CORE_MAX_NATIVE_ADDRESS = 0x4000000800000000,

        /// <summary>
        /// Underlying LeechCore handle (do not close). (R)
        /// </summary>
        CORE_LEECHCORE_HANDLE = 0x4000001000000000,

        /// <summary>
        /// Thread-safe duplicate VMM instance handle,
        /// used with startup option "-create-from-vmmid". (R)
        /// </summary>
        CORE_ID = 0x4000002000000000,

        /// <summary>
        /// System identifier. (R)
        /// </summary>
        CORE_SYSTEM = 0x2000000100000000,

        /// <summary>
        /// Memory model in use. (R)
        /// </summary>
        CORE_MEMORYMODEL = 0x2000000200000000,

        /// <summary>
        /// Whether refresh is enabled (1/0). (R)
        /// </summary>
        CONFIG_IS_REFRESH_ENABLED = 0x2000000300000000,

        /// <summary>
        /// Base tick period in milliseconds. (RW)
        /// </summary>
        CONFIG_TICK_PERIOD = 0x2000000400000000,

        /// <summary>
        /// Memory cache validity period (in ticks). (RW)
        /// </summary>
        CONFIG_READCACHE_TICKS = 0x2000000500000000,

        /// <summary>
        /// Page table (TLB) cache validity period (in ticks). (RW)
        /// </summary>
        CONFIG_TLBCACHE_TICKS = 0x2000000600000000,

        /// <summary>
        /// Process refresh (partial) period (in ticks). (RW)
        /// </summary>
        CONFIG_PROCCACHE_TICKS_PARTIAL = 0x2000000700000000,

        /// <summary>
        /// Process refresh (full) period (in ticks). (RW)
        /// </summary>
        CONFIG_PROCCACHE_TICKS_TOTAL = 0x2000000800000000,

        /// <summary>
        /// Version major. (R)
        /// </summary>
        CONFIG_VERSION_MAJOR = 0x2000000900000000,

        /// <summary>
        /// Version minor. (R)
        /// </summary>
        CONFIG_VERSION_MINOR = 0x2000000A00000000,

        /// <summary>
        /// Version revision. (R)
        /// </summary>
        CONFIG_VERSION_REVISION = 0x2000000B00000000,

        /// <summary>
        /// Enable function call statistics (.status/statistics_fncall file). (RW)
        /// </summary>
        CONFIG_STATISTICS_FUNCTIONCALL = 0x2000000C00000000,

        /// <summary>
        /// Whether paging is enabled (1/0). (RW)
        /// </summary>
        CONFIG_IS_PAGING_ENABLED = 0x2000000D00000000,

        /// <summary>
        /// Debug mode. (W)
        /// </summary>
        CONFIG_DEBUG = 0x2000000E00000000,

        /// <summary>
        /// YARA rules configuration. (R)
        /// </summary>
        CONFIG_YARA_RULES = 0x2000000F00000000,

        /// <summary>
        /// Windows version major. (R)
        /// </summary>
        WIN_VERSION_MAJOR = 0x2000010100000000,

        /// <summary>
        /// Windows version minor. (R)
        /// </summary>
        WIN_VERSION_MINOR = 0x2000010200000000,

        /// <summary>
        /// Windows version build number. (R)
        /// </summary>
        WIN_VERSION_BUILD = 0x2000010300000000,

        /// <summary>
        /// Windows system unique identifier. (R)
        /// </summary>
        WIN_SYSTEM_UNIQUE_ID = 0x2000010400000000,

        /// <summary>
        /// Forensic mode type [0-4]. (RW)
        /// </summary>
        FORENSIC_MODE = 0x2000020100000000,

        // ---- Refresh Options ----

        /// <summary>
        /// Refresh all caches. (W)
        /// </summary>
        REFRESH_ALL = 0x2001ffff00000000,

        /// <summary>
        /// Refresh memory cache (excluding TLB) fully. (W)
        /// </summary>
        REFRESH_FREQ_MEM = 0x2001100000000000,

        /// <summary>
        /// Refresh memory cache (excluding TLB) partially (33% per call). (W)
        /// </summary>
        REFRESH_FREQ_MEM_PARTIAL = 0x2001000200000000,

        /// <summary>
        /// Refresh page table (TLB) cache fully. (W)
        /// </summary>
        REFRESH_FREQ_TLB = 0x2001080000000000,

        /// <summary>
        /// Refresh page table (TLB) cache partially (33% per call). (W)
        /// </summary>
        REFRESH_FREQ_TLB_PARTIAL = 0x2001000400000000,

        /// <summary>
        /// Refresh at fast frequency, includes partial process refresh. (W)
        /// </summary>
        REFRESH_FREQ_FAST = 0x2001040000000000,

        /// <summary>
        /// Refresh at medium frequency, includes full process refresh. (W)
        /// </summary>
        REFRESH_FREQ_MEDIUM = 0x2001000100000000,

        /// <summary>
        /// Refresh at slow frequency. (W)
        /// </summary>
        REFRESH_FREQ_SLOW = 0x2001001000000000,

        /// <summary>
        /// Refresh only heap allocations. (W)
        /// </summary>
        REFRESH_SPECIFIC_HEAP_ALLOC = 0x2003000100000000,

        /// <summary>
        /// Refresh only kernel objects. (W)
        /// </summary>
        REFRESH_SPECIFIC_KOBJECT = 0x2003000200000000,

        /// <summary>
        /// Refresh only network connections. (W)
        /// </summary>
        REFRESH_SPECIFIC_NET = 0x2003000300000000,

        /// <summary>
        /// Refresh only PFN database. (W)
        /// </summary>
        REFRESH_SPECIFIC_PFN = 0x2003000400000000,

        /// <summary>
        /// Refresh only physical memory map. (W)
        /// </summary>
        REFRESH_SPECIFIC_PHYSMEMMAP = 0x2003000500000000,

        /// <summary>
        /// Refresh only kernel pool. (W)
        /// </summary>
        REFRESH_SPECIFIC_POOL = 0x2003000600000000,

        /// <summary>
        /// Refresh only registry. (W)
        /// </summary>
        REFRESH_SPECIFIC_REGISTRY = 0x2003000700000000,

        /// <summary>
        /// Refresh only services. (W)
        /// </summary>
        REFRESH_SPECIFIC_SERVICES = 0x2003000800000000,

        /// <summary>
        /// Refresh only thread callstacks. (W)
        /// </summary>
        REFRESH_SPECIFIC_THREADCS = 0x2003000900000000,

        /// <summary>
        /// Refresh only users. (W)
        /// </summary>
        REFRESH_SPECIFIC_USER = 0x2003000A00000000,

        /// <summary>
        /// Refresh only virtual machines. (W)
        /// </summary>
        REFRESH_SPECIFIC_VM = 0x2003000B00000000,

        /// <summary>
        /// Refresh only the specified process. (W)
        /// The low DWORD contains the process ID (PID).
        /// </summary>
        REFRESH_SPECIFIC_PROCESS = 0x2002000300000000,

        // ---- Process Options ----

        /// <summary>
        /// Force set process Directory Table Base (DTB). (W)  
        /// [LO-DWORD contains process PID]
        /// </summary>
        PROCESS_DTB = 0x2002000100000000,

        /// <summary>
        /// Force set process DTB in fast, low-integrity mode (fewer checks).  
        /// Use at your own risk. (W)  
        /// [LO-DWORD contains process PID]
        /// </summary>
        PROCESS_DTB_FAST_LOWINTEGRITY = 0x2002000200000000
    }

}
