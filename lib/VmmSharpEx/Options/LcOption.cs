/*  
 *  VmmSharpEx by Lone (Lone DMA)
 *  Copyright (C) 2025 AGPL-3.0
*/

namespace VmmSharpEx.Options
{
    /// <summary>
    /// Options and commands for configuring and controlling LeechCore (LC).  
    /// Values are used for querying, setting, or invoking various LC features.
    /// </summary>
    public enum LcOption : ulong
    {
        // ---- Printf / Verbosity ----

        /// <summary>
        /// Enable printf logging.
        /// </summary>
        CONFIG_PRINTF_ENABLED = 0x01,

        /// <summary>
        /// Verbose printf logging.
        /// </summary>
        CONFIG_PRINTF_V = 0x02,

        /// <summary>
        /// Extra verbose printf logging.
        /// </summary>
        CONFIG_PRINTF_VV = 0x04,

        /// <summary>
        /// Ultra-verbose printf logging.
        /// </summary>
        CONFIG_PRINTF_VVV = 0x08,

        // ---- Core Options ----

        /// <summary>
        /// Enable/disable core printf output. (RW)
        /// </summary>
        CORE_PRINTF_ENABLE = 0x4000000100000000,

        /// <summary>
        /// Enable verbose mode. (RW)
        /// </summary>
        CORE_VERBOSE = 0x4000000200000000,

        /// <summary>
        /// Enable extra verbose mode. (RW)
        /// </summary>
        CORE_VERBOSE_EXTRA = 0x4000000300000000,

        /// <summary>
        /// Enable TLP-specific verbose output. (RW)
        /// </summary>
        CORE_VERBOSE_EXTRA_TLP = 0x4000000400000000,

        /// <summary>
        /// Core version major. (R)
        /// </summary>
        CORE_VERSION_MAJOR = 0x4000000500000000,

        /// <summary>
        /// Core version minor. (R)
        /// </summary>
        CORE_VERSION_MINOR = 0x4000000600000000,

        /// <summary>
        /// Core version revision. (R)
        /// </summary>
        CORE_VERSION_REVISION = 0x4000000700000000,

        /// <summary>
        /// Maximum supported physical address. (R)
        /// </summary>
        CORE_ADDR_MAX = 0x1000000800000000,

        /// <summary>
        /// Call count statistics. (R) [lo-dword: LC_STATISTICS_ID_*]
        /// </summary>
        CORE_STATISTICS_CALL_COUNT = 0x4000000900000000,

        /// <summary>
        /// Call time statistics. (R) [lo-dword: LC_STATISTICS_ID_*]
        /// </summary>
        CORE_STATISTICS_CALL_TIME = 0x4000000a00000000,

        /// <summary>
        /// Core is volatile. (R)
        /// </summary>
        CORE_VOLATILE = 0x1000000b00000000,

        /// <summary>
        /// Core is read-only. (R)
        /// </summary>
        CORE_READONLY = 0x1000000c00000000,

        // ---- Memory Info ----

        /// <summary>
        /// Memory info validity flag. (R)
        /// </summary>
        MEMORYINFO_VALID = 0x0200000100000000,

        /// <summary>
        /// 32-bit memory model flag. (R)
        /// </summary>
        MEMORYINFO_FLAG_32BIT = 0x0200000300000000,

        /// <summary>
        /// PAE memory model flag. (R)
        /// </summary>
        MEMORYINFO_FLAG_PAE = 0x0200000400000000,

        /// <summary>
        /// Memory architecture type (LC_ARCH_TP). (R)
        /// </summary>
        MEMORYINFO_ARCH = 0x0200001200000000,

        /// <summary>
        /// OS version minor. (R)
        /// </summary>
        MEMORYINFO_OS_VERSION_MINOR = 0x0200000500000000,

        /// <summary>
        /// OS version major. (R)
        /// </summary>
        MEMORYINFO_OS_VERSION_MAJOR = 0x0200000600000000,

        /// <summary>
        /// OS Directory Table Base (DTB). (R)
        /// </summary>
        MEMORYINFO_OS_DTB = 0x0200000700000000,

        /// <summary>
        /// OS PFN (Page Frame Number). (R)
        /// </summary>
        MEMORYINFO_OS_PFN = 0x0200000800000000,

        /// <summary>
        /// OS PsLoadedModuleList address. (R)
        /// </summary>
        MEMORYINFO_OS_PsLoadedModuleList = 0x0200000900000000,

