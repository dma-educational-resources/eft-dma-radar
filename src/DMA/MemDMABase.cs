// Global using removed - use eft_dma_radar.Tarkov.MemoryInterface instead
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Tarkov.Features.MemoryWrites;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Vmmsharp;

namespace eft_dma_radar.Common.DMA
{
    internal static class BaseMemoryHolder
    {
        private static MemDMABase _memory;
        /// <summary>
        /// Limited Singleton Instance for use in this satelite assembly.
        /// </summary>
        public static MemDMABase MemoryBase
        {
            get => _memory;
            internal set => _memory ??= value;
        }
    }
    /// <summary>
    /// DMA Memory Module.
    /// </summary>
    public abstract class MemDMABase
    {
        #region Init

        private const string _memoryMapFile = "mmap.txt";
        public const uint MAX_READ_SIZE = (uint)0x1000 * 1500;
        protected static readonly ManualResetEvent _syncProcessRunning = new(false);
        protected static readonly ManualResetEvent _syncInRaid = new(false);
        protected readonly Vmm _hVMM;
        protected bool _restartRadar;
        /// <summary>
        /// Current Process ID (PID).
        /// </summary>
        public ulong MonoBase { get; protected set; }
        public ulong UnityBase { get; protected set; }
        public VmmProcess Process { get; protected set; }
        public virtual bool Starting { get; }
        public virtual bool Ready { get; }
        public virtual bool InRaid { get; }
        public virtual bool IsOffline { get; }
        public virtual ulong LevelSettings { get; }
        public virtual bool RaidHasStarted => true;

        /// <summary>
        /// Set to TRUE to restart the Radar on the next game loop cycle.
        /// </summary>
        public bool RestartRadar
        {
            set
            {
                if (InRaid)
                    _restartRadar = value;
            }
        }

        /// <summary>
        /// Vmm Handle for this DMA Connection.
        /// </summary>
        public Vmm VmmHandle => _hVMM;

        private MemDMABase() { }

        protected MemDMABase(FpgaAlgo fpgaAlgo, bool useMemMap)
        {
            XMLogging.WriteLine("Initializing DMA...");
            /// Check MemProcFS Versions...
            var vmmVersion = FileVersionInfo.GetVersionInfo("vmm.dll").FileVersion;
            var lcVersion = FileVersionInfo.GetVersionInfo("leechcore.dll").FileVersion;
            string versions = $"Vmm Version: {vmmVersion}\n" +
                $"Leechcore Version: {lcVersion}";
            var initArgs = new string[] {
                "-norefresh",
                "-device",
                fpgaAlgo is FpgaAlgo.Auto ?
                    "fpga" : $"fpga://algo={(int)fpgaAlgo}",
                "-waitinitialize"};
            try
            {
                /// Begin Init...
                if (useMemMap && !File.Exists(_memoryMapFile))
                {
                    XMLogging.WriteLine("[DMA] No MemMap, attempting to generate...");
                    _hVMM = new Vmm(initArgs);
                    var map = _hVMM.MapMemoryAsString() ??
                        throw new Exception("Map_GetPhysMem FAIL");
                    var mapBytes = Encoding.ASCII.GetBytes(map);
                    if (!_hVMM.LeechCore.Command(LeechCore.LC_CMD_MEMMAP_SET, mapBytes, out _))
                        throw new Exception("LC_CMD_MEMMAP_SET FAIL");
                    File.WriteAllBytes(_memoryMapFile, mapBytes);
                }
                else
                {
                    if (useMemMap)
                    {
                        var mapArgs = new string[] { "-memmap", _memoryMapFile };
                        initArgs = initArgs.Concat(mapArgs).ToArray();
                    }
                    _hVMM = new Vmm(initArgs);
                }
                SetCustomVMMRefresh();
                BaseMemoryHolder.MemoryBase = this;
                XMLogging.WriteLine("DMA Initialized!");

                Process = _hVMM.Process("EscapeFromTarkov.exe");
            }
            catch (Exception ex)
            {
                throw new Exception(
                "DMA Initialization Failed!\n" +
                $"Reason: {ex.Message}\n" +
                $"{versions}\n\n" +
                "===TROUBLESHOOTING===\n" +
                "1. Reboot both your Game PC / Radar PC (This USUALLY fixes it).\n" +
                "2. Reseat all cables/connections and make sure they are secure.\n" +
                "3. Changed Hardware/Operating System on Game PC? Delete your mmap.txt and symbols folder.\n" +
                "4. Make sure all Setup Steps are completed (See DMA Setup Guide/FAQ for additional troubleshooting).");
            }
        }

        #endregion

        #region VMM Refresh

        private readonly System.Timers.Timer _memCacheRefreshTimer = new(TimeSpan.FromMilliseconds(300));
        private readonly System.Timers.Timer _tlbRefreshTimer = new(TimeSpan.FromSeconds(2));

