/*  
*  C# API wrapper 'vmmsharp' for MemProcFS 'vmm.dll' and LeechCore 'leechcore.dll' APIs.
*  
*  Please see the example project in vmmsharp_example for additional information.
*  
*  Please consult the C/C++ header files vmmdll.h and leechcore.h for information about parameters and API usage.
*  
*  (c) Ulf Frisk, 2020-2025
*  Author: Ulf Frisk, pcileech@frizk.net
*  
*/

/*  
 *  VmmSharpEx by Lone (Lone DMA)
 *  Copyright (C) 2025 AGPL-3.0
*/

using Collections.Pooled;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using VmmSharpEx.Internal;
using VmmSharpEx.Options;
using VmmSharpEx.Refresh;

namespace VmmSharpEx;

/// <summary>
/// Public managed API for interacting with MemProcFS and LeechCore.
/// </summary>
/// <remarks>
/// This class wraps a native VMM handle and exposes higher-level helpers around memory read/write, VFS, and process
/// enumeration facilities. Instances are <see cref="IDisposable"/> and must be disposed to release the native handle.
/// </remarks>
public sealed partial class Vmm : IDisposable
{
    #region Base Functionality

    public static implicit operator Vmm.Handle(Vmm vmm) => vmm._handle;

    private readonly Vmm.Handle _handle;
    private bool _disposed;

    /// <summary>
    /// Gets the underlying <see cref="LeechCore"/> context associated with this <see cref="Vmm"/> instance.
    /// </summary>
    public LeechCore LeechCore { get; }
    /// <summary>
    /// True if this <see cref="Vmm"/> instance has been disposed; otherwise false.
    /// </summary>
    public bool IsDisposed => _disposed;

    private readonly bool _enableMemoryWriting = true;