        /// <summary>
        /// OS PsActiveProcessHead address. (R)
        /// </summary>
        MEMORYINFO_OS_PsActiveProcessHead = 0x0200000a00000000,

        /// <summary>
        /// OS machine image type. (R)
        /// </summary>
        MEMORYINFO_OS_MACHINE_IMAGE_TP = 0x0200000b00000000,

        /// <summary>
        /// Number of processors. (R)
        /// </summary>
        MEMORYINFO_OS_NUM_PROCESSORS = 0x0200000c00000000,

        /// <summary>
        /// System time. (R)
        /// </summary>
        MEMORYINFO_OS_SYSTEMTIME = 0x0200000d00000000,

        /// <summary>
        /// System uptime. (R)
        /// </summary>
        MEMORYINFO_OS_UPTIME = 0x0200000e00000000,

        /// <summary>
        /// OS kernel base address. (R)
        /// </summary>
        MEMORYINFO_OS_KERNELBASE = 0x0200000f00000000,

        /// <summary>
        /// OS kernel hint. (R)
        /// </summary>
        MEMORYINFO_OS_KERNELHINT = 0x0200001000000000,

        /// <summary>
        /// OS KdDebuggerDataBlock address. (R)
        /// </summary>
        MEMORYINFO_OS_KdDebuggerDataBlock = 0x0200001100000000,

        // ---- FPGA Options ----

        /// <summary>
        /// Maximum number of probe pages. (RW)
        /// </summary>
        FPGA_PROBE_MAXPAGES = 0x0300000100000000,

        /// <summary>
        /// Maximum RX size. (RW)
        /// </summary>
        FPGA_MAX_SIZE_RX = 0x0300000300000000,

        /// <summary>
        /// Maximum TX size. (RW)
        /// </summary>
        FPGA_MAX_SIZE_TX = 0x0300000400000000,

        /// <summary>
        /// Probe read delay (µs). (RW)
        /// </summary>
        FPGA_DELAY_PROBE_READ = 0x0300000500000000,

        /// <summary>
        /// Probe write delay (µs). (RW)
        /// </summary>
        FPGA_DELAY_PROBE_WRITE = 0x0300000600000000,

        /// <summary>
        /// Write delay (µs). (RW)
        /// </summary>
        FPGA_DELAY_WRITE = 0x0300000700000000,

        /// <summary>
        /// Read delay (µs). (RW)
        /// </summary>
        FPGA_DELAY_READ = 0x0300000800000000,

        /// <summary>
        /// Retry on error flag. (RW)
        /// </summary>
        FPGA_RETRY_ON_ERROR = 0x0300000900000000,

        /// <summary>
        /// PCIe device ID (bus:dev:fn). (RW)
        /// Example: 04:00.0 = 0x0400
        /// </summary>
        FPGA_DEVICE_ID = 0x0300008000000000,

        /// <summary>
        /// FPGA ID. (R)
        /// </summary>
        FPGA_FPGA_ID = 0x0300008100000000,

        /// <summary>
        /// FPGA version major. (R)
        /// </summary>
        FPGA_VERSION_MAJOR = 0x0300008200000000,

        /// <summary>
        /// FPGA version minor. (R)
        /// </summary>
        FPGA_VERSION_MINOR = 0x0300008300000000,

        /// <summary>
        /// Use tiny 128-byte/TLP read algorithm (1/0). (RW)
        /// </summary>
        FPGA_ALGO_TINY = 0x0300008400000000,

        /// <summary>
        /// Use synchronous (legacy) read algorithm (1/0). (RW)
        /// </summary>
        FPGA_ALGO_SYNCHRONOUS = 0x0300008500000000,

        /// <summary>
        /// FPGA config space (Xilinx).  
        /// [lo-dword: register address in bytes]  
        /// [bytes: 0-3 data, 4-7 byte_enable; top bit = cfg_mgmt_wr_rw1c_as_rw] (RW)
        /// </summary>
        FPGA_CFGSPACE_XILINX = 0x0300008600000000,

        /// <summary>
        /// Call TLP read callback with additional string info in szInfo (1/0). (RW)
        /// </summary>
        FPGA_TLP_READ_CB_WITHINFO = 0x0300009000000000,

        /// <summary>
        /// Filter memory read completions when invoking TLP read callback (1/0). (RW)
        /// </summary>
        FPGA_TLP_READ_CB_FILTERCPL = 0x0300009100000000,
    }
}
