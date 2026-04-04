/*  
 *  VmmSharpEx by Lone (Lone DMA)
 *  Copyright (C) 2025 AGPL-3.0
*/

using VmmSharpEx.Options;

namespace VmmSharpEx.Refresh;

/// <summary>
/// VMM Refresh Options.
/// </summary>
public enum RefreshOption : ulong
{
    /// <summary>
    /// refresh all caches
    /// </summary>
    All = VmmOption.REFRESH_ALL,

    /// <summary>
    /// refresh memory cache (excl. TLB) [fully]
    /// </summary>
    Memory = VmmOption.REFRESH_FREQ_MEM,

    /// <summary>
    /// refresh memory cache (excl. TLB) [partial 33%/call]
    /// </summary>
    MemoryPartial = VmmOption.REFRESH_FREQ_MEM_PARTIAL,

    /// <summary>
    /// refresh page table (TLB) cache [fully]
    /// </summary>
    Tlb = VmmOption.REFRESH_FREQ_TLB,

    /// <summary>
    /// refresh page table (TLB) cache [partial 33%/call]
    /// </summary>
    TlbPartial = VmmOption.REFRESH_FREQ_TLB_PARTIAL,

    /// <summary>
    /// refresh fast frequency - incl. partial process refresh
    /// </summary>
    Fast = VmmOption.REFRESH_FREQ_FAST,

    /// <summary>
    /// refresh medium frequency - incl. full process refresh
    /// </summary>
    Medium = VmmOption.REFRESH_FREQ_MEDIUM,

    /// <summary>
    /// refresh slow frequency.
    /// </summary>
    Slow = VmmOption.REFRESH_FREQ_SLOW,

    /// <summary>
    /// Refresh only heap allocations. (W)
    /// </summary>
    SPECIFIC_HEAP_ALLOC = VmmOption.REFRESH_SPECIFIC_HEAP_ALLOC,

    /// <summary>
    /// Refresh only kernel objects. (W)
    /// </summary>
    SPECIFIC_KOBJECT = VmmOption.REFRESH_SPECIFIC_KOBJECT,

    /// <summary>
    /// Refresh only network connections. (W)
    /// </summary>
    SPECIFIC_NET = VmmOption.REFRESH_SPECIFIC_NET,

    /// <summary>
    /// Refresh only PFN database. (W)
    /// </summary>
    SPECIFIC_PFN = VmmOption.REFRESH_SPECIFIC_PFN,

    /// <summary>
    /// Refresh only physical memory map. (W)
    /// </summary>
    SPECIFIC_PHYSMEMMAP = VmmOption.REFRESH_SPECIFIC_PHYSMEMMAP,

    /// <summary>
    /// Refresh only kernel pool. (W)
    /// </summary>
    SPECIFIC_POOL = VmmOption.REFRESH_SPECIFIC_POOL,

    /// <summary>
    /// Refresh only registry. (W)
    /// </summary>
    SPECIFIC_REGISTRY = VmmOption.REFRESH_SPECIFIC_REGISTRY,

    /// <summary>
    /// Refresh only services. (W)
    /// </summary>
    SPECIFIC_SERVICES = VmmOption.REFRESH_SPECIFIC_SERVICES,

    /// <summary>
    /// Refresh only thread callstacks. (W)
    /// </summary>
    SPECIFIC_THREADCS = VmmOption.REFRESH_SPECIFIC_THREADCS,

    /// <summary>
    /// Refresh only users. (W)
    /// </summary>
    SPECIFIC_USER = VmmOption.REFRESH_SPECIFIC_USER,

    /// <summary>
    /// Refresh only virtual machines. (W)
    /// </summary>
    SPECIFIC_VM = VmmOption.REFRESH_SPECIFIC_VM,

    /// <summary>
    /// Refresh only the specified process. (W)
    /// The low DWORD contains the process ID (PID).
    /// </summary>
    SPECIFIC_PROCESS = VmmOption.REFRESH_SPECIFIC_PROCESS
}