    /// <summary>
    /// Gets a value indicating whether memory writing via this high-level API is enabled.
    /// </summary>
    /// <remarks>
    /// Set this to <see langword="false"/> during initialization to disable all memory write operations exposed by this
    /// wrapper. Attempts to write memory will throw a <see cref="VmmException"/>. This setting is immutable after
    /// initialization.
    /// </remarks>
    public bool EnableMemoryWriting
    {
        get => _enableMemoryWriting;
        init
        {
            _enableMemoryWriting = value;
            if (!_enableMemoryWriting)
            {
                Log("Memory Writing Disabled!");
            }
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return _disposed ? "Vmm:Disposed" : $"Vmm:{_handle:X}";
    }

    /// <summary>
    /// Creates and initializes the native VMM context.
    /// </summary>
    /// <param name="configErrorInfo">Receives extended configuration error information, if available.</param>
    /// <param name="args">MemProcFS/Vmm command line arguments.</param>
    /// <returns>The native VMM handle on success.</returns>
    /// <exception cref="VmmException">Thrown if initialization fails.</exception>
    private static IntPtr Create(out LeechCore.LCConfigErrorInfo configErrorInfo, params string[] args)
    {
        var cbERROR_INFO = Marshal.SizeOf<Lci.LC_CONFIG_ERRORINFO>();
        var hVMM = Vmmi.VMMDLL_InitializeEx(args.Length, args, out var pLcErrorInfo);
        var vaLcCreateErrorInfo = pLcErrorInfo.ToInt64();
        configErrorInfo = new LeechCore.LCConfigErrorInfo
        {
            strUserText = ""
        };
        if (hVMM.ToInt64() == 0)
        {
            throw new VmmException("VMM INIT FAILED.");
        }

        if (vaLcCreateErrorInfo == 0)
        {
            return hVMM;
        }

        var e = Marshal.PtrToStructure<Lci.LC_CONFIG_ERRORINFO>(pLcErrorInfo);
        if (e.dwVersion == LeechCore.LC_CONFIG_ERRORINFO_VERSION)
        {
            configErrorInfo.fValid = true;
            configErrorInfo.fUserInputRequest = e.fUserInputRequest;
            if (e.cwszUserText > 0)
            {
                configErrorInfo.strUserText = Marshal.PtrToStringUni(checked((IntPtr)(vaLcCreateErrorInfo + cbERROR_INFO)));
            }
        }

        return hVMM;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Vmm"/> class.
    /// </summary>
    /// <remarks>This private constructor prevents parameterless instantiation.</remarks>
    private Vmm() { throw new NotImplementedException(); }

    /// <summary>
    /// Initialize a new <see cref="Vmm"/> instance with command line arguments and capture extended error information.
    /// </summary>
    /// <param name="configErrorInfo">Error information in case of an error.</param>
    /// <param name="args">MemProcFS/Vmm command line arguments.</param>
    public Vmm(out LeechCore.LCConfigErrorInfo configErrorInfo, params string[] args)
    {
        try
        {
            _handle = new Vmm.Handle(handle: Create(out configErrorInfo, args));
            LeechCore = new LeechCore(this);
            Log($"VmmSharpEx Initialized ({_handle:X16}).");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Initialize a new <see cref="Vmm"/> instance with command line arguments.
    /// </summary>
    /// <param name="args">MemProcFS/Vmm command line arguments.</param>
    public Vmm(params string[] args)
        : this(out _, args) { }

    /// <summary>
    /// Manually initialize plugins.
    /// </summary>
    /// <remarks>
    /// Plugins are not initialized during <see cref="Vmm"/> construction by default.
    /// </remarks>
    /// <returns><see langword="true"/> if plugins are loaded successfully; otherwise <see langword="false"/>.</returns>
    public bool InitializePlugins() => Vmmi.VMMDLL_InitializePlugins(_handle);

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true) == false)
        {
            LeechCore?.Dispose(); // Since this can be called from the ctor, Leechcore may be null here.
            RefreshManager.UnregisterAll(this);
            _handle?.Dispose();
        }
    }

    /// <summary>
    /// Close all <see cref="Vmm"/> instances in the native layer.
    /// </summary>
    /// <remarks>
    /// This invokes <see cref="Vmmi.VMMDLL_CloseAll"/> and affects all native VMM contexts in the process.
    /// </remarks>
    public static void CloseAll()
    {
        Vmmi.VMMDLL_CloseAll();
    }

    public sealed class Handle : SafeHandle
    {
        public Handle() : base(IntPtr.Zero, true) { }

        internal Handle(IntPtr handle) : base(IntPtr.Zero, true)
        {
            SetHandle(handle);
        }

        public override bool IsInvalid => this.handle == IntPtr.Zero;

        protected override bool ReleaseHandle()
        {
            Vmmi.VMMDLL_Close(this.handle);
            return true;
        }
    }

    #endregion

    #region Config Get/Set

    /// <summary>
    /// Memory model types known to MemProcFS.
    /// </summary>
    public enum MemoryModelType
    {
        /// <summary>Not applicable/unknown.</summary>
        MEMORYMODEL_NA = 0,
        /// <summary>x86 (32-bit) paging model.</summary>
        MEMORYMODEL_X86 = 1,
        /// <summary>x86 PAE (Physical Address Extension) paging model.</summary>
        MEMORYMODEL_X86PAE = 2,
        /// <summary>x64 (64-bit) paging model.</summary>
        MEMORYMODEL_X64 = 3,
        /// <summary>ARM64 paging model.</summary>
        MEMORYMODEL_ARM64 = 4
    }

    /// <summary>
    /// Operating system type inferred for the target system.
    /// </summary>
    public enum SystemType
    {
        /// <summary>Unknown x64 system.</summary>
        SYSTEM_UNKNOWN_X64 = 1,
        /// <summary>Windows x64.</summary>
        SYSTEM_WINDOWS_X64 = 2,
        /// <summary>Unknown x86 system.</summary>
        SYSTEM_UNKNOWN_X86 = 3,
        /// <summary>Windows x86.</summary>
        SYSTEM_WINDOWS_X86 = 4
    }



    //---------------------------------------------------------------------
    // CONFIG GET/SET:
    //---------------------------------------------------------------------

    /// <summary>
    /// Get a configuration option given by a <see cref="VmmOption"/> constant.
    /// </summary>
    /// <param name="fOption">The <see cref="VmmOption"/> option to retrieve.</param>
    /// <returns>The config value retrieved on success; otherwise <see langword="null"/>.</returns>
    public ulong? ConfigGet(VmmOption fOption)
    {
        if (!Vmmi.VMMDLL_ConfigGet(_handle, fOption, out var value))
        {
            return null;
        }

        return value;
    }

    /// <summary>
    /// Set a configuration option given by a <see cref="VmmOption"/> constant.
    /// </summary>
    /// <param name="fOption">The <see cref="VmmOption"/> option to set.</param>
    /// <param name="qwValue">The value to set.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public bool ConfigSet(VmmOption fOption, ulong qwValue)
    {
        return Vmmi.VMMDLL_ConfigSet(_handle, fOption, qwValue);
    }

    /// <summary>
    /// Returns the physical memory map in string format, with additional optional setup parameters.
    /// </summary>
    /// <param name="applyMap">If <see langword="true"/>, applies the memory map to the current <see cref="Vmm"/>/<see cref="LeechCore"/> instance.</param>
    /// <param name="outputFile">If non-<see langword="null"/>, writes the memory map to disk at the specified output location.</param>
    /// <returns>Memory map ptr in string format.</returns>
    /// <exception cref="VmmException">Thrown if the memory map cannot be retrieved or applied.</exception>
    public string GetMemoryMap(
        bool applyMap = false,
        string? outputFile = null)
    {
        var map = Map_GetPhysMem() ?? throw new VmmException("Failed to get memory map.");
        var sb = new StringBuilder();
        for (var i = 0; i < map.Length; i++)
        {
            sb.AppendLine($"{map[i].pa:X16} - {(map[i].pa + map[i].cb - 1):X16}");
        }

        string strMap = sb.ToString();
        if (applyMap)
        {
            if (!LeechCore.ExecuteCommand(LcCmd.MEMMAP_SET, Encoding.UTF8.GetBytes(strMap), out _))
            {
                throw new VmmException("LC_CMD_MEMMAP_SET FAIL");
            }
        }

        if (outputFile is not null)
        {
            File.WriteAllBytes(outputFile, Encoding.UTF8.GetBytes(strMap));
        }

        return strMap;
    }

    #endregion

    #region Memory Read/Write

    //---------------------------------------------------------------------
    // MEMORY READ/WRITE FUNCTIONALITY BELOW:
    //---------------------------------------------------------------------

    /// <summary>
    /// Special PID that indicates physical memory operations.
    /// </summary>
    public const uint PID_PHYSICALMEMORY = unchecked((uint)-1); // Pass as a PID Parameter to read Physical Memory

    /// <summary>
    /// Flag to combine with a PID to enable process kernel memory (use with extreme care).
    /// </summary>
    public const uint PID_PROCESS_WITH_KERNELMEMORY = 0x80000000; // Combine with dwPID to enable process kernel memory (NB! use with extreme care).

    /// <summary>
    /// Perform a scatter read of multiple page-sized virtual/physical memory ranges.
    /// </summary>
    /// <param name="pid">Process ID (PID) this operation will take place upon.</param>
    /// <param name="flags">VMM read flags.</param>
    /// <param name="vas">Page-aligned memory addresses.</param>
    /// <returns>Array of <see cref="LeechCore.MEM_SCATTER"/> results.</returns>
    /// <exception cref="VmmException">Thrown if the native scatter allocation fails.</exception>
    public unsafe LeechCore.MEM_SCATTER[] MemReadScatter(uint pid, VmmFlags flags, params ReadOnlySpan<ulong> vas)
    {
        if (!Lci.LcAllocScatter1((uint)vas.Length, out var pppMEMs) || pppMEMs == IntPtr.Zero)
        {
            throw new VmmException("LcAllocScatter1 FAIL");
        }
        try
        {
            var mems = new LeechCore.MEM_SCATTER[vas.Length];
            var ppMEMs = (LeechCore.MEM_SCATTER_NATIVE**)pppMEMs.ToPointer();
            int i;
            for (i = 0; i < vas.Length; i++)
            {
                var pMEM = ppMEMs[i];
                if (pMEM is null)
                    continue;
                pMEM->qwA = vas[i] & ~0xffful;
                pMEM->cb = 0x1000;
            }

            _ = Vmmi.VMMDLL_MemReadScatter(_handle, pid, pppMEMs, (uint)vas.Length, flags);

            for (i = 0; i < vas.Length; i++)
            {
                var pMEM = ppMEMs[i];
                if (pMEM is null)
                    continue;
                mems[i] = pMEM->ToManaged();
            }

            return mems;
        }
        finally
        {
            Lci.LcMemFree(pppMEMs);
        }
    }

    /// <summary>
    /// Read memory from a virtual address into a byte array.
    /// </summary>
    /// <remarks>
    /// NOTE: This method incurs a heap allocation for the returned byte array. For high-performance use other read methods instead.
    /// </remarks>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="va">Virtual address to read from.</param>
    /// <param name="cb">Count of bytes to read.</param>
    /// <param name="cbRead">Count of bytes actually read.</param>
    /// <param name="flags">VMM read flags.</param>
    /// <returns>A byte array with the read memory, otherwise <see langword="null"/>. Be sure to also check <paramref name="cbRead"/>.</returns>
    public unsafe byte[]? MemRead(uint pid, ulong va, uint cb, out uint cbRead, VmmFlags flags = VmmFlags.NONE)
    {
        var arr = new byte[cb];
        fixed (void* pb = arr)
        {
            if (!Vmmi.VMMDLL_MemReadEx(_handle, pid, va, pb, cb, out cbRead, flags))
            {
                return null;
            }
        }
        return arr;
    }

    /// <summary>
    /// Read memory from a virtual address into unmanaged memory.
    /// </summary>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="va">Virtual address to read from.</param>
    /// <param name="pb">Pointer to buffer to receive the read.</param>
    /// <param name="cb">Count of bytes to read.</param>
    /// <param name="cbRead">Receives the count of bytes successfully read.</param>
    /// <param name="flags">VMM read flags.</param>
    /// <returns><see langword="true"/> if successful; otherwise <see langword="false"/>. Be sure to check <paramref name="cbRead"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool MemRead(uint pid, ulong va, IntPtr pb, uint cb, out uint cbRead, VmmFlags flags = VmmFlags.NONE)
    {
        return MemRead(pid, va, pb.ToPointer(), cb, out cbRead, flags);
    }

    /// <summary>
    /// Read memory from a virtual address into unmanaged memory.
    /// </summary>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="va">Virtual address to read from.</param>
    /// <param name="pb">Pointer to buffer to receive the read.</param>
    /// <param name="cb">Count of bytes to read.</param>
    /// <param name="cbRead">Receives the count of bytes successfully read.</param>
    /// <param name="flags">VMM read flags.</param>
    /// <returns><see langword="true"/> if successful; otherwise <see langword="false"/>. Be sure to check <paramref name="cbRead"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool MemRead(uint pid, ulong va, void* pb, uint cb, out uint cbRead, VmmFlags flags = VmmFlags.NONE)
    {
        return Vmmi.VMMDLL_MemReadEx(_handle, pid, va, pb, cb, out cbRead, flags);
    }

    /// <summary>
    /// Read memory from a virtual address into a <see langword="struct"/> of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Struct/Ref struct type.</typeparam>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="va">Virtual address to read from.</param>
    /// <param name="flags">VMM read flags.</param>
    /// <returns><typeparamref name="T"/> value.</returns>
    /// <exception cref="VmmException"></exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe T MemReadValue<T>(uint pid, ulong va, VmmFlags flags = VmmFlags.NONE)
        where T : unmanaged, allows ref struct
    {
        if (!MemReadValue<T>(pid, va, out var result, flags))
            throw new VmmException("Memory Read Failed!");
        return result;
    }

    /// <summary>
    /// Read memory from a virtual address into a <see langword="struct"/> of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Struct/Ref struct type.</typeparam>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="va">Virtual address to read from.</param>
    /// <param name="result">Receives the value read on success; otherwise <see langword="default"/>.</param>
    /// <param name="flags">VMM read flags.</param>
    /// <returns><see langword="true"/> if successful; otherwise <see langword="false"/>.</returns>
    public unsafe bool MemReadValue<T>(uint pid, ulong va, out T result, VmmFlags flags = VmmFlags.NONE)
        where T : unmanaged, allows ref struct
    {
        uint cb = (uint)sizeof(T);
        result = default;
        fixed (void* pb = &result)
        {
            return Vmmi.VMMDLL_MemReadEx(_handle, pid, va, pb, cb, out var cbRead, flags) && cbRead == cb;
        }
    }

    /// <summary>
    /// Read memory from a virtual address into an array of type <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// NOTE: This method incurs a heap allocation for the returned byte array. For high-performance use other read methods instead.
    /// </remarks>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="va">Virtual address to read from.</param>
    /// <param name="count">Number of elements to read.</param>
    /// <param name="flags">VMM read flags.</param>
    /// <returns>An array on success; otherwise <see langword="null"/>.</returns>
    public unsafe T[]? MemReadArray<T>(uint pid, ulong va, int count, VmmFlags flags = VmmFlags.NONE)
        where T : unmanaged
    {
        if (count <= 0)
            return null;
        var array = new T[count];
        uint cb = checked((uint)sizeof(T) * (uint)count);
        fixed (T* pb = array)
        {
            if (!Vmmi.VMMDLL_MemReadEx(_handle, pid, va, pb, cb, out var cbRead, flags) || cbRead != cb)
            {
                return null;
            }
        }
        return array;
    }

    /// <summary>
    /// Read memory from a virtual address into a pooled array of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="va">Virtual address to read from.</param>
    /// <param name="count">Number of elements to read.</param>
    /// <param name="flags">VMM read flags.</param>
    /// <returns>A <see cref="IMemoryOwner{T}"/> lease, or <see langword="null"/> if failed. Be sure to call <see cref="IDisposable.Dispose()"/> when done.</returns>
    public unsafe IMemoryOwner<T>? MemReadPooled<T>(uint pid, ulong va, int count, VmmFlags flags = VmmFlags.NONE)
        where T : unmanaged
    {
        if (count <= 0)
            return null;
        var arr = new PooledMemory<T>(count);
        uint cb = checked((uint)sizeof(T) * (uint)count);
        fixed (T* pb = arr.Span)
        {
            if (!Vmmi.VMMDLL_MemReadEx(_handle, pid, va, pb, cb, out var cbRead, flags) || cbRead != cb)
            {
                arr.Dispose();
                return null;
            }
        }
        return arr;
    }

    /// <summary>
    /// Read memory into a <see cref="Span{T}"/>.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="va">Memory address to read from.</param>
    /// <param name="span">Span to receive the memory read.</param>
    /// <param name="flags">Read flags.</param>
    /// <returns><see langword="true"/> if successful; otherwise <see langword="false"/>.</returns>
    public unsafe bool MemReadSpan<T>(uint pid, ulong va, Span<T> span, VmmFlags flags = VmmFlags.NONE)
        where T : unmanaged
    {
        uint cb = checked((uint)sizeof(T) * (uint)span.Length);
        fixed (T* pb = span)
        {
            return Vmmi.VMMDLL_MemReadEx(_handle, pid, va, pb, cb, out var cbRead, flags) && cbRead == cb;
        }
    }

    /// <summary>
    /// Read memory from a virtual address into a managed <see cref="string"/>.
    /// </summary>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="va">Virtual address to read from.</param>
    /// <param name="cb">Number of bytes to read. Keep in mind some string encodings are 2–4 bytes per character.</param>
    /// <param name="encoding">String encoding for this read.</param>
    /// <param name="flags">VMM read flags.</param>
    /// <returns>A managed <see cref="string"/> on success; otherwise <see langword="null"/>.</returns>
    public unsafe string? MemReadString(uint pid, ulong va, int cb, Encoding encoding,
        VmmFlags flags = VmmFlags.NONE)
    {
        byte[]? rentedBytes = null;
        char[]? rentedChars = null;
        try
        {
            Span<byte> bytesSource = cb <= 256 ?
                stackalloc byte[cb] : (rentedBytes = ArrayPool<byte>.Shared.Rent(cb));
            var bytes = bytesSource.Slice(0, cb); // Rented Pool can have more than cb
            if (!MemReadSpan(pid, va, bytes, flags))
            {
                return null;
            }

            int charCount = encoding.GetCharCount(bytes);
            Span<char> charsSource = charCount <= 128 ?
                stackalloc char[charCount] : (rentedChars = ArrayPool<char>.Shared.Rent(charCount));
            var chars = charsSource.Slice(0, charCount);
            encoding.GetChars(bytes, chars);
            int nt = chars.IndexOf('\0');
            return nt != -1 ?
                chars.Slice(0, nt).ToString() : chars.ToString(); // Only one string allocation
        }
        finally
        {
            if (rentedBytes is not null)
                ArrayPool<byte>.Shared.Return(rentedBytes);
            if (rentedChars is not null)
                ArrayPool<char>.Shared.Return(rentedChars);
        }
    }

    /// <summary>
    /// Read a single 4096-byte page of memory.
    /// </summary>
    /// <param name="pid">PID of target process, (DWORD)-1 to read physical memory.</param>
    /// <param name="qwA">Page address to read from.</param>
    /// <returns>Byte array on success; otherwise null.</returns>
    public unsafe byte[]? MemReadPage(uint pid, ulong qwA)
    {
        var arr = new byte[0x1000];
        fixed (void* pb = arr)
        {
            if (!Vmmi.VMMDLL_MemReadPage(_handle, pid, qwA, pb))
                return null;
        }
        return arr;
    }

    /// <summary>
    /// Read a single 4096-byte page of memory (Pooled).
    /// </summary>
    /// <param name="pid">PID of target process, (DWORD)-1 to read physical memory.</param>
    /// <param name="qwA">Page address to read from.</param>
    /// <returns>Pooled array on success; otherwise null.</returns>
    public unsafe IMemoryOwner<byte>? MemReadPagePooled(uint pid, ulong qwA)
    {
        var pooled = new PooledMemory<byte>(0x1000);
        fixed (void* pb = pooled.Memory.Span)
        {
            if (!Vmmi.VMMDLL_MemReadPage(_handle, pid, qwA, pb))
            {
                pooled.Dispose();
                return null;
            }
        }
        return pooled;
    }

    /// <summary>
    /// Prefetch pages into the MemProcFS internal cache.
    /// </summary>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="vas">An array of the virtual addresses to prefetch.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public unsafe bool MemPrefetchPages(uint pid, params Span<ulong> vas)
    {
        fixed (void* pb = vas)
        {
            return Vmmi.VMMDLL_MemPrefetchPages(_handle, pid, pb, (uint)vas.Length);
        }
    }

    /// <summary>
    /// Write memory from unmanaged memory to a given virtual address.
    /// </summary>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="va">Virtual address to write to.</param>
    /// <param name="pb">Pointer to buffer to write from.</param>
    /// <param name="cb">Count of bytes to write.</param>
    /// <returns><see langword="true"/> if the write is successful; otherwise <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool MemWrite(uint pid, ulong va, IntPtr pb, uint cb)
    {
        return MemWrite(pid, va, pb.ToPointer(), cb);
    }

    /// <summary>
    /// Write memory from unmanaged memory to a given virtual address.
    /// </summary>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="va">Virtual address to write to.</param>
    /// <param name="pb">Pointer to buffer to write from.</param>
    /// <param name="cb">Count of bytes to write.</param>
    /// <returns><see langword="true"/> if the write is successful; otherwise <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool MemWrite(uint pid, ulong va, void* pb, uint cb)
    {
        ThrowIfMemWritesDisabled();
        return Vmmi.VMMDLL_MemWrite(_handle, pid, va, pb, cb);
    }

    /// <summary>
    /// Write memory from a struct value <typeparamref name="T"/> to a given virtual address.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="va">Virtual address to write to.</param>
    /// <param name="value">The value to write.</param>
    /// <returns><see langword="true"/> if the write is successful; otherwise <see langword="false"/>.</returns>
    public unsafe bool MemWriteValue<T>(uint pid, ulong va, T value)
        where T : unmanaged, allows ref struct
    {
        ThrowIfMemWritesDisabled();
        uint cb = (uint)sizeof(T);
        return Vmmi.VMMDLL_MemWrite(_handle, pid, va, &value, cb);
    }

    /// <summary>
    /// Write memory from a managed array of <typeparamref name="T"/> to a given virtual address.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="va">Virtual address to write to.</param>
    /// <param name="data">Managed <typeparamref name="T"/> array to write.</param>
    /// <returns><see langword="true"/> if the write is successful; otherwise <see langword="false"/>.</returns>
    public unsafe bool MemWriteArray<T>(uint pid, ulong va, T[] data)
        where T : unmanaged
    {
        ThrowIfMemWritesDisabled();
        uint cb = checked((uint)sizeof(T) * (uint)data.Length);
        fixed (T* pb = data)
        {
            return Vmmi.VMMDLL_MemWrite(_handle, pid, va, pb, cb);
        }
    }

    /// <summary>
    /// Write memory from a <see cref="Span{T}"/> of <typeparamref name="T"/> to a specified memory address.
    /// </summary>
    /// <typeparam name="T">Value type.</typeparam>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="va">Memory address to write to.</param>
    /// <param name="span">Span to write from.</param>
    /// <returns><see langword="true"/> if successful; otherwise <see langword="false"/>.</returns>
    public unsafe bool MemWriteSpan<T>(uint pid, ulong va, Span<T> span)
        where T : unmanaged
    {
        ThrowIfMemWritesDisabled();
        uint cb = checked((uint)sizeof(T) * (uint)span.Length);
        fixed (T* pb = span)
        {
            return Vmmi.VMMDLL_MemWrite(_handle, pid, va, pb, cb);
        }
    }

    /// <summary>
    /// Translate a virtual address to a physical address.
    /// </summary>
    /// <param name="pid">Process ID (PID) this operation will take place within.</param>
    /// <param name="va">Virtual address to translate from.</param>
    /// <param name="pa">Translated physical address if successful, otherwise 0.</param>
    /// <returns>True if successful, otherwise False.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MemVirt2Phys(uint pid, ulong va, out ulong pa)
    {
        return Vmmi.VMMDLL_MemVirt2Phys(_handle, pid, va, out pa);
    }

    #endregion

    #region VFS (Virtual File System) functionality

    //---------------------------------------------------------------------
    // VFS (VIRTUAL FILE SYSTEM) FUNCTIONALITY BELOW:
    //---------------------------------------------------------------------

    /// <summary>
    /// Extended VFS file information provided by VMMDLL when listing files/directories.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct VMMDLL_VFS_FILELIST_EXINFO
    {
        /// <summary>Structure version.</summary>
        public uint dwVersion;
        /// <summary>Indicates whether the file is compressed.</summary>
        public bool fCompressed;
        /// <summary>Creation timestamp (FILETIME).</summary>
        public ulong ftCreationTime;
        /// <summary>Last access timestamp (FILETIME).</summary>
        public ulong ftLastAccessTime;
        /// <summary>Last write timestamp (FILETIME).</summary>
        public ulong ftLastWriteTime;
    }

    /// <summary>
    /// Managed VFS entry describing a file or directory.
    /// </summary>
    public struct VfsEntry
    {
        /// <summary>The file or directory name.</summary>
        public string name;
        /// <summary><see langword="true"/> if the entry is a directory; otherwise <see langword="false"/>.</summary>
        public bool isDirectory;
        /// <summary>The size of the entry in bytes (0 for directories).</summary>
        public ulong size;
        /// <summary>Optional extended file information.</summary>
        public VMMDLL_VFS_FILELIST_EXINFO info;
    }

    /// <summary>
    /// Managed VFS context structure.
    /// Can be extended to hold additional user implementation if needed.
    /// </summary>
    public class VfsContext : IDisposable
    {
        internal readonly GCHandle _gcHandle;
        public List<VfsEntry> Entries { get; } = new();

        public VfsContext()
        {
            _gcHandle = GCHandle.Alloc(this);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                _gcHandle.Free();
            }
        }
    }

