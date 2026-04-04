/*  
 *  VmmSharpEx by Lone (Lone DMA)
 *  Copyright (C) 2025 AGPL-3.0
*/

namespace VmmSharpEx.Options
{
    /// <summary>
    /// Commands for interacting with LeechCore (LC).  
    /// These are used to query, set, or execute actions through the LC API.
    /// </summary>
    public enum LcCmd : ulong
    {
        // ---- FPGA Commands ----

        /// <summary>
        /// Read PCIe config space. (R)
        /// </summary>
        FPGA_PCIECFGSPACE = 0x0000010300000000,

        /// <summary>
        /// PCIe config register access. (RW) [lo-dword: register address]
        /// </summary>
        FPGA_CFGREGPCIE = 0x0000010400000000,

        /// <summary>
        /// CFG config register access. (RW) [lo-dword: register address]
        /// </summary>
        FPGA_CFGREGCFG = 0x0000010500000000,

        /// <summary>
        /// DRP config register access. (RW) [lo-dword: register address]
        /// </summary>
        FPGA_CFGREGDRP = 0x0000010600000000,

        /// <summary>
        /// Masked write to CFG config register. (W)  
        /// [lo-dword: register address]  
        /// [bytes: 0-1 data, 2-3 mask]
        /// </summary>
        FPGA_CFGREGCFG_MARKWR = 0x0000010700000000,

        /// <summary>
        /// Masked write to PCIe config register. (W)  
        /// [lo-dword: register address]  
        /// [bytes: 0-1 data, 2-3 mask]
        /// </summary>
        FPGA_CFGREGPCIE_MARKWR = 0x0000010800000000,

        /// <summary>
        /// Debug print FPGA config registers. (N/A)
        /// </summary>
        FPGA_CFGREG_DEBUGPRINT = 0x0000010a00000000,

        /// <summary>
        /// Probe FPGA. (RW)
        /// </summary>
        FPGA_PROBE = 0x0000010b00000000,

        /// <summary>
        /// Shadow read of FPGA config space. (R)
        /// </summary>
        FPGA_CFGSPACE_SHADOW_RD = 0x0000010c00000000,

        /// <summary>
        /// Shadow write of FPGA config space. (W)  
        /// [lo-dword: config space write base address]
        /// </summary>
        FPGA_CFGSPACE_SHADOW_WR = 0x0000010d00000000,

        /// <summary>
        /// Write single TLP bytes. (W)
        /// </summary>
        FPGA_TLP_WRITE_SINGLE = 0x0000011000000000,

        /// <summary>
        /// Write multiple LC_TLPs. (W)
        /// </summary>
        FPGA_TLP_WRITE_MULTIPLE = 0x0000011100000000,

        /// <summary>
        /// Convert single TLP to LPSTR; *pcbDataOut includes NULL terminator. (RW)
        /// </summary>
        FPGA_TLP_TOSTRING = 0x0000011200000000,

        /// <summary>
        /// Set/unset TLP user-defined context (pbDataIn == LPVOID). [not remote]. (W)
        /// </summary>
        FPGA_TLP_CONTEXT = 0x2000011400000000,

        /// <summary>
        /// Get TLP user-defined context. [not remote]. (R)
        /// </summary>
        FPGA_TLP_CONTEXT_RD = 0x2000011b00000000,

        /// <summary>
        /// Set/unset TLP callback function (pbDataIn == PLC_TLP_CALLBACK). [not remote]. (W)
        /// </summary>
        FPGA_TLP_FUNCTION_CALLBACK = 0x2000011500000000,

        /// <summary>
        /// Get TLP callback function. [not remote]. (R)
        /// </summary>
        FPGA_TLP_FUNCTION_CALLBACK_RD = 0x2000011c00000000,

        /// <summary>
        /// Set/unset BAR user-defined context (pbDataIn == LPVOID). [not remote]. (W)
        /// </summary>
        FPGA_BAR_CONTEXT = 0x2000011800000000,

        /// <summary>
        /// Get BAR user-defined context. [not remote]. (R)
        /// </summary>
        FPGA_BAR_CONTEXT_RD = 0x2000011d00000000,

        /// <summary>
        /// Set/unset BAR callback function (pbDataIn == PLC_BAR_CALLBACK). [not remote]. (W)
        /// </summary>
        FPGA_BAR_FUNCTION_CALLBACK = 0x2000011900000000,

        /// <summary>
        /// Get BAR callback function. [not remote]. (R)
        /// </summary>
        FPGA_BAR_FUNCTION_CALLBACK_RD = 0x2000011e00000000,

        /// <summary>
        /// Get BAR info (pbDataOut == LC_BAR_INFO[6]). (R)
        /// </summary>
        FPGA_BAR_INFO = 0x0000011a00000000,

        // ---- File ----

        /// <summary>
        /// Get dump file header. (R)
        /// </summary>
        FILE_DUMPHEADER_GET = 0x0000020100000000,

        // ---- General ----

        /// <summary>
        /// Get statistics. (R)
        /// </summary>
        STATISTICS_GET = 0x4000010000000000,

        /// <summary>
        /// Get memory map as LPSTR. (R)
        /// </summary>
        MEMMAP_GET = 0x4000020000000000,

        /// <summary>
        /// Set memory map as LPSTR. (W)
        /// </summary>
        MEMMAP_SET = 0x4000030000000000,

        /// <summary>
        /// Get memory map as LC_MEMMAP_ENTRY[]. (R)
        /// </summary>
        MEMMAP_GET_STRUCT = 0x4000040000000000,

        /// <summary>
        /// Set memory map as LC_MEMMAP_ENTRY[]. (W)
        /// </summary>
        MEMMAP_SET_STRUCT = 0x4000050000000000,

        // ---- Agent ----

        /// <summary>
        /// Execute Python agent. (RW) [lo-dword: optional timeout in ms]
        /// </summary>
        AGENT_EXEC_PYTHON = 0x8000000100000000,

        /// <summary>
        /// Exit process. (W) [lo-dword: process exit code]
        /// </summary>
        AGENT_EXIT_PROCESS = 0x8000000200000000,

        /// <summary>
        /// List VFS contents. (RW)
        /// </summary>
        AGENT_VFS_LIST = 0x8000000300000000,

        /// <summary>
        /// Read from VFS. (RW)
        /// </summary>
        AGENT_VFS_READ = 0x8000000400000000,

        /// <summary>
        /// Write to VFS. (RW)
        /// </summary>
        AGENT_VFS_WRITE = 0x8000000500000000,

        /// <summary>
        /// Get VFS options. (RW)
        /// </summary>
        AGENT_VFS_OPT_GET = 0x8000000600000000,

        /// <summary>
        /// Set VFS options. (RW)
        /// </summary>
        AGENT_VFS_OPT_SET = 0x8000000700000000,

        /// <summary>
        /// Initialize VFS. (RW)
        /// </summary>
        AGENT_VFS_INITIALIZE = 0x8000000800000000,

        /// <summary>
        /// VFS console. (RW)
        /// </summary>
        AGENT_VFS_CONSOLE = 0x8000000900000000
    }

}
