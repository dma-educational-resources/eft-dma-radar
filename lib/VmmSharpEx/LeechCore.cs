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
using VmmSharpEx.Internal;
using VmmSharpEx.Options;

namespace VmmSharpEx;

/// <summary>
/// High-level managed wrapper over the native LeechCore API.
/// </summary>
/// <remarks>
/// This class wraps a native <c>LC_CONTEXT</c> handle created by LeechCore and exposes common read/write and control
/// operations against physical memory devices. It is typically acquired from <see cref="Vmm"/> via
/// <see cref="Vmm.LeechCore"/> when MemProcFS has been initialized with a LeechCore-backed device.
/// Native counterparts are defined in <c>leechcore.h</c> and implemented in <c>leechcore.dll</c>.
/// </remarks>
public sealed class LeechCore : IDisposable
{
    public static implicit operator LeechCore.Handle(LeechCore lc) => lc._handle;

    private readonly Vmm? _parent;
    private readonly LeechCore.Handle _handle;
    private bool _disposed;

    private LeechCore() { throw new NotImplementedException(); }

    private LeechCore(IntPtr hLC)
    {
        _handle = new LeechCore.Handle(handle: hLC);
    }

    /// <summary>
    /// Create a new inherited <see cref="LeechCore"/> instance from a given <see cref="Vmm"/> instance.
    /// </summary>
    /// <param name="vmm">The owning <see cref="Vmm"/> instance the LC context should be bound to.</param>
    /// <exception cref="VmmException">Thrown if the native LeechCore handle cannot be retrieved or duplicated.</exception>
    internal LeechCore(Vmm vmm)
    {
        if (vmm.ConfigGet(VmmOption.CORE_LEECHCORE_HANDLE) is not ulong pqwValue)
        {
            throw new VmmException("LeechCore: failed retrieving handle from Vmm.");
        }

        var cfg = new LCConfig
        {
            dwVersion = LC_CONFIG_VERSION,
            szDevice = $"existing://0x{pqwValue:X}"
        };
        var cfgNative = Marshal.AllocHGlobal(Marshal.SizeOf<LCConfig>());
        Marshal.StructureToPtr(cfg, cfgNative, false);
        try
        {
            var hLC = Lci.LcCreate(cfgNative);
            if (hLC == IntPtr.Zero)
            {
                throw new VmmException("LeechCore: failed to create object.");
            }

            _handle = new LeechCore.Handle(handle: hLC);
            _parent = vmm;
        }
        finally
        {
            Marshal.DestroyStructure<LCConfig>(cfgNative);
            Marshal.FreeHGlobal(cfgNative);
        }
    }