    [UnmanagedCallersOnly]
    private static unsafe int VfsList_AddFileCB(IntPtr hCtx, void* pName, ulong cb, IntPtr pExInfo)
    {
        var h = GCHandle.FromIntPtr(hCtx);
        var ctx = (VfsContext?)h.Target ?? throw new ArgumentNullException(nameof(hCtx));
        var e = new VfsEntry
        {
            name = Marshal.PtrToStringUTF8((IntPtr)pName) ?? string.Empty,
            isDirectory = false,
            size = cb
        };
        if (pExInfo != IntPtr.Zero)
        {
            e.info = Marshal.PtrToStructure<VMMDLL_VFS_FILELIST_EXINFO>(pExInfo);
        }

        ctx.Entries.Add(e);
        return 1; // continue
    }

    [UnmanagedCallersOnly]
    private static unsafe int VfsList_AddDirectoryCB(IntPtr hCtx, void* pName, IntPtr pExInfo)
    {
        var h = GCHandle.FromIntPtr(hCtx);
        var ctx = (VfsContext?)h.Target ?? throw new ArgumentNullException(nameof(hCtx));
        var e = new VfsEntry
        {
            name = Marshal.PtrToStringUTF8((IntPtr)pName) ?? string.Empty,
            isDirectory = true,
            size = 0
        };
        if (pExInfo != IntPtr.Zero)
        {
            e.info = Marshal.PtrToStructure<VMMDLL_VFS_FILELIST_EXINFO>(pExInfo);
        }

        ctx.Entries.Add(e);
        return 1; // continue
    }

    /// <summary>
    /// VFS list files and directories in a virtual file system path.
    /// </summary>
    /// <param name="path">Virtual path to enumerate.</param>
    /// <returns>A list with file and directory entries on success; a null list on failure.</returns>
    public unsafe List<VfsEntry>? VfsList(string path)
    {
        using var ctx = new VfsContext();
        Vmmi.VMMDLL_VFS_FILELIST FileList;
        FileList.dwVersion = Vmmi.VMMDLL_VFS_FILELIST_VERSION;
        FileList.h = GCHandle.ToIntPtr(ctx._gcHandle);
        FileList._Reserved = 0;
        FileList.pfnAddFile = &VfsList_AddFileCB;
        FileList.pfnAddDirectory = &VfsList_AddDirectoryCB;
        if (!Vmmi.VMMDLL_VfsList(_handle, path.Replace('/', '\\'), ref FileList))
            return null;
        return ctx.Entries;
    }

    /// <summary>
    /// VFS read data from a virtual file.
    /// </summary>
    /// <param name="fileName">The file name/path within the VFS.</param>
    /// <param name="data">The data read from the operation on success; otherwise null.</param>
    /// <param name="size">The maximum number of bytes to read. 0 = default = 16MB.</param>
    /// <param name="offset">Optional offset within the file to start reading at.</param>
    /// <returns>The NTSTATUS value of the operation (success = 0).</returns>
    public unsafe uint VfsRead(string fileName, out byte[]? data, uint size = 0, ulong offset = 0)
    {
        uint cbRead = 0;
        if (size == 0)
        {
            size = 0x01000000; // 16MB
        }

        var dataLocal = new byte[size];
        fixed (void* pb = dataLocal)
        {
            uint ret = Vmmi.VMMDLL_VfsRead(_handle, fileName.Replace('/', '\\'), pb, size, out cbRead, offset);
            if (ret == 0) // STATUS_SUCCESS
            {
                if (cbRead < size)
                {
                    Array.Resize(ref dataLocal, (int)cbRead);
                }
                data = dataLocal;
            }
            else
            {
                data = null;
            }
            return ret;
        }
    }

    /// <summary>
    /// VFS write data to a virtual file.
    /// </summary>
    /// <param name="fileName">The file name/path within the VFS.</param>
    /// <param name="data">The data to write.</param>
    /// <param name="offset">Optional offset within the file to start writing at.</param>
    /// <returns>The NTSTATUS value of the operation (success = 0).</returns>
    public unsafe uint VfsWrite(string fileName, ReadOnlySpan<byte> data, ulong offset = 0)
    {
        uint cbRead = 0;
        fixed (void* pb = data)
        {
            return Vmmi.VMMDLL_VfsWrite(_handle, fileName.Replace('/', '\\'), pb, (uint)data.Length, out cbRead, offset);
        }
    }

    #endregion

    #region Process functionality

    //---------------------------------------------------------------------
    // PROCESS FUNCTIONALITY BELOW:
    //---------------------------------------------------------------------

    /// <summary>
    /// Get all process IDs (PIDs) currently running on the target system.
    /// </summary>
    /// <returns>Array of PIDs; null array on failure.</returns>
    public unsafe uint[]? PidGetList()
    {
        bool result;
        ulong c = 0;
        result = Vmmi.VMMDLL_PidList(_handle, null, ref c);
        if (!result || c == 0)
        {
            return null;
        }

        fixed (byte* pb = new byte[c * 4])
        {
            result = Vmmi.VMMDLL_PidList(_handle, pb, ref c);
            if (!result || c == 0)
            {
                return null;
            }

            var m = new uint[c];
            for (ulong i = 0; i < c; i++)
            {
                m[i] = (uint)Marshal.ReadInt32((IntPtr)(pb + i * 4));
            }

            return m;
        }
    }

    /// <summary>
    /// Get the process ID (PID) for a given process name.
    /// </summary>
    /// <remarks>
    /// This only returns the first PID found for the process name. For multiple PIDs, use <see cref="PidGetAllFromName(string)"/>.
    /// </remarks>
    /// <param name="sProcName">Name of the process to look up.</param>
    /// <param name="pdwPID">Receives the PID ptr.</param>
    /// <returns><see langword="true"/> if successful; otherwise <see langword="false"/>.</returns>
    public bool PidGetFromName(string sProcName, out uint pdwPID)
    {
        return Vmmi.VMMDLL_PidGetFromName(_handle, sProcName, out pdwPID);
    }

    /// <summary>
    /// Get all process IDs (PIDs) for a given process name.
    /// </summary>
    /// <param name="sProcName">Name of the process to look up.</param>
    /// <returns>Array of PIDs that match, or null if failed.</returns>
    public uint[]? PidGetAllFromName(string sProcName)
    {
        var pids = new List<uint>();
        var procInfo = ProcessGetInformationAll();
        if (procInfo is null)
        {
            return null;
        }
        for (var i = 0; i < procInfo.Length; i++)
        {
            if (procInfo[i].sNameLong.Equals(sProcName, StringComparison.OrdinalIgnoreCase))
            {
                pids.Add(procInfo[i].dwPID);
            }
        }
        return pids.Count > 0 ?
            pids.ToArray() : null;
    }

    /// <summary>
    /// PTE (Page Table Entry) information.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="fIdentifyModules">If <see langword="true"/>, attempt to identify modules for regions.</param>
    /// <returns>Array of PTEs on success; null array on failure.</returns>
    public unsafe PteEntry[]? Map_GetPTE(uint pid, bool fIdentifyModules = true)
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_PTE>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_PTEENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetPte(_handle, pid, fIdentifyModules, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_PTE>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_PTE_VERSION)
            {
                return null;
            }