        /// <summary>
        /// Sets Custom VMM Refresh Timers. Be sure to FULL refresh when outside of a raid.
        /// </summary>
        private void SetCustomVMMRefresh()
        {
            _memCacheRefreshTimer.Elapsed += memCacheRefreshTimer_Elapsed;
            _tlbRefreshTimer.Elapsed += tlbRefreshTimer_Elapsed;
            _memCacheRefreshTimer.Start();
            _tlbRefreshTimer.Start();
        }

        private void memCacheRefreshTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!_hVMM.SetConfig(Vmm.CONFIG_OPT_REFRESH_FREQ_MEM_PARTIAL, 1))
                XMLogging.WriteLine("WARNING: Vmm MEM CACHE Refresh (Partial) Failed!");
        }

        private void tlbRefreshTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (!_hVMM.SetConfig(Vmm.CONFIG_OPT_REFRESH_FREQ_TLB_PARTIAL, 1))
                XMLogging.WriteLine("WARNING: Vmm TLB Refresh (Partial) Failed!");
        }

        /// <summary>
        /// Manually Force a Full Vmm Refresh.
        /// </summary>
        public void FullRefresh()
        {
            if (!_hVMM.SetConfig(Vmm.CONFIG_OPT_REFRESH_ALL, 1))
                XMLogging.WriteLine("WARNING: Vmm FULL Refresh Failed!");
        }

        #endregion

        #region Events

        /// <summary>
        /// Raised when the game process is successfully started.
        /// Outside Subscribers should handle exceptions!
        /// </summary>
        public static event EventHandler<EventArgs> GameStarted;
        /// <summary>
        /// Raised when the game process is no longer running.
        /// Outside Subscribers should handle exceptions!
        /// </summary>
        public static event EventHandler<EventArgs> GameStopped;
        /// <summary>
        /// Raised when a raid starts.
        /// Outside Subscribers should handle exceptions!
        /// </summary>
        public static event EventHandler<EventArgs> RaidStarted;
        /// <summary>
        /// Raised when a raid ends.
        /// Outside Subscribers should handle exceptions!
        /// </summary>
        public static event EventHandler<EventArgs> RaidStopped;

        /// <summary>
        /// Raises the GameStarted Event.
        /// </summary>
        protected static void OnGameStarted()
        {
            GameStarted?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Raises the GameStopped Event.
        /// </summary>
        protected static void OnGameStopped()
        {
            GameStopped?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Raises the RaidStarted Event.
        /// </summary>
        protected static void OnRaidStarted()
        {
            RaidStarted?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Raises the RaidStopped Event.
        /// </summary>
        protected static void OnRaidStopped()
        {
            RaidStopped?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>
        /// Blocks indefinitely until the Game Process is Running, otherwise returns immediately.
        /// </summary>
        /// <returns>True if the Process is running, otherwise this method never returns.</returns>
        public static bool WaitForProcess() => _syncProcessRunning.WaitOne();

        /// <summary>
        /// Blocks indefinitely until In Raid/Match, otherwise returns immediately.
        /// </summary>
        /// <returns>True if In Raid/Match, otherwise this method never returns.</returns>
        public static bool WaitForRaid() => _syncInRaid.WaitOne();

        #endregion

        #region ScatterRead

        /// <summary>
        /// Performs multiple reads in one sequence, significantly faster than single reads.
        /// Designed to run without throwing unhandled exceptions, which will ensure the maximum amount of
        /// reads are completed OK even if a couple fail.
        /// </summary>
        public void ReadScatter(IScatterEntry[] entries, bool useCache = true)
        {
            if (entries.Length == 0)
                return;
            var pagesToRead = new HashSet<ulong>(entries.Length); // Will contain each unique page only once to prevent reading the same page multiple times
            foreach (var entry in entries) // First loop through all entries - GET INFO
            {
                // INTEGRITY CHECK - Make sure the read is valid and within range
                if (entry.Address == 0x0 || entry.CB == 0 || (uint)entry.CB > MAX_READ_SIZE)
                {
                    //XMLogging.WriteLine($"[Scatter Read] Out of bounds read @ 0x{entry.Address.ToString("X")} ({entry.CB})");
                    entry.IsFailed = true;
                    continue;
                }

                // get the number of pages
                uint numPages = ADDRESS_AND_SIZE_TO_SPAN_PAGES(entry.Address, (uint)entry.CB);
                ulong basePage = PAGE_ALIGN(entry.Address);

                //loop all the pages we would need
                for (int p = 0; p < numPages; p++)
                {
                    ulong page = basePage + 0x1000 * (uint)p;
                    pagesToRead.Add(page);
                }
            }
            if (pagesToRead.Count == 0)
                return;

            uint flags = useCache ? 0 : Vmm.FLAG_NOCACHE;
            using var hScatter = Process.MemReadScatter2(flags, pagesToRead.ToArray());
            

            foreach (var entry in entries) // Second loop through all entries - PARSE RESULTS
            {
                if (entry.IsFailed)
                    continue;
                entry.SetResult(hScatter);
            }
        }

        #endregion

        #region ReadMethods

        /// <summary>
        /// Prefetch pages into the cache.
        /// </summary>
        /// <param name="va"></param>
        public void ReadCache(params ulong[] va)
        {
            Process.MemPrefetchPages(va);
        }

        /// <summary>
        /// Read memory into a Buffer of type <typeparamref name="T"/>
        /// </summary>
        /// <typeparam name="T">Value Type <typeparamref name="T"/></typeparam>
        /// <param name="addr">Virtual Address to read from.</param>
        /// <param name="buffer">Buffer to receive memory read in.</param>
        /// <param name="useCache">Use caching for this read.</param>
        public unsafe void ReadBuffer<T>(ulong addr, Span<T> buffer, bool useCache = true, bool allowPartialRead = false)
            where T : unmanaged
        {
            uint cb = (uint)(SizeChecker<T>.Size * buffer.Length);
            try
            {
                uint flags = useCache ? 0 : Vmm.FLAG_NOCACHE;

                if (!Process.MemReadSpan(addr, buffer, out uint cbRead, flags))
                    throw new VmmException("Memory Read Failed!");

                if (cbRead == 0)
                    throw new VmmException("Memory Read Failed!");
                if (!allowPartialRead && cbRead != cb)
                    throw new VmmException("Memory Read Failed!");
            }
            catch (VmmException)
            {
                
                throw;
            }
        }
         /// <summary>
        /// Read an array of type <typeparamref name="T"/> from memory.
        /// The first element begins reading at 0x0 and the array is assumed to be contiguous.
        /// IMPORTANT: You must call <see cref="IDisposable.Dispose"/> on the returned SharedArray when done."/>
        /// </summary>
        /// <typeparam name="T">Value type to read.</typeparam>
        /// <param name="addr">Address to read from.</param>
        /// <param name="count">Number of array elements to read.</param>
        /// <param name="useCache">Use caching for this read.</param>
        /// <returns><see cref="PooledMemory{T}"/> value. Be sure to call <see cref="IDisposable.Dispose"/>!</returns>
        public T[] ReadArray<T>(ulong addr, int count, bool useCache = true)
            where T : unmanaged
        {
            if (count <= 0)
                return Array.Empty<T>();

            T[] result = new T[count];
            ReadBuffer(addr, result.AsSpan(), useCache, false);
            return result;
        }
        /// <summary>
        /// Read memory into a buffer.
        /// </summary>
        public byte[] ReadBuffer(ulong addr, int size, bool useCache = true, bool allowIncompleteRead = false)
        {
            try
            {
                uint flags = useCache ? 0 : Vmm.FLAG_NOCACHE;
                var buf = Process.MemRead(addr, (uint)size, flags);
                if (!allowIncompleteRead && buf.Length != size)
                    throw new Exception("Incomplete memory read!");
                return buf;
            }
            catch (Exception ex)
            {
                throw new Exception($"[DMA] ERROR reading buffer at 0x{addr:X}", ex);
            }
        }        
        /// <summary>
        /// Read memory into a Buffer of type <typeparamref name="T"/> and ensure the read is correct.
        /// </summary>
        /// <typeparam name="T">Value Type <typeparamref name="T"/></typeparam>
        /// <param name="addr">Virtual Address to read from.</param>
        /// <param name="buffer1">Buffer to receive memory read in.</param>
        /// <param name="useCache">Use caching for this read.</param>
        public unsafe void ReadBufferEnsure<T>(ulong addr, Span<T> buffer1)
            where T : unmanaged
        {
            uint cb = (uint)(SizeChecker<T>.Size * buffer1.Length);
            try
            {
                var buffer2 = new T[buffer1.Length].AsSpan();
                var buffer3 = new T[buffer1.Length].AsSpan();
                uint cbRead;

                if (!Process.MemReadSpan(addr, buffer3, out cbRead, Vmm.FLAG_NOCACHE))
                    throw new VmmException("Memory Read Failed!");

                if (cbRead != cb)
                    throw new VmmException("Memory Read Failed!");

                Thread.SpinWait(5);

                if (!Process.MemReadSpan(addr, buffer2, out cbRead, Vmm.FLAG_NOCACHE))
                    throw new VmmException("Memory Read Failed!");

                if (cbRead != cb)
                    throw new VmmException("Memory Read Failed!");

                Thread.SpinWait(5);

                if (!Process.MemReadSpan(addr, buffer1, out cbRead, Vmm.FLAG_NOCACHE))
                    throw new VmmException("Memory Read Failed!");

                if (cbRead != cb)
                    throw new VmmException("Memory Read Failed!");
                if (!buffer1.SequenceEqual(buffer2) || !buffer1.SequenceEqual(buffer3) || !buffer2.SequenceEqual(buffer3))
                {
                    throw new VmmException("Memory Read Failed!");
                }
            }
            catch (VmmException)
            {
                
                throw;
            }
        }
        /// <summary>
        /// Read memory into a buffer and validate the right bytes were received.
        /// </summary>
        public static unsafe byte[] ReadBufferEnsureE(ulong addr, int size)
        {
            const int ValidationCount = 3;
        
            try
            {
                if (BaseMemoryHolder.MemoryBase == null)
                    throw new Exception("[DMA] BaseMemoryHolder.MemoryBase is not initialized!");
        
                byte[][] buffers = new byte[ValidationCount][];
                for (int i = 0; i < ValidationCount; i++)
                {
                    buffers[i] = new byte[size];
                    fixed (byte* bufferPtr = buffers[i])
                    {
                        uint bytesRead;
                        bool success = BaseMemoryHolder.MemoryBase.Process.MemRead(
                            addr,                          // memory address
                            (nint)bufferPtr,               // pointer to buffer
                            (uint)size,                    // size to read
                            out bytesRead,                 // actual bytes read
                            Vmm.FLAG_NOCACHE               // no cache flag
                        );
        
                        if (!success || bytesRead != size)
                            throw new Exception($"Incomplete memory read ({bytesRead}/{size}) at 0x{addr:X}");
                    }
                }
        
                // Validation: ensure all reads match
                for (int i = 1; i < ValidationCount; i++)
                {
                    if (!buffers[i].SequenceEqual(buffers[0]))
                    {
                        XMLogging.WriteLine($"[WARN] ReadBufferEnsure() -> 0x{addr:X} failed memory consistency check.");
                        return null;
                    }
                }
        
                return buffers[0];
            }
            catch (Exception ex)
            {
                throw new Exception($"[DMA] ERROR reading buffer at 0x{addr:X}", ex);
            }
        }


        /// <summary>
        /// Read a chain of pointers and get the final result.
        /// </summary>
        public ulong ReadPtrChain(ulong addr, uint[] offsets, bool useCache = true)
        {
            var pointer = addr; // push ptr to first address value
            for (var i = 0; i < offsets.Length; i++)
                pointer = ReadPtr(pointer + offsets[i], useCache);

            return pointer;
        }

        /// <summary>
        /// Resolves a pointer and returns the memory address it points to.
        /// </summary>
        public ulong ReadPtr(ulong addr, bool useCache = true)
        {
            var pointer = ReadValue<ulong>(addr, useCache);
            pointer.ThrowIfInvalidVirtualAddress();
            return pointer;
        }
        public unsafe T Read<T>(ulong address) where T : unmanaged
        {
            var size = (uint)Unsafe.SizeOf<T>();
            var bytes = Process.MemRead(address, size);
            if (bytes == null || bytes.Length != size)
                throw new ArgumentException($"Failed to read {typeof(T).Name} from 0x{address:X}");

            unsafe
            {
                fixed (byte* ptr = bytes)
                {
                    return *(T*)ptr;
                }
            }
        }
        /// <summary>
        /// Read null terminated UTF8 string.
        /// </summary>
        public string ReadUtf8String(ulong addr, int cb, bool useCache = true) // read n bytes (string)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(cb, 0x1000, nameof(cb));
            uint flags = useCache ? 0 : Vmm.FLAG_NOCACHE;
            return Process.MemReadString(Encoding.UTF8, addr, (uint)cb, flags) ??
                throw new VmmException("Memory Read Failed!");
        }        
        /// <summary>
        /// Read value type/struct from specified address.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to read from.</param>
        public unsafe T ReadValue<T>(ulong addr, bool useCache = true)
            where T : unmanaged, allows ref struct
        {
            try
            {
                uint flags = useCache ? 0 : Vmm.FLAG_NOCACHE;
                byte[] data = Process.MemRead(addr, (uint)sizeof(T), flags);

                if (data.Length != sizeof(T))
                    throw new VmmException("Memory Read Failed!");

                T result;
                fixed (byte* ptr = data)
                    result = *(T*)ptr;

                return result;
            }
            catch (VmmException)
            {
                throw;
            }
        }
        
        public ulong FindDataXref(
            ulong targetAddress,
            string moduleName = "UnityPlayer.dll",
            int searchRange = 0x4000)
        {
            if (targetAddress == 0)
                return 0;
        
            ulong moduleBase = Process.GetModuleBase(moduleName);
            if (moduleBase == 0 || moduleBase == ulong.MaxValue)
                return 0;
        
            // Scan forward from the string location
            ulong scanStart = targetAddress & ~0xFFFUL; // page-align
            ulong scanEnd   = scanStart + (ulong)searchRange;
        
            byte[] buffer;
            try
            {
                buffer = Process.MemRead(
                    scanStart,
                    (uint)searchRange,
                    Vmm.FLAG_NOCACHE);
            }
            catch
            {
                return 0;
            }
        
            for (int i = 0; i <= buffer.Length - 8; i += 8)
            {
                ulong value = BitConverter.ToUInt64(buffer, i);
                if (value == targetAddress)
                {
                    return scanStart + (ulong)i;
                }
            }
        
            return 0;
        }

        /// <summary>
        /// Read byref value type/struct from specified address.
        /// Result returned byref.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to read from.</param>
        public unsafe void ReadValue<T>(ulong addr, out T result, bool useCache = true)
            where T : unmanaged, allows ref struct
        {
            try
            {
                uint flags = useCache ? 0 : Vmm.FLAG_NOCACHE;
                byte[] data = Process.MemRead(addr, (uint)sizeof(T), flags);

                if (data.Length != sizeof(T))
                    throw new VmmException("Memory Read Failed!");

                fixed (byte* ptr = data)
                    result = *(T*)ptr;
            }
            catch (VmmException)
            {
                throw;
            }
        }

        /// <summary>
        /// Read value type/struct from specified address multiple times to ensure the read is correct.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to read from.</param>
        public unsafe T ReadValueEnsure<T>(ulong addr)
            where T : unmanaged, allows ref struct
        {
            int cb = sizeof(T);
            try
            {
                byte[] data1 = Process.MemRead(addr, (uint)cb, Vmm.FLAG_NOCACHE);
                if (data1.Length != cb)
                    throw new VmmException("Memory Read Failed!");

                T r1;
                fixed (byte* ptr1 = data1)
                    r1 = *(T*)ptr1;

                Thread.SpinWait(5);

                byte[] data2 = Process.MemRead(addr, (uint)cb, Vmm.FLAG_NOCACHE);
                if (data2.Length != cb)
                    throw new VmmException("Memory Read Failed!");

                T r2;
                fixed (byte* ptr2 = data2)
                    r2 = *(T*)ptr2;

                Thread.SpinWait(5);

                byte[] data3 = Process.MemRead(addr, (uint)cb, Vmm.FLAG_NOCACHE);
                if (data3.Length != cb)

                    throw new VmmException("Memory Read Failed!");

                T r3;
                fixed (byte* ptr3 = data3)
                    r3 = *(T*)ptr3;

                var b1 = new ReadOnlySpan<byte>(&r1, cb);
                var b2 = new ReadOnlySpan<byte>(&r2, cb);
                var b3 = new ReadOnlySpan<byte>(&r3, cb);
                if (!b1.SequenceEqual(b2) || !b1.SequenceEqual(b3) || !b2.SequenceEqual(b3))
                    throw new VmmException("Memory Read Failed!");

                return r1;
            }
            catch (VmmException)
            {
                throw;
            }
        }

        /// <summary>
        /// Read byref value type/struct from specified address multiple times to ensure the read is correct.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to read from.</param>
        public unsafe void ReadValueEnsure<T>(ulong addr, out T result)
            where T : unmanaged, allows ref struct
        {
            int cb = sizeof(T);
            try
            {
                byte[] data1 = Process.MemRead(addr, (uint)cb, Vmm.FLAG_NOCACHE);
                if (data1.Length != cb)
                    throw new VmmException("Memory Read Failed!");

                T r1;
                fixed (byte* ptr1 = data1)
                    r1 = *(T*)ptr1;

                Thread.SpinWait(5);

                byte[] data2 = Process.MemRead(addr, (uint)cb, Vmm.FLAG_NOCACHE);
                if (data2.Length != cb)
                    throw new VmmException("Memory Read Failed!");

                T r2;
                fixed (byte* ptr2 = data2)
                    r2 = *(T*)ptr2;

                Thread.SpinWait(5);

                byte[] data3 = Process.MemRead(addr, (uint)cb, Vmm.FLAG_NOCACHE);
                if (data3.Length != cb)
                    throw new VmmException("Memory Read Failed!");

                T r3;
                fixed (byte* ptr3 = data3)
                    r3 = *(T*)ptr3;

                var b1 = new ReadOnlySpan<byte>(&r1, cb);
                var b2 = new ReadOnlySpan<byte>(&r2, cb);
                var b3 = new ReadOnlySpan<byte>(&r3, cb);
                
                if (!b1.SequenceEqual(b2) || !b1.SequenceEqual(b3) || !b2.SequenceEqual(b3))
                    throw new VmmException("Memory Read Failed!");

                result = r1;
            }
            catch (VmmException)
            {
                throw;
            }
        }
        public bool TryReadValueEnsure<T>(ulong addr, out T result) where T : unmanaged
        {
            try
            {
                ReadValueEnsure(addr, out result);
                return true;
            }
            catch
            {
                result = default;
                return false;
            }
        }
        /// <summary>
        /// Read null terminated string (utf-8/default).
        /// </summary>
        /// <param name="length">Number of bytes to read.</param>
        /// <exception cref="Exception"></exception>
        public string ReadString(ulong addr, int length, bool useCache = true) // read n bytes (string)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, (int)0x1000, nameof(length));
            Span<byte> buffer = stackalloc byte[length];
            buffer.Clear();
            ReadBuffer(addr, buffer, useCache, true);
            var nullIndex = buffer.IndexOf((byte)0);
            return nullIndex >= 0
                ? Encoding.UTF8.GetString(buffer.Slice(0, nullIndex))
                : Encoding.UTF8.GetString(buffer);
        }
        /// <summary>
        /// Read UnityEngineString structure
        /// </summary>
        public string ReadUnityString(ulong addr, int length = 64, bool useCache = true)
        {
            if (length % 2 != 0)
                length++;
            length *= 2; // Unicode 2 bytes per char
            ArgumentOutOfRangeException.ThrowIfGreaterThan(length, (int)0x1000, nameof(length));
            Span<byte> buffer = stackalloc byte[length];
            buffer.Clear();
            ReadBuffer(addr + 0x14, buffer, useCache, true);
            var nullIndex = buffer.FindUtf16NullTerminatorIndex();
            return nullIndex >= 0
                ? Encoding.Unicode.GetString(buffer.Slice(0, nullIndex))
                : Encoding.Unicode.GetString(buffer);
        }

        /// <summary>
        /// Searches for a pattern signature within a specific module.
        /// Convenience overload for module-based scanning.
        /// </summary>
        /// <param name="signature">Pattern signature in the format "AA BB ?? DD" where ?? represents a wildcard.</param>
        /// <param name="moduleName">Name of the module to search (e.g., "UnityPlayer.dll")</param>
        /// <returns>Address where the pattern was found, or 0 if not found.</returns>
        public ulong FindSignature(string signature, string moduleName)
        {
            try
            {
                var moduleBase = Process?.GetModuleBase(moduleName) ?? 0;
                if (moduleBase == 0 || moduleBase == ulong.MaxValue)
                {
                    XMLogging.WriteLine($"[Signature] Module {moduleName} not found");
                    return 0;
                }

                // IL2CPP GameAssembly.dll can be 150-200+ MB, search in chunks
                // Search up to 200MB to cover most IL2CPP builds
                const ulong MAX_SEARCH_SIZE = 0xC800000; // 200MB
                const ulong CHUNK_SIZE = 0x1000000; // 16MB chunks for DMA reads
                
                ulong rangeEnd = moduleBase + MAX_SEARCH_SIZE;
                
                // Search in chunks to avoid DMA read limits
                for (ulong chunkStart = moduleBase; chunkStart < rangeEnd; chunkStart += CHUNK_SIZE - 0x100)
                {
                    ulong chunkEnd = Math.Min(chunkStart + CHUNK_SIZE, rangeEnd);
                    var result = FindSignature(signature, chunkStart, chunkEnd, Process);
                    if (result != 0)
                        return result;
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[Signature] Error searching module {moduleName}: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Searches for a pattern signature in memory within the specified address range.
        /// </summary>
        /// <param name="signature">Pattern signature in the format "AA BB ?? DD" where ?? represents a wildcard.</param>
        /// <param name="rangeStart">Start address of the search range.</param>
        /// <param name="rangeEnd">End address of the search range.</param>
        /// <param name="process">The process to read memory of.</param>
        /// <returns>Address where the pattern was found, or 0 if not found.</returns>
        public ulong FindSignature(string signature, ulong rangeStart, ulong rangeEnd, VmmProcess process)
        {
            if (string.IsNullOrEmpty(signature) || rangeStart >= rangeEnd)
                return 0;

            try
            {
                // Read the memory block to search within
                byte[] buffer = process.MemRead(rangeStart, (uint)(rangeEnd - rangeStart), Vmm.FLAG_NOCACHE);

                if (buffer.Length == 0)
                    return 0;

                string pat = signature;
                ulong firstMatch = 0;

                for (ulong i = 0; i < (ulong)buffer.Length; i++)
                {
                    if (pat[0] == '?' || buffer[i] == GetByte(pat.Substring(0, 2)))
                    {
                        if (firstMatch == 0)
                            firstMatch = rangeStart + i;

                        if (pat.Length <= 2)
                            break;

                        pat = pat.Substring(pat[0] == '?' ? 2 : 3);
                    }
                    else
                    {
                        pat = signature;
                        firstMatch = 0;
                    }
                }

                return firstMatch;
            }
            catch (VmmException ex)
            {
                XMLogging.WriteLine($"[DMA] Error in FindSignature: {ex.Message}");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[DMA] Error in FindSignature: {ex.Message}");
            }

            return 0;
        }

        /// <summary>
        /// Converts a hex string to a byte value.
        /// </summary>
        private byte GetByte(string hex)
        {
            if (hex.Length < 2)
                return 0;

            byte value = 0;
            byte.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out value);
            return value;
        }
        #endregion

        #region WriteMethods

        /// <summary>
        /// Write value type/struct to specified address, and ensure it is written.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to write to.</param>
        /// <param name="value">Value to write.</param>
        public unsafe void WriteValueEnsure<T>(ulong addr, T value)
            where T : unmanaged, allows ref struct
        {
            int cb = sizeof(T);
            try
            {
                var b1 = new ReadOnlySpan<byte>(&value, cb);
                const int retryCount = 3;
                for (int i = 0; i < retryCount; i++)
                {
                    try
                    {
                        WriteValue(addr, value);
                        Thread.SpinWait(5);
                        T temp = ReadValue<T>(addr, false);
                        var b2 = new ReadOnlySpan<byte>(&temp, cb);
                        if (b1.SequenceEqual(b2))
                        {
                            return; // SUCCESS
                        }
                    }
                    catch { }
                }
                throw new VmmException("Memory Write Failed!");
            }
            catch (VmmException)
            {
                throw;
            }
        }

        /// <summary>
        /// Write byref value type/struct to specified address, and ensure it is written.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to write to.</param>
        /// <param name="value">Value to write.</param>
        public unsafe void WriteValueEnsure<T>(ulong addr, ref T value)
            where T : unmanaged, allows ref struct
        {
            int cb = sizeof(T);
            try
            {
                fixed (void* pb = &value)
                {
                    var b1 = new ReadOnlySpan<byte>(pb, cb);
                    const int retryCount = 3;
                    for (int i = 0; i < retryCount; i++)
                    {
                        try
                        {
                            WriteValue(addr, ref value);
                            Thread.SpinWait(5);
                            T temp = ReadValue<T>(addr, false);
                            var b2 = new ReadOnlySpan<byte>(&temp, cb);
                            if (b1.SequenceEqual(b2))
                            {
                                return; // SUCCESS
                            }
                        }
                        catch { }
                    }
                    throw new VmmException("Memory Write Failed!");
                }
            }
            catch (VmmException)
            {
                throw;
            }
        }
        public unsafe bool TryWriteValueEnsure<T>(ulong addr, ref T value)
            where T : unmanaged
        {
            int cb = sizeof(T);
            try
            {
                fixed (void* pb = &value)
                {
                    var b1 = new ReadOnlySpan<byte>(pb, cb);
                    const int retryCount = 3;
                    for (int i = 0; i < retryCount; i++)
                    {
                        try
                        {
                            WriteValue(addr, ref value);
                            Thread.SpinWait(5);
                            T temp = ReadValue<T>(addr, false);
                            var b2 = new ReadOnlySpan<byte>(&temp, cb);
                            if (b1.SequenceEqual(b2))
                                return true;
                        }
                        catch { }
                    }
                }
            }
            catch (VmmException)
            {
            }
            return false;
        }

        /// <summary>
        /// Write value type/struct to specified address.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to write to.</param>
        /// <param name="value">Value to write.</param>
        public unsafe void WriteValue<T>(ulong addr, T value)
            where T : unmanaged, allows ref struct
        {
            if (!SharedProgram.Config?.MemWritesEnabled ?? false)
                throw new Exception("Memory Writing is Disabled!");

            try
            {
                int size = sizeof(T);
                byte[] buffer = new byte[size];
                fixed (byte* bufferPtr = buffer)
                    *(T*)bufferPtr = value;

                if (!Process.MemWrite(addr, buffer))
                    throw new VmmException("Memory Write Failed!");
            }
            catch (VmmException)
            {
                throw;
            }
        }

        /// <summary>
        /// Write byref value type/struct to specified address.
        /// </summary>
        /// <typeparam name="T">Specified Value Type.</typeparam>
        /// <param name="addr">Address to write to.</param>
        /// <param name="value">Value to write.</param>
        public unsafe void WriteValue<T>(ulong addr, ref T value)
            where T : unmanaged, allows ref struct
        {
            if (!SharedProgram.Config?.MemWritesEnabled ?? false)
                throw new Exception("Memory Writing is Disabled!");

            try
            {
                int size = sizeof(T);
                byte[] buffer = new byte[size];
                fixed (byte* bufferPtr = buffer)
                    *(T*)bufferPtr = value;

                if (!Process.MemWrite(addr, buffer))
                    throw new VmmException("Memory Write Failed!");
            }
            catch (VmmException)
            {
                throw;
            }
        }

        /// <summary>
        /// Write byte array buffer to Memory Address.
        /// </summary>
        /// <param name="addr">Address to write to.</param>
        /// <param name="buffer">Buffer to write.</param>
        public unsafe void WriteBuffer<T>(ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            if (!SharedProgram.Config?.MemWritesEnabled ?? false)
                throw new Exception("Memory Writing is Disabled!");
            try
            {
                if (!Process.MemWriteSpan(addr, buffer))
                    throw new VmmException("Memory Write Failed!");
            }
            catch (VmmException)
            {
                throw;
            }
        }

        /// <summary>
        /// Write a buffer to the specified address and validate the right bytes were written.
        /// </summary>
        /// <param name="addr">Address to write to.</param>
        /// <param name="buffer">Buffer to write.</param>
        public void WriteBufferEnsure<T>(ulong addr, Span<T> buffer)
            where T : unmanaged
        {
            int cb = SizeChecker<T>.Size * buffer.Length;
            try
            {
                Span<byte> temp = cb > 0x1000 ? new byte[cb] : stackalloc byte[cb];
                ReadOnlySpan<byte> b1 = MemoryMarshal.Cast<T, byte>(buffer);
                const int retryCount = 3;
                for (int i = 0; i < retryCount; i++)
                {
                    try
                    {
                        WriteBuffer(addr, buffer);
                        Thread.SpinWait(5);
                        temp.Clear();
                        ReadBuffer(addr, temp, false, false);
                        if (temp.SequenceEqual(b1))
                        {
                            return; // SUCCESS
                        }
                    }
                    catch { }
                }
                throw new VmmException("Memory Write Failed!");
            }
            catch (VmmException)
            {
                throw;
            }
        }
        /// <summary>
        /// Write a buffer to the specified address and validate the right bytes were written.
        /// </summary>
        /// <param name="addr">Address to write to.</param>
        /// <param name="buffer">Buffer to write.</param>
        public bool WriteBufferEnsureB(ulong addr, byte[] buffer)
        {
            const int RetryCount = 3;

            try
            {
                bool success = false;
                for (int i = 0; i < RetryCount; i++)
                {
                    WriteBuffer<byte>(addr, buffer);

                    // Validate the bytes were written properly
                    var validateBytes = ReadBufferEnsureE(addr, buffer.Length);

                    if (validateBytes is null || !validateBytes.SequenceEqual(buffer))
                    {
                        XMLogging.WriteLine($"[WARN] WriteBufferEnsure() -> 0x{addr:X} did not pass validation on try {i + 1}!");
                        success = false;
                        continue;
                    }

                    success = true;
                    break;
                }

                return success;
            }
            catch (Exception ex)
            {
                throw new Exception($"[DMA] ERROR writing bytes at 0x{addr:X}", ex);
            }
        }
        #endregion

        #region Misc

        /// <summary>
        /// Get an Export from this process.
        /// </summary>
        /// <param name="module"></param>
        /// <param name="name"></param>
        /// <returns></returns>
        public ulong GetExport(string module, string name)
        {
            var export = Process.GetProcAddress(module, name);
            export.ThrowIfInvalidVirtualAddress();
            return export;
        }

        /// <summary>
        /// Close the FPGA Connection.
        /// </summary>
        public void CloseFPGA() => _hVMM?.Close();

        /// <summary>
        /// Get a Vmm Scatter Handle.
        /// </summary>
        /// <param name="flags"></param>
        /// <param name="pid"></param>
        /// <returns></returns>
        public VmmScatterMemory GetScatter(uint flags)
        {
            var handle = Process.Scatter_Initialize(flags);
            ArgumentNullException.ThrowIfNull(handle, nameof(handle));
            return handle;
        }

        #endregion

        #region Memory Macros

        /// <summary>
        /// The PAGE_ALIGN macro takes a virtual address and returns a page-aligned
        /// virtual address for that page.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong PAGE_ALIGN(ulong va) => va & ~(0x1000ul - 1);

        /// <summary>
        /// The ADDRESS_AND_SIZE_TO_SPAN_PAGES macro takes a virtual address and size and returns the number of pages spanned by
        /// the size.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ADDRESS_AND_SIZE_TO_SPAN_PAGES(ulong va, uint size) =>
            (uint)(BYTE_OFFSET(va) + size + (0x1000ul - 1) >> (int)12);

        /// <summary>
        /// The BYTE_OFFSET macro takes a virtual address and returns the byte offset
        /// of that address within the page.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint BYTE_OFFSET(ulong va) => (uint)(va & 0x1000ul - 1);

        /// <summary>
        /// Returns a length aligned to 8 bytes.
        /// Always rounds up.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint AlignLength(uint length) => (length + 7) & ~7u;

        /// <summary>
        /// Returns an address aligned to 8 bytes.
        /// Always the next aligned address.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ulong AlignAddress(ulong address) => (address + 7) & ~7ul;

        #endregion
    }
}