    /// <summary>
    /// Releases native resources.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true) == false)
        {
            _handle?.Dispose();
        }
    }

    /// <summary>
    /// Returns a string representation of this instance including the native handle value.
    /// </summary>
    public override string ToString()
    {
        return _disposed ? "LeechCore:Disposed" : $"LeechCore:{_handle:X}";
    }

    /// <summary>
    /// Factory that creates a new <see cref="LeechCore"/> object from a native <see cref="LCConfig"/>.
    /// </summary>
    /// <remarks>
    /// This overload provides access to extended create-time error information via
    /// <paramref name="configErrorInfo"/>. See native <c>LcCreateEx</c> in <c>leechcore.h</c>.
    /// </remarks>
    /// <param name="pLcCreateConfig">The LC configuration to use.</param>
    /// <param name="configErrorInfo">Receives extended create-time error information, if available.</param>
    /// <returns>An initialized <see cref="LeechCore"/> instance on success; otherwise <see langword="null"/>.</returns>
    public static unsafe LeechCore? Create(ref LCConfig pLcCreateConfig, out LCConfigErrorInfo configErrorInfo)
    {
        var cbERROR_INFO = Marshal.SizeOf<Lci.LC_CONFIG_ERRORINFO>();
        var pLcCreateConfigNative = Marshal.AllocHGlobal(Marshal.SizeOf<LCConfig>());
        Marshal.StructureToPtr(pLcCreateConfig, pLcCreateConfigNative, false);
        try
        {
            var hLC = Lci.LcCreateEx(pLcCreateConfigNative, out var pLcErrorInfo);
            configErrorInfo = new LCConfigErrorInfo
            {
                strUserText = ""
            };
            if (pLcErrorInfo != IntPtr.Zero && hLC != IntPtr.Zero)
            {
                return new LeechCore(hLC);
            }

            if (hLC != IntPtr.Zero)
            {
                Lci.LcClose(hLC);
            }

            if (pLcErrorInfo != IntPtr.Zero)
            {
                var e = Marshal.PtrToStructure<Lci.LC_CONFIG_ERRORINFO>(pLcErrorInfo);
                if (e.dwVersion == LC_CONFIG_ERRORINFO_VERSION)
                {
                    configErrorInfo.fValid = true;
                    configErrorInfo.fUserInputRequest = e.fUserInputRequest;
                    if (e.cwszUserText > 0)
                    {
                        configErrorInfo.strUserText = Marshal.PtrToStringUni(checked((IntPtr)(pLcErrorInfo.ToInt64() + cbERROR_INFO)));
                    }
                }

                Lci.LcMemFree(pLcErrorInfo);
            }

            return null;
        }
        finally
        {
            Marshal.DestroyStructure<LCConfig>(pLcCreateConfigNative);
            Marshal.FreeHGlobal(pLcCreateConfigNative);
        }
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
            Lci.LcClose(this.handle);
            return true;
        }
    }

    //---------------------------------------------------------------------
    // LEECHCORE: GENERAL FUNCTIONALITY BELOW:
    //---------------------------------------------------------------------

    /// <summary>
    /// Read memory from a physical address into a byte array.
    /// </summary>
    /// <remarks>
    /// NOTE: This method incurs a heap allocation for the returned byte array. For high-performance use other read methods instead.
    /// </remarks>
    /// <param name="pa">Physical address to read from.</param>
    /// <param name="cb">Count of bytes to read.</param>
    /// <returns>A byte array with the read memory, otherwise <see langword="null"/>.</returns>
    public unsafe byte[]? Read(ulong pa, uint cb)
    {
        var arr = new byte[cb];
        fixed (void* pb = arr)
        {
            if (!Lci.LcRead(_handle, pa, cb, pb))
            {
                return null;
            }
        }
        return arr;
    }

    /// <summary>
    /// Read physical memory into a value of type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">An unmanaged value or <see langword="struct"/>.</typeparam>
    /// <param name="pa">Physical address to read.</param>
    /// <param name="result">Receives the value read from memory on success.</param>
    /// <returns><see langword="true"/> if successful; otherwise <see langword="false"/>.</returns>
    /// <seealso cref="Lci.LcRead"/>
    public unsafe bool ReadValue<T>(ulong pa, out T result)
        where T : unmanaged, allows ref struct
    {
        uint cb = (uint)sizeof(T);
        result = default;
        fixed (void* pb = &result)
        {
            return Lci.LcRead(_handle, pa, cb, pb);
        }
    }

    /// <summary>
    /// Read physical memory into an array of <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// NOTE: This method incurs a heap allocation for the returned byte array. For high-performance use other read methods instead.
    /// </remarks>
    /// <typeparam name="T">An unmanaged value type.</typeparam>
    /// <param name="pa">Physical address to read.</param>
    /// <param name="count">Number of elements to read.</param>
    /// <returns>An array on success; otherwise <see langword="null"/>.</returns>
    public unsafe T[]? ReadArray<T>(ulong pa, int count)
        where T : unmanaged
    {
        if (count <= 0)
            return null;
        var arr = new T[count];
        uint cb = checked((uint)sizeof(T) * (uint)count);
        fixed (T* pb = arr)
        {
            if (!Lci.LcRead(_handle, pa, cb, pb))
            {
                return null;
            }
        }
        return arr;
    }

    /// <summary>
    /// Read physical memory into a pooled array of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">An unmanaged value type.</typeparam>
    /// <param name="pa">Physical address to read.</param>
    /// <param name="count">Number of elements to read.</param>
    /// <returns>A <see cref="IMemoryOwner{T}"/> lease on success; otherwise <see langword="null"/>. Be sure to call <see cref="IDisposable.Dispose()"/> when done.</returns>
    public unsafe IMemoryOwner<T>? ReadPooled<T>(ulong pa, int count)
        where T : unmanaged
    {
        if (count <= 0)
            return null;
        var arr = new PooledMemory<T>(count);
        uint cb = checked((uint)sizeof(T) * (uint)count);
        fixed (T* pb = arr.Span)
        {
            if (!Lci.LcRead(_handle, pa, cb, pb))
            {
                arr.Dispose();
                return null;
            }
        }
        return arr;
    }

    /// <summary>
    /// Read physical memory into a <see cref="Span{T}"/>.
    /// </summary>
    /// <typeparam name="T">An unmanaged value type.</typeparam>
    /// <param name="pa">Physical address to read.</param>
    /// <param name="span">Destination span to receive the data.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public unsafe bool ReadSpan<T>(ulong pa, Span<T> span)
        where T : unmanaged
    {
        uint cb = checked((uint)sizeof(T) * (uint)span.Length);
        fixed (T* pb = span)
        {
            return Lci.LcRead(_handle, pa, cb, pb);
        }
    }

    /// <summary>
    /// Write a <see cref="Span{T}"/> of unmanaged values to physical memory.
    /// </summary>
    /// <typeparam name="T">An unmanaged value type.</typeparam>
    /// <param name="pa">Physical address to write.</param>
    /// <param name="span">Source span that will be written.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public unsafe bool WriteSpan<T>(ulong pa, Span<T> span)
        where T : unmanaged
    {
        _parent?.ThrowIfMemWritesDisabled();
        uint cb = checked((uint)sizeof(T) * (uint)span.Length);
        fixed (T* pb = span)
        {
            return Lci.LcWrite(_handle, pa, cb, pb);
        }
    }

    /// <summary>
    /// Read physical memory into unmanaged memory.
    /// </summary>
    /// <param name="pa">Physical address to read.</param>
    /// <param name="pb">Destination pointer to receive the data.</param>
    /// <param name="cb">Number of bytes to read.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool Read(ulong pa, IntPtr pb, uint cb)
    {
        return Read(pa, pb.ToPointer(), cb);
    }

    /// <summary>
    /// Read physical memory into unmanaged memory.
    /// </summary>
    /// <param name="pa">Physical address to read.</param>
    /// <param name="pb">Destination pointer to receive the data.</param>
    /// <param name="cb">Number of bytes to read.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public unsafe bool Read(ulong pa, void* pb, uint cb)
    {
        if (!Lci.LcRead(_handle, pa, cb, pb))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Perform a scatter read of multiple page-sized physical memory ranges.
    /// </summary>
    /// <param name="pas">Page-aligned physical memory addresses.</param>
    /// <returns>Array of <see cref="MEM_SCATTER"/> results.</returns>
    /// <exception cref="VmmException">Thrown if the native scatter allocation fails.</exception>
    public unsafe MEM_SCATTER[] ReadScatter(params ReadOnlySpan<ulong> pas)
    {
        if (!Lci.LcAllocScatter1((uint)pas.Length, out var pppMEMs) || pppMEMs == IntPtr.Zero)
        {
            throw new VmmException("LcAllocScatter1 FAIL");
        }
        try
        {
            var mems = new MEM_SCATTER[pas.Length];
            var ppMEMs = (MEM_SCATTER_NATIVE**)pppMEMs.ToPointer();
            int i;
            for (i = 0; i < pas.Length; i++)
            {
                var pMEM = ppMEMs[i];
                if (pMEM is null)
                    continue;
                pMEM->qwA = pas[i] & ~0xffful;
                pMEM->cb = 0x1000;
            }

            Lci.LcReadScatter(_handle, (uint)pas.Length, pppMEMs);

            for (i = 0; i < pas.Length; i++)
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
    /// Write a single value of type <typeparamref name="T"/> to physical memory.
    /// </summary>
    /// <typeparam name="T">An unmanaged value or <see langword="struct"/>.</typeparam>
    /// <param name="pa">Physical address to write.</param>
    /// <param name="value">The value to write.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public unsafe bool WriteValue<T>(ulong pa, T value)
        where T : unmanaged, allows ref struct
    {
        _parent?.ThrowIfMemWritesDisabled();
        uint cb = (uint)sizeof(T);
        return Lci.LcWrite(_handle, pa, cb, &value);
    }

    /// <summary>
    /// Write a managed array of <typeparamref name="T"/> to physical memory.
    /// </summary>
    /// <typeparam name="T">An unmanaged value type.</typeparam>
    /// <param name="pa">Physical address to write.</param>
    /// <param name="data">The managed array to write.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public unsafe bool WriteArray<T>(ulong pa, T[] data)
        where T : unmanaged
    {
        _parent?.ThrowIfMemWritesDisabled();
        uint cb = checked((uint)sizeof(T) * (uint)data.Length);
        fixed (T* pb = data)
        {
            return Lci.LcWrite(_handle, pa, cb, pb);
        }
    }

    /// <summary>
    /// Write from unmanaged memory into physical memory.
    /// </summary>
    /// <param name="pa">Physical address to write.</param>
    /// <param name="pb">Source pointer to write from.</param>
    /// <param name="cb">Number of bytes to write.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public unsafe bool Write(ulong pa, IntPtr pb, uint cb)
    {
        return Write(pa, pb.ToPointer(), cb);
    }

    /// <summary>
    /// Write from unmanaged memory into physical memory.
    /// </summary>
    /// <param name="pa">Physical address to write.</param>
    /// <param name="pb">Source pointer to write from.</param>
    /// <param name="cb">Number of bytes to write.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public unsafe bool Write(ulong pa, void* pb, uint cb)
    {
        _parent?.ThrowIfMemWritesDisabled();
        return Lci.LcWrite(_handle, pa, cb, pb);
    }

    /// <summary>
    /// Retrieve a LeechCore option value via <see cref="Lci.GetOption"/>.
    /// </summary>
    /// <param name="fOption">The <see cref="LcOption"/> to query.</param>
    /// <returns>The option value on success; otherwise <see langword="null"/>.</returns>
    public ulong? GetOption(LcOption fOption)
    {
        if (!Lci.GetOption(_handle, fOption, out var pqwValue))
        {
            return null;
        }

        return pqwValue;
    }

    /// <summary>
    /// Set a LeechCore option value via <see cref="Lci.SetOption"/>.
    /// </summary>
    /// <param name="fOption">The <see cref="LcOption"/> to set.</param>
    /// <param name="qwValue">The value to assign.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public bool SetOption(LcOption fOption, ulong qwValue)
    {
        return Lci.SetOption(_handle, fOption, qwValue);
    }

    /// <summary>
    /// Send a command to LeechCore.
    /// </summary>
    /// <remarks>
    /// See native <c>LcCommand</c> in <c>leechcore.h</c>. The output buffer, if any, is owned by the caller and must be
    /// freed by the wrapper (handled internally).
    /// </remarks>
    /// <param name="fOption">The <see cref="LcCmd"/> to execute.</param>
    /// <param name="dataIn">Optional input data.</param>
    /// <param name="dataOut">Receives any output data returned by the command.</param>
    /// <returns><see langword="true"/> on success; otherwise <see langword="false"/>.</returns>
    public unsafe bool ExecuteCommand(LcCmd fOption, ReadOnlySpan<byte> dataIn, out byte[]? dataOut)
    {
        uint cbDataOut;
        IntPtr pbDataOut;
        dataOut = null;
        if (dataIn.IsEmpty)
        {
            if (!Lci.LcCommand(_handle, fOption, 0, null, out pbDataOut, out cbDataOut))
            {
                return false;
            }
        }
        else
        {
            fixed (void* pbDataIn = dataIn)
            {
                if (!Lci.LcCommand(_handle, fOption, (uint)dataIn.Length, pbDataIn, out pbDataOut, out cbDataOut))
                {
                    return false;
                }
            }
        }

        dataOut = new byte[cbDataOut];
        if (cbDataOut > 0)
        {
            var src = new ReadOnlySpan<byte>(pbDataOut.ToPointer(), checked((int)cbDataOut));
            src.CopyTo(dataOut);
            Lci.LcMemFree(pbDataOut);
        }

        return true;
    }

    #region Constants/Types

    //---------------------------------------------------------------------
    // LEECHCORE: CORE FUNCTIONALITY BELOW:
    //---------------------------------------------------------------------

    /// <summary>
    /// Current <see cref="LCConfig"/> structure version used by this wrapper.
    /// </summary>
    public const uint LC_CONFIG_VERSION = 0xc0fd0002;

    /// <summary>
    /// Current <see cref="LCConfigErrorInfo"/> structure version used by this wrapper.
    /// </summary>
    public const uint LC_CONFIG_ERRORINFO_VERSION = 0xc0fe0002;

    /// <summary>
    /// Current <see cref="MEM_SCATTER_NATIVE"/> structure version used by this wrapper.
    /// </summary>
    public const uint MEM_SCATTER_VERSION = 0xc0fe0002;

    /// <summary>
    /// Managed scatter descriptor mirroring <c>MEM_SCATTER</c> in <c>leechcore.h</c>.
    /// </summary>
    public readonly struct MEM_SCATTER
    {
        public readonly ulong qwA;
        public readonly bool f;
        public readonly byte[]? pb;

        internal MEM_SCATTER(ulong qwA, bool f, byte[] pb)
        {
            this.qwA = qwA;
            this.f = f;
            this.pb = pb;
        }
    }

    /// <summary>
    /// Native scatter descriptor mirroring <c>tdMEM_SCATTER</c> in <c>leechcore.h</c>.
    /// </summary>
    /// <remarks>
    /// This type is laid out for blittable interop.
    /// </remarks>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    public struct MEM_SCATTER_NATIVE
    {
        /// <summary>
        /// MEM_SCATTER_VERSION (internal).
        /// </summary>
        [FieldOffset(0)]
        private readonly uint version;
        [FieldOffset(4)]
        private readonly int _f; // WIN32 BOOL
        /// <summary>
        /// Indicates whether the entry contains valid data (<see langword="true"/>) or not.
        /// </summary>
#pragma warning disable IDE1006 // Naming Styles
        public readonly bool f => _f != 0;
#pragma warning restore IDE1006 // Naming Styles
        /// <summary>
        /// Page-aligned address associated with this scatter entry.
        /// </summary>
        [FieldOffset(8)]
        public ulong qwA;
        /// <summary>
        /// Pointer to the native buffer holding the page data.
        /// </summary>
        [FieldOffset(16)]
        public readonly IntPtr pb;
        /// <summary>
        /// Size of the read request in bytes.
        /// </summary>
        [FieldOffset(24)]
        public uint cb;
        /// <summary>
        /// Internal stack pointer (reserved).
        /// </summary>
        [FieldOffset(28)]
        private readonly uint iStack;
        /// <summary>
        /// Internal stack storage (reserved).
        /// </summary>
        [FieldOffset(32)]
        private unsafe fixed ulong vStack[12];

        /// <summary>
        /// A read-only view over the page contents pointed at by <see cref="pb"/>.
        /// </summary>
        /// <remarks>
        /// DANGER: Do not access this memory after the memory is freed via <see cref="Lci.LcMemFree(nint)"/>.
        /// </remarks>
        public readonly unsafe ReadOnlySpan<byte> Data => new(
            pointer: pb.ToPointer(),
            length: checked((int)cb));

        /// <summary>
        /// Transfers the Scatter Result from Native Memory to Managed Memory.
        /// </summary>
        /// <returns>Managed <see cref="MEM_SCATTER"/> struct.</returns>
        public readonly MEM_SCATTER ToManaged()
        {
            var pbManaged = new byte[cb];
            Data.CopyTo(pbManaged);
            return new MEM_SCATTER(
                qwA: qwA,
                f: f,
                pb: pbManaged);
        }
    }

    /// <summary>
    /// Managed representation of native <c>LC_CONFIG</c> used when creating a LeechCore context.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct LCConfig
    {
        /// <summary>
        /// Structure version. Must be set to <see cref="LC_CONFIG_VERSION"/>.
        /// </summary>
        public uint dwVersion;
        /// <summary>
        /// Printf verbosity level.
        /// </summary>
        public uint dwPrintfVerbosity;

        /// <summary>
        /// Device string, e.g. <c>fpga://...</c> or <c>existing://0xHANDLE</c>.
        /// </summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDevice;

        /// <summary>
        /// Remote target string, if applicable.
        /// </summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szRemote;

        /// <summary>
        /// Optional printf callback.
        /// </summary>
        public IntPtr pfn_printf_opt;
        /// <summary>
        /// Maximum physical address to use.
        /// </summary>
        public ulong paMax;
        /// <summary>
        /// If <see langword="true"/>, volatile mode is enabled.
        /// </summary>
        public bool fVolatile;
        /// <summary>
        /// If <see langword="true"/>, writes are allowed.
        /// </summary>
        public bool fWritable;
        /// <summary>
        /// If <see langword="true"/>, operates in remote mode.
        /// </summary>
        public bool fRemote;
        /// <summary>
        /// If <see langword="true"/>, disables compression in remote mode.
        /// </summary>
        public bool fRemoteDisableCompress;

        /// <summary>
        /// Optional device name.
        /// </summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDeviceName;
    }


    /// <summary>
    /// Extended create-time error information corresponding to native <c>LC_CONFIG_ERRORINFO</c>.
    /// </summary>
    public struct LCConfigErrorInfo
    {
        /// <summary>
        /// Indicates whether this structure contains valid data.
        /// </summary>
        public bool fValid;
        /// <summary>
        /// Indicates a user-input request was signalled by the native layer.
        /// </summary>
        public bool fUserInputRequest;
        /// <summary>
        /// Optional user text provided by the native layer.
        /// </summary>
        public string? strUserText;
    }

    #endregion
}