            var m = new PteEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_PTEENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                PteEntry e;
                e.vaBase = n.vaBase;
                e.vaEnd = n.vaBase + (n.cPages << 12) - 1;
                e.cbSize = n.cPages << 12;
                e.cPages = n.cPages;
                e.fPage = n.fPage;
                e.fWoW64 = n.fWoW64;
                e.sText = n.uszText;
                e.cSoftware = n.cSoftware;
                e.fR = true;
                e.fW = 0 != (e.fPage & 0x0000000000000002);
                e.fS = 0 == (e.fPage & 0x0000000000000004);
                e.fX = 0 == (e.fPage & 0x8000000000000000);
                m[i] = e;
            }
            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// VAD (Virtual Address Descriptor) information.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="fIdentifyModules">If <see langword="true"/>, attempt to identify modules for regions.</param>
    /// <returns>Array of VAD entries on success; null array on failure.</returns>
    public unsafe VadEntry[]? Map_GetVad(uint pid, bool fIdentifyModules = true)
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_VAD>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_VADENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetVad(_handle, pid, fIdentifyModules, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_VAD>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_VAD_VERSION)
            {
                return null;
            }

            var m = new VadEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_VADENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                VadEntry e;
                e.vaStart = n.vaStart;
                e.vaEnd = n.vaEnd;
                e.cbSize = n.vaEnd + 1 - n.vaStart;
                e.vaVad = n.vaVad;
                e.VadType = n.dw0 & 0x07;
                e.Protection = (n.dw0 >> 3) & 0x1f;
                e.fImage = ((n.dw0 >> 8) & 1) == 1;
                e.fFile = ((n.dw0 >> 9) & 1) == 1;
                e.fPageFile = ((n.dw0 >> 10) & 1) == 1;
                e.fPrivateMemory = ((n.dw0 >> 11) & 1) == 1;
                e.fTeb = ((n.dw0 >> 12) & 1) == 1;
                e.fStack = ((n.dw0 >> 13) & 1) == 1;
                e.fSpare = (n.dw0 >> 14) & 0x03;
                e.HeapNum = (n.dw0 >> 16) & 0x1f;
                e.fHeap = ((n.dw0 >> 23) & 1) == 1;
                e.cwszDescription = (n.dw0 >> 24) & 0xff;
                e.CommitCharge = n.dw1 & 0x7fffffff;
                e.MemCommit = ((n.dw1 >> 31) & 1) == 1;
                e.u2 = n.u2;
                e.cbPrototypePte = n.cbPrototypePte;
                e.vaPrototypePte = n.vaPrototypePte;
                e.vaSubsection = n.vaSubsection;
                e.sText = n.uszText;
                e.vaFileObject = n.vaFileObject;
                e.cVadExPages = n.cVadExPages;
                e.cVadExPagesBase = n.cVadExPagesBase;
                m[i] = e;
            }
            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Extended VAD (Virtual Address Descriptor) information.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="oPages">Page offset.</param>
    /// <param name="cPages">Number of pages.</param>
    /// <returns>Array of extended VAD entries on success; null array on failure.</returns>
    public unsafe VadExEntry[]? Map_GetVadEx(uint pid, uint oPages, uint cPages)
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_VADEX>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_VADEXENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetVadEx(_handle, pid, oPages, cPages, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_VADEX>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_VADEX_VERSION)
            {
                return null;
            }

            var m = new VadExEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_VADEXENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                VadExEntry e;
                e.tp = n.tp;
                e.iPML = n.iPML;
                e.pteFlags = n.pteFlags;
                e.va = n.va;
                e.pa = n.pa;
                e.pte = n.pte;
                e.proto.tp = n.proto_tp;
                e.proto.pa = n.proto_pa;
                e.proto.pte = n.proto_pte;
                e.vaVadBase = n.vaVadBase;
                m[i] = e;
            }

            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve module (loaded DLL) information for a process.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="fExtendedInfo">If <see langword="true"/>, attempts to include extended debug/version information.</param>
    /// <returns>An array of <see cref="ModuleEntry"/> on success; an null array on failure.</returns>
    public unsafe ModuleEntry[]? Map_GetModule(uint pid, bool fExtendedInfo = false)
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_MODULE>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_MODULEENTRY>();
        var flags = fExtendedInfo ? (uint)0xff : 0;
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetModule(_handle, pid, out pMap, flags))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_MODULE>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_MODULE_VERSION)
            {
                return null;
            }

            var m = new ModuleEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_MODULEENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                ModuleEntry e;
                ModuleEntryDebugInfo eDbg;
                ModuleEntryVersionInfo eVer;
                e.fValid = true;
                e.vaBase = n.vaBase;
                e.vaEntry = n.vaEntry;
                e.cbImageSize = n.cbImageSize;
                e.fWow64 = n.fWow64;
                e.sText = n.uszText;
                e.sFullName = n.uszFullName;
                e.tp = n.tp;
                e.cbFileSizeRaw = n.cbFileSizeRaw;
                e.cSection = n.cSection;
                e.cEAT = n.cEAT;
                e.cIAT = n.cIAT;
                // Extended Debug Information:
                if (n.pExDebugInfo.ToInt64() == 0)
                {
                    eDbg.fValid = false;
                    eDbg.dwAge = 0;
                    eDbg.sGuid = "";
                    eDbg.sPdbFilename = "";
                }
                else
                {
                    var nDbg = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_MODULEENTRY_DEBUGINFO>(n.pExDebugInfo);
                    eDbg.fValid = true;
                    eDbg.dwAge = nDbg.dwAge;
                    eDbg.sGuid = nDbg.uszGuid;
                    eDbg.sPdbFilename = nDbg.uszPdbFilename;
                }

                e.DebugInfo = eDbg;
                // Extended Version Information
                if (n.pExDebugInfo.ToInt64() == 0)
                {
                    eVer.fValid = false;
                    eVer.sCompanyName = "";
                    eVer.sFileDescription = "";
                    eVer.sFileVersion = "";
                    eVer.sInternalName = "";
                    eVer.sLegalCopyright = "";
                    eVer.sFileOriginalFilename = "";
                    eVer.sProductName = "";
                    eVer.sProductVersion = "";
                }
                else
                {
                    var nVer = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_MODULEENTRY_VERSIONINFO>(n.pExVersionInfo);
                    eVer.fValid = true;
                    eVer.sCompanyName = nVer.uszCompanyName;
                    eVer.sFileDescription = nVer.uszFileDescription;
                    eVer.sFileVersion = nVer.uszFileVersion;
                    eVer.sInternalName = nVer.uszInternalName;
                    eVer.sLegalCopyright = nVer.uszLegalCopyright;
                    eVer.sFileOriginalFilename = nVer.uszFileOriginalFilename;
                    eVer.sProductName = nVer.uszProductName;
                    eVer.sProductVersion = nVer.uszProductVersion;
                }

                e.VersionInfo = eVer;
                m[i] = e;
            }
            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Get a single module from its name. If more than one module with the same name is loaded, the first one is returned.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="module">Module name to look up.</param>
    /// <param name="result">Receives the <see cref="ModuleEntry"/> if successful.</param>
    /// <returns><see langword="true"/> if successful; otherwise <see langword="false"/>.</returns>
    public unsafe bool Map_GetModuleFromName(uint pid, string module, out ModuleEntry result)
    {
        result = default;
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetModuleFromName(_handle, pid, module, out pMap, 0))
            {
                return false;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_MODULEENTRY>(pMap);
            result.fValid = true;
            result.vaBase = nM.vaBase;
            result.vaEntry = nM.vaEntry;
            result.cbImageSize = nM.cbImageSize;
            result.fWow64 = nM.fWow64;
            result.sText = module;
            result.sFullName = nM.uszFullName;
            result.tp = nM.tp;
            result.cbFileSizeRaw = nM.cbFileSizeRaw;
            result.cSection = nM.cSection;
            result.cEAT = nM.cEAT;
            result.cIAT = nM.cIAT;
            return true;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve information about unloaded modules.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <returns>An array of <see cref="UnloadedModuleEntry"/> on success; an null array on failure.</returns>
    public unsafe UnloadedModuleEntry[]? Map_GetUnloadedModule(uint pid)
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_UNLOADEDMODULE>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_UNLOADEDMODULEENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetUnloadedModule(_handle, pid, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_UNLOADEDMODULE>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_UNLOADEDMODULE_VERSION)
            {
                return null;
            }

            var m = new UnloadedModuleEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_UNLOADEDMODULEENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                UnloadedModuleEntry e;
                e.vaBase = n.vaBase;
                e.cbImageSize = n.cbImageSize;
                e.fWow64 = n.fWow64;
                e.wText = n.uszText;
                e.dwCheckSum = n.dwCheckSum;
                e.dwTimeDateStamp = n.dwTimeDateStamp;
                e.ftUnload = n.ftUnload;
                m[i] = e;
            }
            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve EAT (Export Address Table) information for a module.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="module">Module name to query.</param>
    /// <param name="info">Receives high-level <see cref="EATInfo"/> for the module.</param>
    /// <returns>An array of <see cref="EATEntry"/> on success; an null array on failure.</returns>
    public unsafe EATEntry[]? Map_GetEAT(uint pid, string module, out EATInfo info)
    {
        info = new EATInfo();
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_EAT>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_EATENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetEAT(_handle, pid, module, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_EAT>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_EAT_VERSION)
            {
                return null;
            }

            var m = new EATEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_EATENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                EATEntry e;
                e.vaFunction = n.vaFunction;
                e.dwOrdinal = n.dwOrdinal;
                e.oFunctionsArray = n.oFunctionsArray;
                e.oNamesArray = n.oNamesArray;
                e.sFunction = n.uszFunction;
                e.sForwardedFunction = n.uszForwardedFunction;
                m[i] = e;
            }

            info.fValid = true;
            info.vaModuleBase = nM.vaModuleBase;
            info.vaAddressOfFunctions = nM.vaAddressOfFunctions;
            info.vaAddressOfNames = nM.vaAddressOfNames;
            info.cNumberOfFunctions = nM.cNumberOfFunctions;
            info.cNumberOfForwardedFunctions = nM.cNumberOfForwardedFunctions;
            info.cNumberOfNames = nM.cNumberOfNames;
            info.dwOrdinalBase = nM.dwOrdinalBase;

            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve IAT (Import Address Table) information for a module.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="module">Module name to query.</param>
    /// <returns>An array of <see cref="IATEntry"/> on success; an null array on failure.</returns>
    public unsafe IATEntry[]? Map_GetIAT(uint pid, string module)
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_IAT>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_IATENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetIAT(_handle, pid, module, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_IAT>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_IAT_VERSION)
            {
                return null;
            }

            var m = new IATEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_IATENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                IATEntry e;
                e.vaFunction = n.vaFunction;
                e.sFunction = n.uszFunction;
                e.sModule = n.uszModule;
                e.f32 = n.f32;
                e.wHint = n.wHint;
                e.rvaFirstThunk = n.rvaFirstThunk;
                e.rvaOriginalFirstThunk = n.rvaOriginalFirstThunk;
                e.rvaNameModule = n.rvaNameModule;
                e.rvaNameFunction = n.rvaNameFunction;
                e.vaModule = nM.vaModuleBase;
                m[i] = e;
            }

            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve heap information for a process.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="result">Receives the <see cref="HeapMap"/> ptr on success.</param>
    /// <returns><see langword="true"/> if successful; otherwise <see langword="false"/>.</returns>
    public unsafe bool Map_GetHeap(uint pid, out HeapMap result)
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_HEAP>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_HEAPENTRY>();
        var cbSEGENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_HEAPSEGMENTENTRY>();
        result = default;
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetHeap(_handle, pid, out pMap))
            {
                return false;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_HEAP>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_HEAP_VERSION)
            {
                return false;
            }

            result.heaps = new HeapEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var nH = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_HEAPENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                result.heaps[i].va = nH.va;
                result.heaps[i].f32 = nH.f32;
                result.heaps[i].tpHeap = nH.tp;
                result.heaps[i].iHeapNum = nH.dwHeapNum;
            }

            result.segments = new HeapSegmentEntry[nM.cSegments];
            for (var i = 0; i < nM.cMap; i++)
            {
                var nH = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_HEAPSEGMENTENTRY>(checked((IntPtr)(nM.pSegments.ToInt64() + i * cbSEGENTRY)));
                result.segments[i].va = nH.va;
                result.segments[i].cb = nH.cb;
                result.segments[i].tpHeapSegment = nH.tp;
                result.segments[i].iHeapNum = nH.iHeap;
            }
            return true;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve heap allocation entries for a specific heap by base address or heap number.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="vaHeapOrHeapNum">Heap base address or heap index.</param>
    /// <returns>An array of <see cref="HeapAllocEntry"/> on success; an null array on failure.</returns>
    public unsafe HeapAllocEntry[]? Map_GetHeapAlloc(uint pid, ulong vaHeapOrHeapNum)
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_HEAPALLOC>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_HEAPALLOCENTRY>();
        IntPtr pHeapAllocMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetHeapAlloc(_handle, pid, vaHeapOrHeapNum, out pHeapAllocMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_HEAPALLOC>(pHeapAllocMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_HEAPALLOC_VERSION)
            {
                return null;
            }

            var m = new HeapAllocEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_HEAPALLOCENTRY>(checked((IntPtr)(pHeapAllocMap.ToInt64() + cbMAP + i * cbENTRY)));
                m[i].va = n.va;
                m[i].cb = n.cb;
                m[i].tp = n.tp;
            }

            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pHeapAllocMap);
        }
    }

    /// <summary>
    /// Retrieve thread information for a process.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <returns>An array of <see cref="ThreadEntry"/> on success; an null array on failure.</returns>
    public unsafe ThreadEntry[]? Map_GetThread(uint pid)
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_THREAD>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_THREADENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetThread(_handle, pid, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_THREAD>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_THREAD_VERSION)
            {
                return null;
            }

            var m = new ThreadEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_THREADENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                ThreadEntry e;
                e.dwTID = n.dwTID;
                e.dwPID = n.dwPID;
                e.dwExitStatus = n.dwExitStatus;
                e.bState = n.bState;
                e.bRunning = n.bRunning;
                e.bPriority = n.bPriority;
                e.bBasePriority = n.bBasePriority;
                e.vaETHREAD = n.vaETHREAD;
                e.vaTeb = n.vaTeb;
                e.ftCreateTime = n.ftCreateTime;
                e.ftExitTime = n.ftExitTime;
                e.vaStartAddress = n.vaStartAddress;
                e.vaWin32StartAddress = n.vaWin32StartAddress;
                e.vaStackBaseUser = n.vaStackBaseUser;
                e.vaStackLimitUser = n.vaStackLimitUser;
                e.vaStackBaseKernel = n.vaStackBaseKernel;
                e.vaStackLimitKernel = n.vaStackLimitKernel;
                e.vaImpersonationToken = n.vaImpersonationToken;
                e.vaTrapFrame = n.vaTrapFrame;
                e.vaRIP = n.vaRIP;
                e.vaRSP = n.vaRSP;
                e.qwAffinity = n.qwAffinity;
                e.dwUserTime = n.dwUserTime;
                e.dwKernelTime = n.dwKernelTime;
                e.bSuspendCount = n.bSuspendCount;
                e.bWaitReason = n.bWaitReason;
                m[i] = e;
            }
            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve thread call stack information.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="tid">Thread ID to retrieve the call stack for.</param>
    /// <param name="flags">Supported <see cref="VmmFlags"/> values include <see cref="VmmFlags.NONE"/>, <see cref="VmmFlags.NOCACHE"/>, and <see cref="VmmFlags.FORCECACHE_READ"/>.</param>
    /// <returns>An array of <see cref="ThreadCallstackEntry"/> on success; an null array on failure.</returns>
    public unsafe ThreadCallstackEntry[]? Map_GetThread_Callstack(uint pid, uint tid, VmmFlags flags = VmmFlags.NONE)
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_THREAD_CALLSTACK>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_THREAD_CALLSTACKENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetThread_Callstack(_handle, pid, tid, flags, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_THREAD_CALLSTACK>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_THREAD_CALLSTACK_VERSION)
            {
                return null;
            }

            var m = new ThreadCallstackEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_THREAD_CALLSTACKENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                ThreadCallstackEntry e;
                e.dwPID = pid;
                e.dwTID = tid;
                e.i = n.i;
                e.fRegPresent = n.fRegPresent;
                e.vaRetAddr = n.vaRetAddr;
                e.vaRSP = n.vaRSP;
                e.vaBaseSP = n.vaBaseSP;
                e.cbDisplacement = (int)n.cbDisplacement;
                e.sModule = n.uszModule;
                e.sFunction = n.uszFunction;
                m[i] = e;
            }

            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve handle information for a process.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <returns>An array of <see cref="HandleEntry"/> on success; an null array on failure.</returns>
    public unsafe HandleEntry[]? Map_GetHandle(uint pid)
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_HANDLE>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_HANDLEENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetHandle(_handle, pid, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_HANDLE>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_HANDLE_VERSION)
            {
                return null;
            }

            var m = new HandleEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_HANDLEENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                HandleEntry e;
                e.vaObject = n.vaObject;
                e.dwHandle = n.dwHandle;
                e.dwGrantedAccess = n.dwGrantedAccess_iType & 0x00ffffff;
                e.iType = n.dwGrantedAccess_iType >> 24;
                e.qwHandleCount = n.qwHandleCount;
                e.qwPointerCount = n.qwPointerCount;
                e.vaObjectCreateInfo = n.vaObjectCreateInfo;
                e.vaSecurityDescriptor = n.vaSecurityDescriptor;
                e.sText = n.uszText;
                e.dwPID = n.dwPID;
                e.dwPoolTag = n.dwPoolTag;
                e.sType = n.uszType;
                m[i] = e;
            }
            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Get the user-mode path of the process image.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <returns>A string path on success; otherwise a null string.</returns>
    public string? GetProcessPathUser(uint pid)
    {
        return GetProcessInformationString(pid, VMMDLL_PROCESS_INFORMATION_OPT_STRING_PATH_USER_IMAGE);
    }

    /// <summary>
    /// Get the kernel-mode path of the process image.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <returns>A string path on success; otherwise a null string.</returns>
    public string? GetProcessPathKernel(uint pid)
    {
        return GetProcessInformationString(pid, VMMDLL_PROCESS_INFORMATION_OPT_STRING_PATH_KERNEL);
    }

    /// <summary>
    /// Get the process command line.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <returns>The command line string on success; otherwise a null string.</returns>
    public string? GetProcessCmdline(uint pid)
    {
        return GetProcessInformationString(pid, VMMDLL_PROCESS_INFORMATION_OPT_STRING_CMDLINE);
    }

    /// <summary>
    /// Get the string representation of a process info option.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="fOptionString">A VMMDLL_PROCESS_INFORMATION_OPT_* flag indicating which string to fetch.</param>
    /// <returns>The string value on success; otherwise a null string.</returns>
    public unsafe string? GetProcessInformationString(uint pid, uint fOptionString)
    {
        var pb = Vmmi.VMMDLL_ProcessGetInformationString(_handle, pid, fOptionString);
        try
        {
            if (pb == IntPtr.Zero)
            {
                return null;
            }

            var s = Marshal.PtrToStringAnsi(pb);
            return s;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pb);
        }
    }

    /// <summary>
    /// Retrieve IMAGE_DATA_DIRECTORY information for the specified module.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="sModule">Module name.</param>
    /// <returns>An array of <see cref="IMAGE_DATA_DIRECTORY"/>; null array on failure.</returns>
    public unsafe IMAGE_DATA_DIRECTORY[]? ProcessGetDirectories(uint pid, string sModule)
    {
        var PE_DATA_DIRECTORIES = new string[16] { "EXPORT", "IMPORT", "RESOURCE", "EXCEPTION", "SECURITY", "BASERELOC", "DEBUG", "ARCHITECTURE", "GLOBALPTR", "TLS", "LOAD_CONFIG", "BOUND_IMPORT", "IAT", "DELAY_IMPORT", "COM_DESCRIPTOR", "RESERVED" };
        bool result;
        var cbENTRY = (uint)Marshal.SizeOf<Vmmi.VMMDLL_IMAGE_DATA_DIRECTORY>();
        fixed (byte* pb = new byte[16 * cbENTRY])
        {
            result = Vmmi.VMMDLL_ProcessGetDirectories(_handle, pid, sModule, pb);
            if (!result)
            {
                return null;
            }

            var m = new IMAGE_DATA_DIRECTORY[16];
            for (var i = 0; i < 16; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_IMAGE_DATA_DIRECTORY>((IntPtr)(pb + i * cbENTRY));
                IMAGE_DATA_DIRECTORY e;
                e.name = PE_DATA_DIRECTORIES[i];
                e.VA = n.VA;
                e.Size = n.Size;
                m[i] = e;
            }

            return m;
        }
    }

    /// <summary>
    /// Retrieve IMAGE_SECTION_HEADER information for the specified module.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="sModule">Module name.</param>
    /// <returns>An array of <see cref="IMAGE_SECTION_HEADER"/>; null array on failure.</returns>
    public unsafe IMAGE_SECTION_HEADER[]? ProcessGetSections(uint pid, string sModule)
    {
        bool result;
        var cbENTRY = (uint)Marshal.SizeOf<Vmmi.VMMDLL_IMAGE_SECTION_HEADER>();
        result = Vmmi.VMMDLL_ProcessGetSections(_handle, pid, sModule, null, 0, out var cData);
        if (!result || cData == 0)
        {
            return null;
        }

        fixed (byte* pb = new byte[cData * cbENTRY])
        {
            result = Vmmi.VMMDLL_ProcessGetSections(_handle, pid, sModule, pb, cData, out cData);
            if (!result || cData == 0)
            {
                return null;
            }

            var m = new IMAGE_SECTION_HEADER[cData];
            for (var i = 0; i < cData; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_IMAGE_SECTION_HEADER>((IntPtr)(pb + i * cbENTRY));
                IMAGE_SECTION_HEADER e;
                e.Name = n.Name;
                e.MiscPhysicalAddressOrVirtualSize = n.MiscPhysicalAddressOrVirtualSize;
                e.VA = n.VA;
                e.SizeOfRawData = n.SizeOfRawData;
                e.PointerToRawData = n.PointerToRawData;
                e.PointerToRelocations = n.PointerToRelocations;
                e.PointerToLinenumbers = n.PointerToLinenumbers;
                e.NumberOfRelocations = n.NumberOfRelocations;
                e.NumberOfLinenumbers = n.NumberOfLinenumbers;
                e.Characteristics = n.Characteristics;
                m[i] = e;
            }

            return m;
        }
    }

    /// <summary>
    /// Get the function address of a function in a loaded module.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="wszModuleName">Module name.</param>
    /// <param name="szFunctionName">Function name.</param>
    /// <returns>The function virtual address on success; 0 on failure.</returns>
    public ulong ProcessGetProcAddress(uint pid, string wszModuleName, string szFunctionName)
    {
        return Vmmi.VMMDLL_ProcessGetProcAddress(_handle, pid, wszModuleName, szFunctionName);
    }

    /// <summary>
    /// Get the base address of a loaded module.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="wszModuleName">Module name.</param>
    /// <returns>The module base address on success; 0 on failure.</returns>
    public ulong ProcessGetModuleBase(uint pid, string wszModuleName)
    {
        return Vmmi.VMMDLL_ProcessGetModuleBase(_handle, pid, wszModuleName);
    }

    /// <summary>
    /// Get process information for a specific process.
    /// </summary>
    /// <param name="pid">Process ID (PID) for this operation.</param>
    /// <param name="result">Receives the <see cref="ProcessInfo"/> if successful.</param>
    /// <returns><see langword="true"/> if successful; otherwise <see langword="false"/>.</returns>
    public unsafe bool ProcessGetInformation(uint pid, out ProcessInfo result)
    {
        result = default;
        var cbENTRY = (ulong)Marshal.SizeOf<Vmmi.VMMDLL_PROCESS_INFORMATION>();
        fixed (byte* pb = new byte[cbENTRY])
        {
            Marshal.WriteInt64(new IntPtr(pb + 0), unchecked((long)Vmmi.VMMDLL_PROCESS_INFORMATION_MAGIC));
            Marshal.WriteInt16(new IntPtr(pb + 8), unchecked((short)Vmmi.VMMDLL_PROCESS_INFORMATION_VERSION));
            if (!Vmmi.VMMDLL_ProcessGetInformation(_handle, pid, pb, ref cbENTRY))
            {
                return false;
            }

            var n = Marshal.PtrToStructure<Vmmi.VMMDLL_PROCESS_INFORMATION>((IntPtr)pb);
            if (n.wVersion != Vmmi.VMMDLL_PROCESS_INFORMATION_VERSION)
            {
                return false;
            }

            result.fValid = true;
            result.tpMemoryModel = n.tpMemoryModel;
            result.tpSystem = n.tpSystem;
            result.fUserOnly = n.fUserOnly;
            result.dwPID = n.dwPID;
            result.dwPPID = n.dwPPID;
            result.dwState = n.dwState;
            result.sName = n.szName;
            result.sNameLong = n.szNameLong;
            result.paDTB = n.paDTB;
            result.paDTB_UserOpt = n.paDTB_UserOpt;
            result.vaEPROCESS = n.vaEPROCESS;
            result.vaPEB = n.vaPEB;
            result.fWow64 = n.fWow64;
            result.vaPEB32 = n.vaPEB32;
            result.dwSessionId = n.dwSessionId;
            result.qwLUID = n.qwLUID;
            result.sSID = n.szSID;
            result.IntegrityLevel = n.IntegrityLevel;

            return true;
        }
    }

    /// <summary>
    /// Get process information for all processes.
    /// </summary>
    /// <returns>An array of <see cref="ProcessInfo"/>; null array on failure.</returns>
    public unsafe ProcessInfo[]? ProcessGetInformationAll()
    {
        var cbENTRY = (uint)Marshal.SizeOf<Vmmi.VMMDLL_PROCESS_INFORMATION>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_ProcessGetInformationAll(_handle, out pMap, out uint pc))
            {
                return null;
            }
            var m = new ProcessInfo[pc];
            for (var i = 0; i < pc; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_PROCESS_INFORMATION>(checked((IntPtr)(pMap + i * cbENTRY)));
                if (i == 0 && n.wVersion != Vmmi.VMMDLL_PROCESS_INFORMATION_VERSION)
                {
                    return null;
                }

                ProcessInfo e;
                e.fValid = true;
                e.tpMemoryModel = n.tpMemoryModel;
                e.tpSystem = n.tpSystem;
                e.fUserOnly = n.fUserOnly;
                e.dwPID = n.dwPID;
                e.dwPPID = n.dwPPID;
                e.dwState = n.dwState;
                e.sName = n.szName;
                e.sNameLong = n.szNameLong;
                e.paDTB = n.paDTB;
                e.paDTB_UserOpt = n.paDTB_UserOpt;
                e.vaEPROCESS = n.vaEPROCESS;
                e.vaPEB = n.vaPEB;
                e.fWow64 = n.fWow64;
                e.vaPEB32 = n.vaPEB32;
                e.dwSessionId = n.dwSessionId;
                e.qwLUID = n.qwLUID;
                e.sSID = n.szSID;
                e.IntegrityLevel = n.IntegrityLevel;
                m[i] = e;
            }
            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Process metadata as returned by MemProcFS/VMMDLL.
    /// </summary>
    public struct ProcessInfo
    {
        /// <summary>Indicates the entry contains valid data.</summary>
        public bool fValid;
        /// <summary>Memory model type (see underlying VMMDLL).</summary>
        public uint tpMemoryModel;
        /// <summary>System type (see underlying VMMDLL).</summary>
        public uint tpSystem;
        /// <summary>True if user-mode only.</summary>
        public bool fUserOnly;
        /// <summary>Process identifier.</summary>
        public uint dwPID;
        /// <summary>Parent process identifier.</summary>
        public uint dwPPID;
        /// <summary>Process state flags.</summary>
        public uint dwState;
        /// <summary>Short process name.</summary>
        public string sName;
        /// <summary>Long process name.</summary>
        public string sNameLong;
        /// <summary>Directory table base (kernel).</summary>
        public ulong paDTB;
        /// <summary>User-mode DTB (optional).</summary>
        public ulong paDTB_UserOpt;
        /// <summary>EPROCESS address.</summary>
        public ulong vaEPROCESS;
        /// <summary>PEB address.</summary>
        public ulong vaPEB;
        /// <summary>True if the process is WoW64.</summary>
        public bool fWow64;
        /// <summary>PEB32 address for WoW64 processes.</summary>
        public uint vaPEB32;
        /// <summary>Windows session id.</summary>
        public uint dwSessionId;
        /// <summary>Process LUID.</summary>
        public ulong qwLUID;
        /// <summary>Process SID string.</summary>
        public string sSID;
        /// <summary>Integrity level value.</summary>
        public uint IntegrityLevel;
    }

    /// <summary>
    /// Page Table Entry (PTE) mapping entry.
    /// </summary>
    public struct PteEntry
    {
        public ulong vaBase;
        public ulong vaEnd;
        public ulong cbSize;
        public ulong cPages;
        public ulong fPage;
        public bool fWoW64;
        public string sText;
        public uint cSoftware;
        public bool fS;
        public bool fR;
        public bool fW;
        public bool fX;
    }

    /// <summary>
    /// Virtual Address Descriptor (VAD) mapping entry.
    /// </summary>
    public struct VadEntry
    {
        public ulong vaStart;
        public ulong vaEnd;
        public ulong vaVad;
        public ulong cbSize;
        public uint VadType;
        public uint Protection;
        public bool fImage;
        public bool fFile;
        public bool fPageFile;
        public bool fPrivateMemory;
        public bool fTeb;
        public bool fStack;
        public uint fSpare;
        public uint HeapNum;
        public bool fHeap;
        public uint cwszDescription;
        public uint CommitCharge;
        public bool MemCommit;
        public uint u2;
        public uint cbPrototypePte;
        public ulong vaPrototypePte;
        public ulong vaSubsection;
        public string sText;
        public ulong vaFileObject;
        public uint cVadExPages;
        public uint cVadExPagesBase;
    }

    /// <summary>
    /// Prototype information for a VAD EX entry.
    /// </summary>
    public struct VadExEntryPrototype
    {
        public uint tp;
        public ulong pa;
        public ulong pte;
    }

    /// <summary>
    /// Extended VAD entry with page table details.
    /// </summary>
    public struct VadExEntry
    {
        public uint tp;
        public uint iPML;
        public ulong va;
        public ulong pa;
        public ulong pte;
        public uint pteFlags;
        public VadExEntryPrototype proto;
        public ulong vaVadBase;
    }

    /// <summary>
    /// Module type constants.
    /// </summary>
    public const uint MAP_MODULEENTRY_TP_NORMAL = 0;
    /// <summary>Module is data only.</summary>
    public const uint VMMDLL_MODULE_TP_DATA = 1;
    /// <summary>Module not linked.</summary>
    public const uint VMMDLL_MODULE_TP_NOTLINKED = 2;
    /// <summary>Module injected.</summary>
    public const uint VMMDLL_MODULE_TP_INJECTED = 3;

    /// <summary>
    /// Extended debug information associated with a module entry.
    /// </summary>
    public struct ModuleEntryDebugInfo
    {
        public bool fValid;
        public uint dwAge;
        public string sGuid;
        public string sPdbFilename;
    }

    /// <summary>
    /// Version information associated with a module entry.
    /// </summary>
    public struct ModuleEntryVersionInfo
    {
        public bool fValid;
        public string sCompanyName;
        public string sFileDescription;
        public string sFileVersion;
        public string sInternalName;
        public string sLegalCopyright;
        public string sFileOriginalFilename;
        public string sProductName;
        public string sProductVersion;
    }

    /// <summary>
    /// Module entry describing a loaded module/DLL.
    /// </summary>
    public struct ModuleEntry
    {
        public bool fValid;
        public ulong vaBase;
        public ulong vaEntry;
        public uint cbImageSize;
        public bool fWow64;
        public string sText;
        public string sFullName;
        public uint tp;
        public uint cbFileSizeRaw;
        public uint cSection;
        public uint cEAT;
        public uint cIAT;
        public ModuleEntryDebugInfo DebugInfo;
        public ModuleEntryVersionInfo VersionInfo;
    }

    /// <summary>
    /// Entry describing an unloaded module.
    /// </summary>
    public struct UnloadedModuleEntry
    {
        public ulong vaBase;
        public uint cbImageSize;
        public bool fWow64;
        public string wText;
        /// <summary>User-mode only.</summary>
        public uint dwCheckSum; // user-mode only
        /// <summary>User-mode only.</summary>
        public uint dwTimeDateStamp; // user-mode only
        /// <summary>Kernel-mode only.</summary>
        public ulong ftUnload; // kernel-mode only
    }

    /// <summary>
    /// High-level EAT metadata for a module.
    /// </summary>
    public struct EATInfo
    {
        public bool fValid;
        public ulong vaModuleBase;
        public ulong vaAddressOfFunctions;
        public ulong vaAddressOfNames;
        public uint cNumberOfFunctions;
        public uint cNumberOfForwardedFunctions;
        public uint cNumberOfNames;
        public uint dwOrdinalBase;
    }

    /// <summary>
    /// Export Address Table entry.
    /// </summary>
    public struct EATEntry
    {
        public ulong vaFunction;
        public uint dwOrdinal;
        public uint oFunctionsArray;
        public uint oNamesArray;
        public string sFunction;
        public string sForwardedFunction;
    }

    /// <summary>
    /// Import Address Table entry.
    /// </summary>
    public struct IATEntry
    {
        public ulong vaFunction;
        public ulong vaModule;
        public string sFunction;
        public string sModule;
        public bool f32;
        public ushort wHint;
        public uint rvaFirstThunk;
        public uint rvaOriginalFirstThunk;
        public uint rvaNameModule;
        public uint rvaNameFunction;
    }

    /// <summary>
    /// Heap descriptor entry.
    /// </summary>
    public struct HeapEntry
    {
        public ulong va;
        public uint tpHeap;
        public bool f32;
        public uint iHeapNum;
    }

    /// <summary>
    /// Heap segment descriptor entry.
    /// </summary>
    public struct HeapSegmentEntry
    {
        public ulong va;
        public uint cb;
        public uint tpHeapSegment;
        public uint iHeapNum;
    }

    /// <summary>
    /// Heap map ptr containing heaps and segments.
    /// </summary>
    public struct HeapMap
    {
        public HeapEntry[] heaps;
        public HeapSegmentEntry[] segments;
    }

    /// <summary>
    /// Heap allocation entry.
    /// </summary>
    public struct HeapAllocEntry
    {
        public ulong va;
        public uint cb;
        public uint tp;
    }

    /// <summary>
    /// Thread descriptor entry.
    /// </summary>
    public struct ThreadEntry
    {
        public uint dwTID;
        public uint dwPID;
        public uint dwExitStatus;
        public byte bState;
        public byte bRunning;
        public byte bPriority;
        public byte bBasePriority;
        public ulong vaETHREAD;
        public ulong vaTeb;
        public ulong ftCreateTime;
        public ulong ftExitTime;
        public ulong vaStartAddress;
        public ulong vaWin32StartAddress;
        public ulong vaStackBaseUser;
        public ulong vaStackLimitUser;
        public ulong vaStackBaseKernel;
        public ulong vaStackLimitKernel;
        public ulong vaTrapFrame;
        public ulong vaImpersonationToken;
        public ulong vaRIP;
        public ulong vaRSP;
        public ulong qwAffinity;
        public uint dwUserTime;
        public uint dwKernelTime;
        public byte bSuspendCount;
        public byte bWaitReason;
    }

    /// <summary>
    /// A single thread call stack frame entry.
    /// </summary>
    public struct ThreadCallstackEntry
    {
        public uint dwPID;
        public uint dwTID;
        public uint i;
        public bool fRegPresent;
        public ulong vaRetAddr;
        public ulong vaRSP;
        public ulong vaBaseSP;
        public int cbDisplacement;
        public string sModule;
        public string sFunction;
    }

    /// <summary>
    /// Kernel handle table entry.
    /// </summary>
    public struct HandleEntry
    {
        /// <summary>Kernel object address.</summary>
        public ulong vaObject;
        /// <summary>Raw handle value.</summary>
        public uint dwHandle;
        /// <summary>Granted access mask (low 24 bits of native field).</summary>
        public uint dwGrantedAccess;
        /// <summary>Object type index (high 8 bits of native field).</summary>
        public uint iType;
        /// <summary>Handle reference count.</summary>
        public ulong qwHandleCount;
        /// <summary>Pointer reference count.</summary>
        public ulong qwPointerCount;
        /// <summary>Pointer to OBJECT_CREATE_INFORMATION.</summary>
        public ulong vaObjectCreateInfo;
        /// <summary>Pointer to security descriptor.</summary>
        public ulong vaSecurityDescriptor;
        /// <summary>Descriptive text, if available.</summary>
        public string sText;
        /// <summary>Owning process ID.</summary>
        public uint dwPID;
        /// <summary>Pool tag.</summary>
        public uint dwPoolTag;
        /// <summary>Object type name.</summary>
        public string sType;
    }

    /// <summary>Process information string option: kernel path.</summary>
    public const uint VMMDLL_PROCESS_INFORMATION_OPT_STRING_PATH_KERNEL = 1;
    /// <summary>Process information string option: user image path.</summary>
    public const uint VMMDLL_PROCESS_INFORMATION_OPT_STRING_PATH_USER_IMAGE = 2;
    /// <summary>Process information string option: command line.</summary>
    public const uint VMMDLL_PROCESS_INFORMATION_OPT_STRING_CMDLINE = 3;

    /// <summary>
    /// Struct corresponding to the native PE IMAGE_SECTION_HEADER.
    /// </summary>
    public struct IMAGE_SECTION_HEADER
    {
        public string Name;
        public uint MiscPhysicalAddressOrVirtualSize;
        public uint VA;
        public uint SizeOfRawData;
        public uint PointerToRawData;
        public uint PointerToRelocations;
        public uint PointerToLinenumbers;
        public ushort NumberOfRelocations;
        public ushort NumberOfLinenumbers;
        public uint Characteristics;
    }

    /// <summary>
    /// Struct corresponding to the native PE IMAGE_DATA_DIRECTORY.
    /// </summary>
    public struct IMAGE_DATA_DIRECTORY
    {
        public string name;
        public uint VA;
        public uint Size;
    }

    #endregion

    #region Registry functionality

    //---------------------------------------------------------------------
    // REGISTRY FUNCTIONALITY BELOW:
    //---------------------------------------------------------------------

    /// <summary>
    /// Registry hive descriptor.
    /// </summary>
    public struct RegHiveEntry
    {
        public ulong vaCMHIVE;
        public ulong vaHBASE_BLOCK;
        public uint cbLength;
        public string sName;
        public string sNameShort;
        public string sHiveRootPath;
    }

    /// <summary>
    /// Registry subkey descriptor.
    /// </summary>
    public struct RegEnumKeyEntry
    {
        public string sName;
        public ulong ftLastWriteTime;
    }

    /// <summary>
    /// Registry value descriptor.
    /// </summary>
    public struct RegEnumValueEntry
    {
        public string sName;
        public uint type;
        public uint size;
    }

    /// <summary>
    /// Registry enumeration ptr containing keys and values.
    /// </summary>
    public struct RegEnumEntry
    {
        public string sKeyFullPath;
        public List<RegEnumKeyEntry> KeyList;
        public List<RegEnumValueEntry> ValueList;
    }

    /// <summary>
    /// List the registry hives.
    /// </summary>
    /// <returns>An array of <see cref="RegHiveEntry"/> on success; an null array on failure.</returns>
    public unsafe RegHiveEntry[]? WinReg_HiveList()
    {
        bool result;
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_REGISTRY_HIVE_INFORMATION>();
        result = Vmmi.VMMDLL_WinReg_HiveList(_handle, null, 0, out var cHives);
        if (!result || cHives == 0)
        {
            return null;
        }

        fixed (byte* pb = new byte[cHives * cbENTRY])
        {
            result = Vmmi.VMMDLL_WinReg_HiveList(_handle, pb, cHives, out cHives);
            if (!result)
            {
                return null;
            }

            var m = new RegHiveEntry[cHives];
            for (var i = 0; i < cHives; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_REGISTRY_HIVE_INFORMATION>((IntPtr)(pb + i * cbENTRY));
                RegHiveEntry e;
                if (n.wVersion != Vmmi.VMMDLL_REGISTRY_HIVE_INFORMATION_VERSION)
                {
                    return null;
                }

                e.vaCMHIVE = n.vaCMHIVE;
                e.vaHBASE_BLOCK = n.vaHBASE_BLOCK;
                e.cbLength = n.cbLength;
                e.sName = Encoding.UTF8.GetString(n.uszName);
                e.sName = e.sName.Substring(0, e.sName.IndexOf((char)0));
                e.sNameShort = Encoding.UTF8.GetString(n.uszNameShort);
                e.sHiveRootPath = Encoding.UTF8.GetString(n.uszHiveRootPath);
                m[i] = e;
            }

            return m;
        }
    }

    /// <summary>
    /// Read a region from a registry hive.
    /// </summary>
    /// <param name="vaCMHIVE">Virtual address of the registry hive.</param>
    /// <param name="ra">Hive registry address (ra).</param>
    /// <param name="cb">Number of bytes to read.</param>
    /// <param name="flags">Optional <see cref="VmmFlags"/> to control the read.</param>
    /// <returns>Data read on success (length may differ from requested read size); null array on failure.</returns>
    public unsafe byte[]? WinReg_HiveReadEx(ulong vaCMHIVE, uint ra, uint cb, VmmFlags flags = VmmFlags.NONE)
    {
        uint cbRead;
        var data = new byte[cb];
        fixed (void* pb = data)
        {
            if (!Vmmi.VMMDLL_WinReg_HiveReadEx(_handle, vaCMHIVE, ra, pb, cb, out cbRead, flags))
            {
                return null;
            }
        }

        if (cbRead != cb)
        {
            Array.Resize(ref data, (int)cbRead);
        }

        return data;
    }

    /// <summary>
    /// Write to a registry hive.
    /// </summary>
    /// <remarks>
    /// NB! This is a very dangerous operation and is not recommended. Only proceed if you know what you're doing.
    /// </remarks>
    /// <param name="vaCMHIVE">The virtual address of the registry hive.</param>
    /// <param name="ra">Hive registry address (ra).</param>
    /// <param name="data">Data to write.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public unsafe bool WinReg_HiveWrite(ulong vaCMHIVE, uint ra, ReadOnlySpan<byte> data)
    {
        fixed (void* pb = data)
        {
            return Vmmi.VMMDLL_WinReg_HiveWrite(_handle, vaCMHIVE, ra, pb, (uint)data.Length);
        }
    }

    /// <summary>
    /// Enumerate a registry key for subkeys and values.
    /// </summary>
    /// <param name="sKeyFullPath">Full registry path to enumerate.</param>
    /// <returns>A <see cref="RegEnumEntry"/> containing subkeys and values on success.</returns>
    public unsafe RegEnumEntry WinReg_Enum(string sKeyFullPath)
    {
        uint i, cchName, cbData = 0;
        var re = new RegEnumEntry
        {
            sKeyFullPath = sKeyFullPath,
            KeyList = new List<RegEnumKeyEntry>(),
            ValueList = new List<RegEnumValueEntry>()
        };
        fixed (void* pb = new byte[0x1000])
        {
            i = 0;
            cchName = 0x800;
            while (Vmmi.VMMDLL_WinReg_EnumKeyEx(_handle, sKeyFullPath, i, pb, ref cchName, out var ftLastWriteTime))
            {
                var e = new RegEnumKeyEntry
                {
                    ftLastWriteTime = ftLastWriteTime,
                    sName = new string((sbyte*)pb, 0, 2 * (int)Math.Max(1, cchName) - 2, Encoding.UTF8)
                };
                re.KeyList.Add(e);
                i++;
                cchName = 0x800;
            }

            i = 0;
            cchName = 0x800;
            while (Vmmi.VMMDLL_WinReg_EnumValue(_handle, sKeyFullPath, i, pb, ref cchName, out var lpType, null, ref cbData))
            {
                var e = new RegEnumValueEntry
                {
                    type = lpType,
                    size = cbData,
                    sName = new string((sbyte*)pb, 0, 2 * (int)Math.Max(1, cchName) - 2, Encoding.UTF8)
                };
                re.ValueList.Add(e);
                i++;
                cchName = 0x800;
            }
        }

        return re;
    }

    /// <summary>
    /// Read a registry value.
    /// </summary>
    /// <param name="sValueFullPath">Full registry value path.</param>
    /// <param name="tp">Receives the registry value type.</param>
    /// <returns>Value data on success; otherwise <see langword="null"/>.</returns>
    public unsafe byte[]? WinReg_QueryValue(string sValueFullPath, out uint tp)
    {
        bool result;
        uint cb = 0;
        result = Vmmi.VMMDLL_WinReg_QueryValueEx(_handle, sValueFullPath, out tp, null, ref cb);
        if (!result)
        {
            return null;
        }

        var data = new byte[cb];
        fixed (void* pb = data)
        {
            result = Vmmi.VMMDLL_WinReg_QueryValueEx(_handle, sValueFullPath, out tp, pb, ref cb);
            return result ? data : null;
        }
    }

    #endregion // Registry functionality

    #region Map functionality

    //---------------------------------------------------------------------
    // "MAP" FUNCTIONALITY BELOW:
    //---------------------------------------------------------------------

    /// <summary>Page is writable.</summary>
    public const ulong MEMMAP_FLAG_PAGE_W = 0x0000000000000002;
    /// <summary>Page is not shareable.</summary>
    public const ulong MEMMAP_FLAG_PAGE_NS = 0x0000000000000004;
    /// <summary>Page is non-executable.</summary>
    public const ulong MEMMAP_FLAG_PAGE_NX = 0x8000000000000000;
    /// <summary>Mask of supported page flags.</summary>
    public const ulong MEMMAP_FLAG_PAGE_MASK = 0x8000000000000006;

    /// <summary>
    /// Network endpoint address entry.
    /// </summary>
    public struct NetEntryAddress
    {
        public bool fValid;
        public ushort port;
        public byte[] pbAddr;
        public string sText;
    }

    /// <summary>
    /// Network connection entry.
    /// </summary>
    public struct NetEntry
    {
        public uint dwPID;
        public uint dwState;
        public uint dwPoolTag;
        public ushort AF;
        public NetEntryAddress src;
        public NetEntryAddress dst;
        public ulong vaObj;
        public ulong ftTime;
        public string sText;
    }

    /// <summary>
    /// Physical memory range entry.
    /// </summary>
    public struct MemoryEntry
    {
        public ulong pa;
        public ulong cb;
    }

    /// <summary>
    /// Kernel device entry.
    /// </summary>
    public struct KDeviceEntry
    {
        public ulong va;
        public uint iDepth;
        public uint dwDeviceType;
        public string sDeviceType;
        public ulong vaDriverObject;
        public ulong vaAttachedDevice;
        public ulong vaFileSystemDevice;
        public string sVolumeInfo;
    }

    /// <summary>
    /// Kernel driver entry.
    /// </summary>
    public struct KDriverEntry
    {
        public ulong va;
        public ulong vaDriverStart;
        public ulong cbDriverSize;
        public ulong vaDeviceObject;
        public string sName;
        public string sPath;
        public string sServiceKeyName;
        public ulong[] MajorFunction;
    }

    /// <summary>
    /// Kernel named object entry.
    /// </summary>
    public struct KObjectEntry
    {
        public ulong va;
        public ulong vaParent;
        public ulong[] vaChild;
        public string sName;
        public string sType;
    }

    /// <summary>
    /// Kernel pool allocation entry.
    /// </summary>
    public struct PoolEntry
    {
        public ulong va;
        public uint cb;
        public uint fAlloc;
        public uint tpPool;
        public uint tpSS;
        public uint dwTag;
        public string sTag;
    }

    /// <summary>
    /// User information entry.
    /// </summary>
    public struct UserEntry
    {
        public string sSID;
        public string sText;
        public ulong vaRegHive;
    }

    /// <summary>
    /// Virtual machine entry.
    /// </summary>
    public struct VirtualMachineEntry
    {
        public ulong hVM;
        public string sName;
        public ulong gpaMax;
        public uint tp;
        public bool fActive;
        public bool fReadOnly;
        public bool fPhysicalOnly;
        public uint dwPartitionID;
        public uint dwVersionBuild;
        public uint tpSystem;
        public uint dwParentVmmMountID;
        public uint dwVmMemPID;
    }

    /// <summary>
    /// Windows service entry.
    /// </summary>
    public struct ServiceEntry
    {
        public ulong vaObj;
        public uint dwPID;
        public uint dwOrdinal;
        public string sServiceName;
        public string sDisplayName;
        public string sPath;
        public string sUserTp;
        public string sUserAcct;
        public string sImagePath;
        public uint dwStartType;
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
    }

    /// <summary>
    /// PFN state types.
    /// </summary>
    public enum PfnType
    {
        Zero = 0,
        Free = 1,
        Standby = 2,
        Modified = 3,
        ModifiedNoWrite = 4,
        Bad = 5,
        Active = 6,
        Transition = 7
    }

    /// <summary>
    /// Extended PFN types.
    /// </summary>
    public enum PfnTypeExtended
    {
        Unknown = 0,
        Unused = 1,
        ProcessPrivate = 2,
        PageTable = 3,
        LargePage = 4,
        DriverLocked = 5,
        Shareable = 6,
        File = 7
    }

    /// <summary>
    /// PFN entry details.
    /// </summary>
    public struct PfnEntry
    {
        public uint dwPfn;
        public PfnType tp;
        public PfnTypeExtended tpExtended;
        public ulong va;
        public ulong vaPte;
        public ulong OriginalPte;
        public uint dwPID;
        public bool fPrototype;
        public bool fModified;
        public bool fReadInProgress;
        public bool fWriteInProgress;
        public byte priority;
    }

    /// <summary>
    /// Retrieve active network connections.
    /// </summary>
    /// <returns>An array of <see cref="NetEntry"/>; null array on failure.</returns>
    public unsafe NetEntry[]? Map_GetNet()
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_NET>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_NETENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetNet(_handle, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_NET>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_NET_VERSION)
            {
                return null;
            }

            var m = new NetEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_NETENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                NetEntry e;
                e.dwPID = n.dwPID;
                e.dwState = n.dwState;
                e.dwPoolTag = n.dwPoolTag;
                e.AF = n.AF;
                e.src.fValid = n.src_fValid;
                e.src.port = n.src_port;
                e.src.pbAddr = n.src_pbAddr;
                e.src.sText = n.src_uszText;
                e.dst.fValid = n.dst_fValid;
                e.dst.port = n.dst_port;
                e.dst.pbAddr = n.dst_pbAddr;
                e.dst.sText = n.dst_uszText;
                e.vaObj = n.vaObj;
                e.ftTime = n.ftTime;
                e.sText = n.uszText;
                m[i] = e;
            }
            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve the physical memory map.
    /// </summary>
    /// <returns>An array of <see cref="MemoryEntry"/> elements; null array on failure.</returns>
    public unsafe MemoryEntry[]? Map_GetPhysMem()
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_PHYSMEM>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_PHYSMEMENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetPhysMem(_handle, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_PHYSMEM>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_PHYSMEM_VERSION)
            {
                return null;
            }

            var m = new MemoryEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_PHYSMEMENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                MemoryEntry e;
                e.pa = n.pa;
                e.cb = n.cb;
                m[i] = e;
            }
            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve the kernel devices on the system.
    /// </summary>
    /// <returns>An array of <see cref="KDeviceEntry"/> elements; null array on failure.</returns>
    public unsafe KDeviceEntry[]? Map_GetKDevice()
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_KDEVICE>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_KDEVICEENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetKDevice(_handle, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_KDEVICE>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_KDEVICE_VERSION)
            {
                return null;
            }

            var m = new KDeviceEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_KDEVICEENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                KDeviceEntry e;
                e.va = n.va;
                e.iDepth = n.iDepth;
                e.dwDeviceType = n.dwDeviceType;
                e.sDeviceType = n.uszDeviceType;
                e.vaDriverObject = n.vaDriverObject;
                e.vaAttachedDevice = n.vaAttachedDevice;
                e.vaFileSystemDevice = n.vaFileSystemDevice;
                e.sVolumeInfo = n.uszVolumeInfo;
                m[i] = e;
            }
            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve the kernel drivers on the system.
    /// </summary>
    /// <returns>An array of <see cref="KDriverEntry"/> elements; null array on failure.</returns>
    public unsafe KDriverEntry[]? Map_GetKDriver()
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_KDRIVER>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_KDRIVERENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetKDriver(_handle, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_KDRIVER>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_KDRIVER_VERSION)
            {
                return null;
            }

            var m = new KDriverEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_KDRIVERENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                KDriverEntry e;
                e.va = n.va;
                e.vaDriverStart = n.vaDriverStart;
                e.cbDriverSize = n.cbDriverSize;
                e.vaDeviceObject = n.vaDeviceObject;
                e.sName = n.uszName;
                e.sPath = n.uszPath;
                e.sServiceKeyName = n.uszServiceKeyName;
                e.MajorFunction = new ulong[28];
                for (var j = 0; j < 28; j++)
                {
                    e.MajorFunction[j] = n.MajorFunction[j];
                }

                m[i] = e;
            }
            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve the kernel named objects on the system.
    /// </summary>
    /// <returns>An array of <see cref="KObjectEntry"/> elements; null array on failure.</returns>
    public unsafe KObjectEntry[]? Map_GetKObject()
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_KOBJECT>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_KOBJECTENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetKObject(_handle, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_KOBJECT>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_KOBJECT_VERSION)
            {
                return null;
            }

            var m = new KObjectEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_KOBJECTENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                KObjectEntry e;
                e.va = n.va;
                e.vaParent = n.vaParent;
                e.vaChild = new ulong[n.cvaChild];
                for (var j = 0; j < n.cvaChild; j++)
                {
                    e.vaChild[j] = (ulong)Marshal.ReadInt64(n.pvaChild, j * 8);
                }

                e.sName = n.uszName;
                e.sType = n.uszType;
                m[i] = e;
            }
            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve entries from the kernel pool.
    /// </summary>
    /// <param name="isBigPoolOnly">
    /// Set to <see langword="true"/> to only retrieve big pool allocations (faster). Default is to retrieve all allocations.
    /// </param>
    /// <returns>An array of <see cref="PoolEntry"/> elements; null array on failure.</returns>
    public unsafe PoolEntry[]? Map_GetPool(bool isBigPoolOnly = false)
    {
        byte[] tag = { 0, 0, 0, 0 };
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_POOL>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_POOLENTRY>();
        var flags = isBigPoolOnly ? VmmPoolMapFlags.BIG : VmmPoolMapFlags.ALL;
        IntPtr pN = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetPool(_handle, out pN, flags))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_POOL>(pN);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_POOL_VERSION)
            {
                return null;
            }

            var eM = new PoolEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var nE = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_POOLENTRY>(checked((IntPtr)(pN.ToInt64() + cbMAP + i * cbENTRY)));
                eM[i].va = nE.va;
                eM[i].cb = nE.cb;
                eM[i].tpPool = nE.tpPool;
                eM[i].tpSS = nE.tpSS;
                eM[i].dwTag = nE.dwTag;
                tag[0] = (byte)((nE.dwTag >> 00) & 0xff);
                tag[1] = (byte)((nE.dwTag >> 08) & 0xff);
                tag[2] = (byte)((nE.dwTag >> 16) & 0xff);
                tag[3] = (byte)((nE.dwTag >> 24) & 0xff);
                eM[i].sTag = Encoding.ASCII.GetString(tag);
            }
            return eM;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pN);
        }
    }

    /// <summary>
    /// Retrieve the detected users on the system.
    /// </summary>
    /// <returns>An array of <see cref="UserEntry"/> elements; null array on failure.</returns>
    public unsafe UserEntry[]? Map_GetUsers()
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_USER>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_USERENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetUsers(_handle, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_USER>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_USER_VERSION)
            {
                return null;
            }

            var m = new UserEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_USERENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                UserEntry e;
                e.sSID = n.uszSID;
                e.sText = n.uszText;
                e.vaRegHive = n.vaRegHive;
                m[i] = e;
            }
            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve the detected virtual machines on the system. This includes Hyper-V, WSL and other virtual machines running on top of the Windows Hypervisor Platform.
    /// </summary>
    /// <returns>An array of <see cref="VirtualMachineEntry"/> elements; null array on failure.</returns>
    public unsafe VirtualMachineEntry[]? Map_GetVM()
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_VM>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_VMENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetVM(_handle, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_VM>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_VM_VERSION)
            {
                return null;
            }

            var m = new VirtualMachineEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_VMENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                VirtualMachineEntry e;
                e.hVM = n.hVM;
                e.sName = n.uszName;
                e.gpaMax = n.gpaMax;
                e.tp = n.tp;
                e.fActive = n.fActive;
                e.fReadOnly = n.fReadOnly;
                e.fPhysicalOnly = n.fPhysicalOnly;
                e.dwPartitionID = n.dwPartitionID;
                e.dwVersionBuild = n.dwVersionBuild;
                e.tpSystem = n.tpSystem;
                e.dwParentVmmMountID = n.dwParentVmmMountID;
                e.dwVmMemPID = n.dwVmMemPID;
                m[i] = e;
            }
            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve the services on the system.
    /// </summary>
    /// <returns>An array of <see cref="ServiceEntry"/> elements; null array on failure.</returns>
    public unsafe ServiceEntry[]? Map_GetServices()
    {
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_SERVICE>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_SERVICEENTRY>();
        IntPtr pMap = default;
        try
        {
            if (!Vmmi.VMMDLL_Map_GetServices(_handle, out pMap))
            {
                return null;
            }

            var nM = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_SERVICE>(pMap);
            if (nM.dwVersion != Vmmi.VMMDLL_MAP_SERVICE_VERSION)
            {
                return null;
            }

            var m = new ServiceEntry[nM.cMap];
            for (var i = 0; i < nM.cMap; i++)
            {
                var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_SERVICEENTRY>(checked((IntPtr)(pMap.ToInt64() + cbMAP + i * cbENTRY)));
                ServiceEntry e;
                e.vaObj = n.vaObj;
                e.dwPID = n.dwPID;
                e.dwOrdinal = n.dwOrdinal;
                e.sServiceName = n.uszServiceName;
                e.sDisplayName = n.uszDisplayName;
                e.sPath = n.uszPath;
                e.sUserTp = n.uszUserTp;
                e.sUserAcct = n.uszUserAcct;
                e.sImagePath = n.uszImagePath;
                e.dwStartType = n.dwStartType;
                e.dwServiceType = n.dwServiceType;
                e.dwCurrentState = n.dwCurrentState;
                e.dwControlsAccepted = n.dwControlsAccepted;
                e.dwWin32ExitCode = n.dwWin32ExitCode;
                e.dwServiceSpecificExitCode = n.dwServiceSpecificExitCode;
                e.dwCheckPoint = n.dwCheckPoint;
                e.dwWaitHint = n.dwWaitHint;
                m[i] = e;
            }
            return m;
        }
        finally
        {
            Vmmi.VMMDLL_MemFree(pMap);
        }
    }

    /// <summary>
    /// Retrieve the PFN entries for the specified PFN numbers.
    /// </summary>
    /// <param name="pfns">The PFN numbers to retrieve information for.</param>
    /// <returns>An array of <see cref="PfnEntry"/>; null array on failure.</returns>
    public unsafe PfnEntry[]? Map_GetPfn(params Span<uint> pfns)
    {
        bool result;
        uint cbPfns;
        var cbMAP = Marshal.SizeOf<Vmmi.VMMDLL_MAP_PFN>();
        var cbENTRY = Marshal.SizeOf<Vmmi.VMMDLL_MAP_PFNENTRY>();
        if (pfns.IsEmpty)
        {
            return null;
        }

        fixed (void* pbPfns = pfns)
        {
            cbPfns = (uint)(cbMAP + pfns.Length * cbENTRY);
            fixed (byte* pb = new byte[cbPfns])
            {
                result =
                    Vmmi.VMMDLL_Map_GetPfn(_handle, pbPfns, (uint)pfns.Length, null, ref cbPfns) &&
                    Vmmi.VMMDLL_Map_GetPfn(_handle, pbPfns, (uint)pfns.Length, pb, ref cbPfns);
                if (!result)
                {
                    return null;
                }

                var pm = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_PFN>((IntPtr)pb);
                if (pm.dwVersion != Vmmi.VMMDLL_MAP_PFN_VERSION)
                {
                    return null;
                }

                var m = new PfnEntry[pm.cMap];
                for (var i = 0; i < pm.cMap; i++)
                {
                    var n = Marshal.PtrToStructure<Vmmi.VMMDLL_MAP_PFNENTRY>((IntPtr)(pb + cbMAP + i * cbENTRY));
                    var e = new PfnEntry
                    {
                        dwPfn = n.dwPfn,
                        tp = (PfnType)((n._u3 >> 16) & 0x07),
                        tpExtended = (PfnTypeExtended)n.tpExtended,
                        vaPte = n.vaPte,
                        OriginalPte = n.OriginalPte,
                        fModified = ((n._u3 >> 20) & 1) == 1,
                        fReadInProgress = ((n._u3 >> 21) & 1) == 1,
                        fWriteInProgress = ((n._u3 >> 19) & 1) == 1,
                        priority = (byte)((n._u3 >> 24) & 7),
                        fPrototype = ((n._u4 >> 57) & 1) == 1
                    };
                    if (e.tp == PfnType.Active && !e.fPrototype)
                    {
                        e.va = n.va;
                        e.dwPID = n.dwPfnPte[0];
                    }

                    m[i] = e;
                }

                return m;
            }
        }
    }

    #endregion // Map functionality

    #region PDB Functionality

    /// <summary>
    /// Load a .pdb symbol file and return its associated module name upon success.
    /// </summary>
    /// <param name="pid">Process ID (PID) whose module the symbols belong to.</param>
    /// <param name="vaModuleBase">Module base address.</param>
    /// <param name="szModuleName">Receives the module name if successful, otherwise null.</param>
    /// <returns><see langword="true"/> if the PDB was loaded successfully; otherwise <see langword="false"/>.</returns>
    public unsafe bool PdbLoad(uint pid, ulong vaModuleBase, out string? szModuleName)
    {
        szModuleName = null;
        var data = new byte[260];
        fixed (void* pb = data)
        {
            var result = Vmmi.VMMDLL_PdbLoad(_handle, pid, vaModuleBase, pb);
            if (!result)
            {
                return false;
            }

            szModuleName = Encoding.UTF8.GetString(data);
            szModuleName = szModuleName.Substring(0, szModuleName.IndexOf((char)0));
        }

        return true;
    }

    /// <summary>
    /// Get the symbol name given an address or offset.
    /// </summary>
    /// <param name="szModule">Module name.</param>
    /// <param name="cbSymbolAddressOrOffset">Symbol address or module-relative offset.</param>
    /// <param name="szSymbolName">Receives the symbol name if succesful, otherwise null.</param>
    /// <param name="pdwSymbolDisplacement">Receives the displacement from the symbol.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public unsafe bool PdbSymbolName(string szModule, ulong cbSymbolAddressOrOffset, out string? szSymbolName, out uint pdwSymbolDisplacement)
    {
        szSymbolName = null;
        pdwSymbolDisplacement = 0;
        var data = new byte[260];
        fixed (void* pb = data)
        {
            var result = Vmmi.VMMDLL_PdbSymbolName(_handle, szModule, cbSymbolAddressOrOffset, pb, out pdwSymbolDisplacement);
            if (!result)
            {
                return false;
            }

            szSymbolName = Encoding.UTF8.GetString(data);
            szSymbolName = szSymbolName.Substring(0, szSymbolName.IndexOf((char)0));
        }

        return true;
    }

    /// <summary>
    /// Get the symbol address given a symbol name.
    /// </summary>
    /// <param name="szModule">Module name.</param>
    /// <param name="szSymbolName">Symbol name.</param>
    /// <param name="pvaSymbolAddress">Receives the symbol address on success.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public bool PdbSymbolAddress(string szModule, string szSymbolName, out ulong pvaSymbolAddress)
    {
        return Vmmi.VMMDLL_PdbSymbolAddress(_handle, szModule, szSymbolName, out pvaSymbolAddress);
    }

    /// <summary>
    /// Get the size of a type.
    /// </summary>
    /// <param name="szModule">Module name.</param>
    /// <param name="szTypeName">Type name.</param>
    /// <param name="pcbTypeSize">Receives the type size.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public bool PdbTypeSize(string szModule, string szTypeName, out uint pcbTypeSize)
    {
        return Vmmi.VMMDLL_PdbTypeSize(_handle, szModule, szTypeName, out pcbTypeSize);
    }

    /// <summary>
    /// Get the child offset of a type.
    /// </summary>
    /// <param name="szModule">Module name.</param>
    /// <param name="szTypeName">Type name.</param>
    /// <param name="wszTypeChildName">Child member name.</param>
    /// <param name="pcbTypeChildOffset">Receives the child offset within the type.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public bool PdbTypeChildOffset(string szModule, string szTypeName, string wszTypeChildName, out uint pcbTypeChildOffset)
    {
        return Vmmi.VMMDLL_PdbTypeChildOffset(_handle, szModule, szTypeName, wszTypeChildName, out pcbTypeChildOffset);
    }

    #endregion

    #region Utility functionality

    /// <summary>
    /// Enum used to specify the log level.
    /// </summary>
    public enum LogLevel : uint
    {
        /// <summary>Critical stopping error.</summary>
        Critical = 1, // critical stopping error
        /// <summary>Severe warning error.</summary>
        Warning = 2, // severe warning error
        /// <summary>Normal/info message.</summary>
        Info = 3, // normal/info message
        /// <summary>Verbose message (visible with -v).</summary>
        Verbose = 4, // verbose message (visible with -v)
        /// <summary>Debug message (visible with -vv).</summary>
        Debug = 5, // debug message (visible with -vv)
        /// <summary>Trace message.</summary>
        Trace = 6 // trace message
    }

    /// <summary>
    /// Log a string to the VMM log.
    /// </summary>
    /// <param name="message">The message to log.</param>
    /// <param name="logLevel">The log level (default INFO).</param>
    /// <param name="MID">Module ID (default = API).</param>
    public void Log(string message, LogLevel logLevel = LogLevel.Info, uint MID = 0x80000011)
    {
        Vmmi.VMMDLL_Log(_handle, MID, logLevel, "%s", message);
    }

    /// <summary>
    /// Register or unregister an optional memory access callback function.
    /// It's possible to have one callback function registered for each type.
    /// To clear an already registered callback function specify NULL as pfnCB.
    /// </summary>
    /// <remarks>
    /// The callback must be a static method marked with <see cref="UnmanagedCallersOnlyAttribute"/> returning void with signature:
    /// <c>(IntPtr ctxUser, uint dwPID, uint cpMEMs, LeechCore.MEM_SCATTER_NATIVE** ppMEMs)</c>
    /// </remarks>
    /// <param name="type">type of callback to register / unregister - VMMDLL_MEM_CALLBACK_*.</param>
    /// <param name="context">user context pointer to be passed to the callback function.</param>
    /// <param name="pfnCB">callback function pointer to register, or null to unregister.</param>
    /// <returns><see langword="true"/> if the callback was successfully registered/unregistered, otherwise <see langword="false"/>.</returns>
    public unsafe bool MemCallback(VmmMemCallbackType type, IntPtr context, delegate* unmanaged<IntPtr, uint, uint, LeechCore.MEM_SCATTER_NATIVE**, void> pfnCB)
    {
        return Vmmi.VMMDLL_MemCallback(_handle, type, context, pfnCB);
    }

    /// <summary>
    /// Throw an exception if memory writing is disabled.
    /// </summary>
    /// <exception cref="VmmException">Thrown when memory writing is disabled.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ThrowIfMemWritesDisabled()
    {
        if (!EnableMemoryWriting)
        {
            throw new VmmException("Memory Writing is Disabled! This operation may not proceed.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct VMMDLL_WIN_THUNKINFO_IAT
    {
        private int _fValid; // WIN32 BOOL
#pragma warning disable IDE1006 // Naming Styles
        public bool fValid
#pragma warning restore IDE1006 // Naming Styles
        {
            readonly get => _fValid != 0;
            set => _fValid = value ? 1 : 0;
        }
        private int _f32; // WIN32 BOOL
        /// <summary>
        /// if TRUE fn is a 32-bit/4-byte entry, otherwise 64-bit/8-byte entry.
        /// </summary>
#pragma warning disable IDE1006 // Naming Styles
        public bool f32
#pragma warning restore IDE1006 // Naming Styles
        {
            readonly get => _f32 != 0;
            set => _f32 = value ? 1 : 0;
        }
        /// <summary>
        /// address of import address table 'thunk'.
        /// </summary>        
        public ulong vaThunk;
        /// <summary>
        /// value if import address table 'thunk' == address of imported function.
        /// </summary>        
        public ulong vaFunction;
        /// <summary>
        /// address of name string for imported module.
        /// </summary>        
        public ulong vaNameModule;
        /// <summary>
        /// address of name string for imported function.
        /// </summary>
        public ulong vaNameFunction;
    }

    /// <summary>
    /// Retrieve information about the import address table IAT thunk for an imported
    /// function.This includes the virtual address of the IAT thunk which is useful
    /// for hooking.
    /// </summary>
    /// <param name="dwPID"></param>
    /// <param name="wszModuleName"></param>
    /// <param name="szImportModuleName"></param>
    /// <param name="szImportFunctionName"></param>
    /// <param name="pThunkInfoIAT"></param>
    /// <returns><see langword="true"/> if the call was successful, otherwise <see langword="false"/>.</returns>
    public bool WinGetThunkInfoIAT(uint dwPID, string wszModuleName, string szImportModuleName, string szImportFunctionName, out VMMDLL_WIN_THUNKINFO_IAT pThunkInfoIAT)
    {
        return Vmmi.VMMDLL_WinGetThunkInfoIATW(_handle, dwPID, wszModuleName, szImportModuleName, szImportFunctionName, out pThunkInfoIAT);
    }

    #endregion // Utility functionality

    #region Custom Refresh Functionality

    /// <summary>
    /// Force a full VMM refresh (equivalent to <see cref="VmmOption.REFRESH_ALL"/>).
    /// </summary>
    public void ForceFullRefresh()
    {
        if (!ConfigSet(VmmOption.REFRESH_ALL, 1))
        {
            Log("WARNING: Vmm Full Refresh Failed!", LogLevel.Warning);
        }
    }

    /// <summary>
    /// Register an auto-refresher with a specified interval.
    /// </summary>
    /// <remarks>
    /// Useful if initialized with -norefresh and you want to control refreshing more closely. Minimum interval resolution is ~10–15ms.
    /// </remarks>
    /// <param name="option">VMM refresh <see cref="RefreshOption"/>.</param>
    /// <param name="interval">Interval at which to fire a refresh operation.</param>
    public void RegisterAutoRefresh(RefreshOption option, TimeSpan interval)
    {
        RefreshManager.Register(this, option, interval);
    }

    /// <summary>
    /// Unregister an auto-refresher.
    /// </summary>
    /// <param name="option">Option to unregister.</param>
    public void UnregisterAutoRefresh(RefreshOption option)
    {
        RefreshManager.Unregister(this, option);
    }

    #endregion
}