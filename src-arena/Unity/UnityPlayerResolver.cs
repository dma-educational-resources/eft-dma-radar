#pragma warning disable IDE0130

using System.IO;
using System.Runtime.InteropServices;

namespace eft_dma_radar.Arena.Unity
{
    /// <summary>
    /// Native-side counterpart to <see cref="IL2CPP.Il2CppDumper"/>.
    /// <para>
    /// Owns <c>UnityPlayer.dll</c> module discovery and exposes shared signature-decode
    /// primitives (RIP-relative <c>[rip+rel32]</c>, <c>[reg+disp8]</c>, <c>[reg+disp32]</c>).
    /// Consumers (e.g. <see cref="GameWorld.CameraManager"/>) keep their domain-specific
    /// signatures local but route the math through this class so the decoding logic
    /// lives in exactly one place.
    /// </para>
    /// <para>
    /// Also produces <c>%AppData%\eft-dma-radar-arena\unity_player_dump.txt</c> — the
    /// symmetric companion to <c>il2cpp_full_dump.txt</c> — listing the live module
    /// fingerprint and every UnityPlayer-side offset currently resolved on
    /// <see cref="UnityOffsets"/>. Useful when investigating Unity engine bumps.
    /// </para>
    /// <para>
    /// This class does NOT perform its own sig scans for <c>AllCameras</c> /
    /// <c>ViewMatrix</c> / <c>FOV</c> / <c>AspectRatio</c> — those remain owned by
    /// <see cref="GameWorld.CameraManager"/>. Adding a new resolution here means it
    /// is used for something <i>not</i> already covered there.
    /// </para>
    /// </summary>
    public static class UnityPlayerResolver
    {
        // ── Constants ────────────────────────────────────────────────────────

        public const string ModuleName = "UnityPlayer.dll";
        private const string LogTag = "[UnityPlayerResolver]";

        // ── Dev gate ─────────────────────────────────────────────────────────
        // Master enable for the entire UnityPlayer dumper. Flip to false to
        // skip every Dump() call across the project (startup + camera-READY
        // hook). Off-by-default in shipping builds — turn on when investigating
        // a Unity-engine bump or porting to a new Unity game.
        public static bool Enabled = false;

        // ── Host integration (game-agnostic) ─────────────────────────────────
        // Everything game/project-specific the dumper needs is funneled through
        // this struct. The resolver itself references nothing outside this
        // file, so to lift it into another project you copy ONLY this .cs and
        // implement IMemoryAccess against that project's memory layer.

        /// <summary>
        /// Minimal memory-access surface the resolver needs from the host
        /// (process module bases, sig scan, typed reads, log sink, VA validator).
        /// Implement this once in the host and pass it via <see cref="HostConfig.Memory"/>.
        /// </summary>
        public interface IMemoryAccess
        {
            ulong UnityBase { get; }
            ulong GameAssemblyBase { get; }
            ulong GomAddress { get; }

            ulong FindSignature(string signature, string moduleName);
            ulong[] FindSignatures(string signature, string moduleName, int maxMatches);

            bool TryReadValue<T>(ulong addr, out T value, bool useCache = false) where T : unmanaged;
            bool TryReadPtr(ulong addr, out ulong value, bool useCache = false);
            bool TryReadBuffer<T>(ulong addr, Span<T> buffer, bool useCache = false) where T : unmanaged;
            T[] ReadArray<T>(ulong addr, int count, bool useCache = false) where T : unmanaged;
            bool TryReadString(ulong addr, out string? result, int maxBytes = 128, bool useCache = false);
            (uint Timestamp, uint SizeOfImage) ReadPeFingerprint(ulong moduleBase);

            bool IsValidVirtualAddress(ulong va);
            void Log(string message);
        }

        public sealed class HostConfig
        {
            /// <summary>Memory access shim — REQUIRED. Drop-in port: implement once.</summary>
            public IMemoryAccess Memory { get; init; } = NullMemoryAccess.Instance;

            /// <summary>Returns the live FPS/main camera pointer, or 0 if unknown.</summary>
            public Func<ulong> GetLiveCamera { get; init; } = () => 0UL;

            /// <summary>Reads an IL2CPP class-name from a klass pointer, or null.</summary>
            public Func<ulong, string?> ReadIl2CppClassName { get; init; } = _ => null;

            /// <summary>
            /// Optional named GameObject roots to resolve via GOM (e.g. <c>"GameWorld"</c>,
            /// <c>"FPS Camera"</c>). For each name the resolver walks GOM → named-GO →
            /// ComponentArray → entry[1].Component → Comp_ObjectClass and prints the
            /// MonoBehaviour ObjectClass body as a hex window so managed-side field
            /// offsets (MainPlayer, RegisteredPlayers, ...) can be eyeballed.
            /// </summary>
            public string[] NamedGameObjectRoots { get; init; } = Array.Empty<string>();

            /// <summary>
            /// Optional MonoBehaviour class names to resolve via a full GOM scan
            /// (mirrors <c>FindBehaviourByClassName</c>). Useful for managed types
            /// that are not parented to a single well-known GameObject (e.g.
            /// <c>"ClientLocalGameWorld"</c>, <c>"TarkovApplication"</c>,
            /// <c>"MatchingProgressView"</c>).
            /// </summary>
            public string[] BehaviourClassNames { get; init; } = Array.Empty<string>();

            // ── RESOLVED column ───────────────────────────────────────────
            // Values the host has already sig-scanned at startup. These fill
            // the "Resolved" column of the summary table — what the live
            // engine module actually exposes on this run.
            public uint AllCamerasRva                { get; init; } = 0;
            public uint GomFallbackRva               { get; init; } = 0;
            public uint CameraViewMatrix             { get; init; } = 0;
            public uint CameraFov                    { get; init; } = 0;
            public uint CameraAspectRatio            { get; init; } = 0;
            public uint CameraDerefIsAddedOffset     { get; init; } = 0;
            public uint TaHierarchyOff               { get; init; } = 0;
            public uint TaIndexOff                   { get; init; } = 0;
            public uint ThWorldPosition              { get; init; } = 0;
            public uint ThWorldRotation              { get; init; } = 0;
            public uint ThVertices                   { get; init; } = 0;
            public uint ThIndices                    { get; init; } = 0;
            // GO / Comp header offsets are resolved by the dumper itself;
            // host can still inject pre-known values to make the resolver
            // skip the chain probe (leave 0 to force a full re-resolve).
            public uint GoObjectClass                { get; init; } = 0;
            public uint GoComponents                 { get; init; } = 0;
            public uint GoName                       { get; init; } = 0;
            public uint CompObjectClass              { get; init; } = 0;
            public uint CompGameObject               { get; init; } = 0;

            // ── CACHED column ─────────────────────────────────────────────
            public uint CachedAllCamerasRva          { get; init; } = 0;
            public uint CachedGomFallbackRva         { get; init; } = 0;
            public uint CachedCameraViewMatrix       { get; init; } = 0;
            public uint CachedCameraFov              { get; init; } = 0;
            public uint CachedCameraAspectRatio      { get; init; } = 0;
            public uint CachedCameraDerefIsAddedOff  { get; init; } = 0;
            public uint CachedGoObjectClass          { get; init; } = 0;
            public uint CachedGoComponents           { get; init; } = 0;
            public uint CachedGoName                 { get; init; } = 0;
            public uint CachedCompObjectClass        { get; init; } = 0;
            public uint CachedCompGameObject         { get; init; } = 0;
            public uint CachedTaHierarchyOff         { get; init; } = 0;
            public uint CachedTaIndexOff             { get; init; } = 0;
            public uint CachedThWorldPosition        { get; init; } = 0;
            public uint CachedThWorldRotation        { get; init; } = 0;
            public uint CachedThVertices             { get; init; } = 0;
            public uint CachedThIndices              { get; init; } = 0;

            /// <summary>Output dump file path.</summary>
            public string DumpFilePath { get; init; } = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "unity_player_dump",
                "unity_player_dump.txt");
        }

        private sealed class NullMemoryAccess : IMemoryAccess
        {
            public static readonly NullMemoryAccess Instance = new();
            public ulong UnityBase => 0;
            public ulong GameAssemblyBase => 0;
            public ulong GomAddress => 0;
            public ulong FindSignature(string s, string m) => 0;
            public ulong[] FindSignatures(string s, string m, int n) => Array.Empty<ulong>();
            public bool TryReadValue<T>(ulong a, out T v, bool c) where T : unmanaged { v = default; return false; }
            public bool TryReadPtr(ulong a, out ulong v, bool c) { v = 0; return false; }
            public bool TryReadBuffer<T>(ulong a, Span<T> b, bool c) where T : unmanaged => false;
            public T[] ReadArray<T>(ulong a, int n, bool c) where T : unmanaged => Array.Empty<T>();
            public bool TryReadString(ulong a, out string? s, int m, bool c) { s = null; return false; }
            public (uint, uint) ReadPeFingerprint(ulong b) => (0, 0);
            public bool IsValidVirtualAddress(ulong va) => false;
            public void Log(string msg) { }
        }

        // ── Inlined layout structs (so the resolver has no Unity.cs dependency) ──

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private readonly struct LinkedListObjectLayout
        {
            public readonly ulong PreviousObjectLink;
            public readonly ulong NextObjectLink;
            public readonly ulong ThisObject;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private readonly struct ComponentArrayEntry
        {
            public readonly ulong InstancePad; // +0x0
            public readonly ulong Component;   // +0x8
        }

        /// <summary>
        /// Snapshot of a Unity GameObject's relevant fields, read via the
        /// host-supplied <see cref="HostConfig"/> offsets so this resolver
        /// has no dependency on a project-specific GameObject struct.
        /// </summary>
        private readonly struct GoSnapshot
        {
            public readonly ulong ObjectClass;
            public readonly ulong NamePtr;
            public readonly ulong CompArrayBase;
            public readonly ulong CompArraySize;
            public readonly ulong CompArrayCapacity;

            public GoSnapshot(ulong objectClass, ulong namePtr, ulong arrBase, ulong size, ulong cap)
            {
                ObjectClass        = objectClass;
                NamePtr            = namePtr;
                CompArrayBase      = arrBase;
                CompArraySize      = size;
                CompArrayCapacity  = cap;
            }
        }

        private static bool TryReadGoSnapshot(ulong go, out GoSnapshot snap)
        {
            snap = default;
            if (!Mem.IsValidVirtualAddress(go)) return false;
            uint goObjOff  = EffGoObjectClass;
            uint goNameOff = EffGoName;
            uint goCompOff = EffGoComponents;
            if (goCompOff == 0) return false; // can't read array layout without offset
            if (!Mem.TryReadPtr(go + goObjOff,  out var oc, true)) return false;
            if (!Mem.TryReadPtr(go + goNameOff, out var np, true)) return false;
            // ComponentArray layout: ArrayBase(0x00), MemLabelId(0x08), Size(0x10), Capacity(0x18)
            ulong compBase = go + goCompOff;
            if (!Mem.TryReadPtr  (compBase + 0x00, out var arrBase, true)) return false;
            if (!Mem.TryReadValue<ulong>(compBase + 0x10, out var size,     true)) return false;
            if (!Mem.TryReadValue<ulong>(compBase + 0x18, out var cap,      true)) return false;
            snap = new GoSnapshot(oc, np, arrBase, size, cap);
            return true;
        }

        /// <summary>
        /// Probes a Unity native GameObjectManager struct to extract the
        /// (LastActiveNode, ActiveNodes) pair without depending on a fixed layout.
        /// Tries the two layouts seen in Unity 2019..2022 builds and validates
        /// by walking one node. Returns (0, 0) if nothing plausible is found.
        /// </summary>
        private static (ulong LastActive, ulong ActiveNodes) ProbeGom(ulong gomAddress)
        {
            if (!Mem.IsValidVirtualAddress(gomAddress)) return (0, 0);
            (uint Last, uint Active)[] candidates = { (0x20, 0x28), (0x28, 0x30) };
            foreach (var (lastOff, activeOff) in candidates)
            {
                if (!Mem.TryReadPtr(gomAddress + lastOff, out var last, false)) continue;
                if (!Mem.TryReadPtr(gomAddress + activeOff, out var active, false)) continue;
                if (!Mem.IsValidVirtualAddress(last) || !Mem.IsValidVirtualAddress(active)) continue;
                if (!Mem.TryReadValue<LinkedListObjectLayout>(active, out var firstNode, false)) continue;
                if (!Mem.IsValidVirtualAddress(firstNode.ThisObject)) continue;
                return (last, active);
            }
            return (0, 0);
        }

        // ── Static facade so the rest of the file calls Mem.X / Log(...) ──────

        private static IMemoryAccess Mem => _host.Memory;

        /// <summary>
        /// Walks the GOM ActiveNodes linked list ONCE and returns the list of
        /// GameObject pointers. Called at the start of Dump() so all sections
        /// share a stable snapshot — the live list mutates continuously and
        /// late-running sections (e.g. the managed MonoBehaviour dump) would
        /// otherwise walk into freed memory and waste 16k iterations.
        /// Mirrors the CONFIRMED live-chain walker: traverse by NextObjectLink
        /// and stop at LastActiveNode. The list traversal itself is the
        /// validator — gating on GO+0x80 → klass-name "GameObject" doesn't work
        /// because a GameObject's native ObjectClass is not an IL2CPP klass and
        /// the [0x0,0x10] name chain only resolves on Component objectClasses.
        /// </summary>
        private static List<ulong> SnapshotGomList()
        {
            var result = new List<ulong>(256);
            ulong gomAddr = Mem.GomAddress;
            if (!Mem.IsValidVirtualAddress(gomAddr)) return result;
            var (last, active) = ProbeGom(gomAddr);
            if (!Mem.IsValidVirtualAddress(active) || !Mem.IsValidVirtualAddress(last)) return result;

            // Mirror the working GOM name-listing sweep (Phase E in this same
            // file): walk via NextObjectLink at +0x08, ThisObject at +0x10,
            // cap at 256, stop on null / self-loop. We deliberately do NOT
            // walk to LastActiveNode equality — on this Unity build the
            // identity check never matches and the walk runs into 8000+
            // recycled-heap entries, which then floods DMA in the managed
            // pass and causes legitimate GO reads to fail intermittently.
            const int maxNodes = 256;
            ulong sweep = active;
            for (int i = 0; i < 8192 && result.Count < maxNodes; i++)
            {
                if (!Mem.IsValidVirtualAddress(sweep)) break;
                if (!Mem.TryReadValue<ulong>(sweep + 0x08, out var nextLink, false)) break;
                if (Mem.TryReadValue<ulong>(sweep + 0x10, out var thisGo, false) &&
                    Mem.IsValidVirtualAddress(thisGo))
                {
                    result.Add(thisGo);
                }
                if (nextLink == 0 || nextLink == sweep) break;
                sweep = nextLink;
            }
            return result;
        }
        private static void Log(string message) => _host.Memory.Log(message);

        /// <summary>
        /// Scans the loaded <c>UnityPlayer.dll</c> image for the canonical Unity
        /// version string (e.g. <c>2022.3.62f1</c> or <c>6000.0.36f1</c>) so the
        /// dump records the exact engine build, not just a PE timestamp.
        /// Returns null when nothing matches.
        /// </summary>
        private static string? TryReadUnityVersion(ulong unityBase, uint sizeOfImage)
        {
            if (!Mem.IsValidVirtualAddress(unityBase) || sizeOfImage == 0) return null;

            // Cap at 16 MB so we never scan the whole image; the version string
            // lives in .rdata, well within the first few MB on every Unity build.
            int max = (int)Math.Min(sizeOfImage, 16u * 1024u * 1024u);
            const int chunk = 0x10000;
            const int overlap = 0x40;
            byte[] buf = new byte[chunk + overlap];

            for (int offset = 0; offset < max; offset += chunk)
            {
                int want = Math.Min(chunk + overlap, max - offset);
                if (!Mem.TryReadBuffer<byte>(unityBase + (uint)offset, buf.AsSpan(0, want), false))
                    continue;

                for (int i = 0; i < want - 8; i++)
                {
                    // Match: <digit>{1..4} '.' <digit>{1..2} '.' <digit>{1..3} ('a'|'b'|'f'|'p'|'x') <digit>{1..3}
                    int p = i;
                    int s = p;
                    while (p < want && buf[p] >= (byte)'0' && buf[p] <= (byte)'9') p++;
                    int len1 = p - s; if (len1 < 1 || len1 > 4 || p >= want || buf[p] != (byte)'.') continue;
                    p++; int s2 = p;
                    while (p < want && buf[p] >= (byte)'0' && buf[p] <= (byte)'9') p++;
                    int len2 = p - s2; if (len2 < 1 || len2 > 2 || p >= want || buf[p] != (byte)'.') continue;
                    p++; int s3 = p;
                    while (p < want && buf[p] >= (byte)'0' && buf[p] <= (byte)'9') p++;
                    int len3 = p - s3; if (len3 < 1 || len3 > 3 || p >= want) continue;
                    byte tag = buf[p];
                    if (tag != (byte)'a' && tag != (byte)'b' && tag != (byte)'f' &&
                        tag != (byte)'p' && tag != (byte)'x') continue;
                    p++; int s4 = p;
                    while (p < want && buf[p] >= (byte)'0' && buf[p] <= (byte)'9') p++;
                    int len4 = p - s4; if (len4 < 1 || len4 > 3) continue;

                    // Sanity-gate the major version against known Unity ranges
                    // (4..2023 LTS series and the 6000.x branch).
                    int major = 0;
                    for (int k = s; k < s + len1; k++) major = major * 10 + (buf[k] - '0');
                    if (!(major is >= 4 and <= 2023 or >= 6000 and <= 6999)) continue;

                    return System.Text.Encoding.ASCII.GetString(buf, s, p - s);
                }
            }
            return null;
        }

        private static HostConfig _host = new();

        /// <summary>Inject the host bindings (call once at startup).</summary>
        public static void Configure(HostConfig host) => _host = host ?? new HostConfig();

        // ── Last-resolved offsets (populated by AppendLiveChainDumps) ────────
        // These are the values the dumper proved on the most recent run, used
        // by the final summary table so it shows what the dumper resolved
        // rather than the constants in UnityOffsets (which start at 0).
        private static uint _resolvedGoObjectClass;
        private static uint _resolvedGoComponents;
        private static uint _resolvedGoName;
        private static uint _resolvedCompObjectClass;
        private static uint _resolvedCompGameObject;
        private static int  _resolvedCompStride;   // stride between ComponentArray entries (e.g. 0x10 or 0x20)
        private static int  _resolvedCompInstOff;  // offset of the Component pointer inside an entry (e.g. +0x08 or +0x10)

        // Helpers so downstream sections (managed dump, transform probe, etc.)
        // can use the live-resolved offsets when _host.* are still 0 on this build.
        private static uint EffGoObjectClass    => _resolvedGoObjectClass    != 0 ? _resolvedGoObjectClass    : _host.GoObjectClass;
        private static uint EffGoComponents     => _resolvedGoComponents     != 0 ? _resolvedGoComponents     : _host.GoComponents;
        private static uint EffGoName           => _resolvedGoName           != 0 ? _resolvedGoName           : _host.GoName;
        private static uint EffCompObjectClass  => _resolvedCompObjectClass  != 0 ? _resolvedCompObjectClass  : _host.CompObjectClass;
        private static uint EffCompGameObject   => _resolvedCompGameObject   != 0 ? _resolvedCompGameObject   : _host.CompGameObject;
        private static int  EffCompStride       => _resolvedCompStride       > 0  ? _resolvedCompStride       : 0x10;
        private static int  EffCompInstOff      => _resolvedCompInstOff      > 0  ? _resolvedCompInstOff      : 0x08;

        // ── Per-Dump-call GOM snapshot ───────────────────────────────────────
        // The GOM linked list mutates continuously while the game runs. By the
        // time the managed MonoBehaviour section executes (~25s after Dump()
        // started, after Il2CppDumper) the live list has churned and a stale
        // Next pointer can drift into freed memory, producing a 16k-entry
        // garbage walk. We snapshot the list ONCE at the start of Dump() and
        // reuse it across all sections that need a stable GO set.
        private static List<ulong>? _gomSnapshot;

        // ── Module info ──────────────────────────────────────────────────────

        /// <summary>UnityPlayer.dll base address, or 0 if not yet loaded.</summary>
        public static ulong ModuleBase => Mem.UnityBase;

        /// <summary>True if UnityPlayer.dll is mapped at a plausible address.</summary>
        public static bool IsLoaded => Mem.IsValidVirtualAddress(Mem.UnityBase);

        /// <summary>
        /// PE fingerprint (timestamp + SizeOfImage) of the live UnityPlayer.dll, or
        /// (0, 0) if the module is not loaded / unreadable. Used to key caches that
        /// depend on Unity-engine struct layouts.
        /// </summary>
        public static (uint Timestamp, uint SizeOfImage) PeFingerprint =>
            IsLoaded ? Mem.ReadPeFingerprint(Mem.UnityBase) : (0u, 0u);

        // ── RIP-relative decode (mirror of TypeInfoTableResolver pattern) ────

        /// <summary>
        /// Decodes a <c>mov reg, [rip+rel32]</c>-style instruction and returns the
        /// resolved RVA into UnityPlayer.dll, or 0 on failure.
        /// </summary>
        /// <param name="sigAddr">Address the signature matched at.</param>
        /// <param name="relOffset">Byte offset of the 32-bit displacement within the instruction.</param>
        /// <param name="instrLen">Total length of the instruction (RIP base for the displacement).</param>
        public static ulong DecodeRipRelativeRva(ulong sigAddr, int relOffset, int instrLen)
        {
            var unityBase = Mem.UnityBase;
            if (!Mem.IsValidVirtualAddress(unityBase) || sigAddr == 0)
                return 0;
            if (!Mem.TryReadValue<int>(sigAddr + (ulong)relOffset, out var rel, false))
                return 0;
            ulong va = sigAddr + (ulong)instrLen + (ulong)(long)rel;
            return va > unityBase ? va - unityBase : 0;
        }

        /// <summary>
        /// Reads a 1-byte displacement (e.g. the <c>?</c> in <c>mov rax, [rcx+?]</c>) at
        /// the given position relative to the signature match, returning 0 on failure.
        /// </summary>
        public static uint ReadDisp8(ulong sigAddr, int position)
            => sigAddr != 0 && Mem.TryReadValue<byte>(sigAddr + (ulong)position, out var b, false) ? b : 0u;

        /// <summary>
        /// Reads a 4-byte displacement (e.g. the <c>? ? ? ?</c> in <c>movss xmm0, [rcx+?]</c>)
        /// at the given position relative to the signature match, returning 0 on failure.
        /// </summary>
        public static uint ReadDisp32(ulong sigAddr, int position)
            => sigAddr != 0 && Mem.TryReadValue<uint>(sigAddr + (ulong)position, out var u, false) ? u : 0u;

        /// <summary>
        /// Resolves a <c>call rel32</c> target inside UnityPlayer.dll. Returns the absolute
        /// VA of the call's body, or 0 on failure. Useful when a wrapper exposes the offset
        /// you actually want.
        /// </summary>
        /// <param name="sigAddr">Address of the <c>E8</c> opcode.</param>
        public static ulong DecodeCallTarget(ulong sigAddr)
        {
            if (sigAddr == 0) return 0;
            if (!Mem.TryReadValue<int>(sigAddr + 1, out var rel, false))
                return 0;
            ulong va = sigAddr + 5 + (ulong)(long)rel;
            return Mem.IsValidVirtualAddress(va) ? va : 0;
        }

        // ── Convenience scan helpers ─────────────────────────────────────────

        /// <summary>
        /// Single-match scan against UnityPlayer.dll. Returns 0 on miss.
        /// </summary>
        public static ulong FindSignature(string signature)
        {
            if (!IsLoaded) return 0;
            try { return Mem.FindSignature(signature, ModuleName); }
            catch (Exception ex)
            {
                Log($"{LogTag} FindSignature error: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Multi-match scan against UnityPlayer.dll. Returns an empty array on error or miss.
        /// </summary>
        public static ulong[] FindSignatures(string signature, int maxMatches = 64)
        {
            if (!IsLoaded) return Array.Empty<ulong>();
            try { return Mem.FindSignatures(signature, ModuleName, maxMatches); }
            catch (Exception ex)
            {
                Log($"{LogTag} FindSignatures error: {ex.Message}");
                return Array.Empty<ulong>();
            }
        }

        // ── Dump file ────────────────────────────────────────────────────────

        private static string DumpFilePath => _host.DumpFilePath;

        // ── Host-bound helpers (keep all game coupling here) ─────────────────
        private static ulong   SafeGetLiveCamera()
        {
            try { return _host.GetLiveCamera?.Invoke() ?? 0UL; }
            catch { return 0UL; }
        }
        private static string? SafeReadKlassName(ulong klass)
        {
            try { return _host.ReadIl2CppClassName?.Invoke(klass); }
            catch { return null; }
        }

        /// <summary>
        /// True if every char in <paramref name="s"/> is an ASCII identifier-ish
        /// character (letters/digits/_/&lt;/&gt;/`). Used by the chain probe to
        /// decide whether a klass-name read produced a sane string.
        /// </summary>
        private static bool IsCleanIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (var ch in s)
            {
                bool ok = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') ||
                          (ch >= '0' && ch <= '9') || ch == '_' || ch == '<' || ch == '>' || ch == '`';
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>
        /// Writes a snapshot of UnityPlayer.dll module info + every currently-resolved
        /// UnityPlayer-side offset (sourced from <see cref="UnityOffsets"/>) to
        /// <c>%AppData%\eft-dma-radar-arena\unity_player_dump.txt</c>. Safe to call any
        /// time after the game is loaded; non-throwing.
        /// </summary>
        // Once a "live" dump (with a valid live camera pointer) has been written,
        // refuse to overwrite it with a degraded dump (e.g. during shutdown when
        // the camera pointer is stale/invalid). Avoids the program-close pass
        // clobbering the good in-match dump.
        private static volatile bool _liveDumpWritten;

        public static void Dump()
        {
            if (!Enabled)
            {
                Log($"{LogTag} Dump skipped (UnityPlayerResolver.Enabled = false).");
                return;
            }

            ulong liveCam = SafeGetLiveCamera();
            bool isLive = Mem.IsValidVirtualAddress(liveCam);
            if (_liveDumpWritten && !isLive)
            {
                Log($"{LogTag} Skipping degraded dump (live dump already on disk).");
                return;
            }

            var sb = new StringBuilder(4096);
            _gomSnapshot = SnapshotGomList();
            ulong unityBase = 0;
            try
            {
                unityBase = Mem.UnityBase;
                if (!Mem.IsValidVirtualAddress(unityBase))
                {
                    Log($"{LogTag} ABORT: UnityPlayer.dll not loaded.");
                    return;
                }

                var (ts, size) = (0u, 0u);
                try { (ts, size) = Mem.ReadPeFingerprint(unityBase); }
                catch (Exception ex) { Log($"{LogTag} PE fingerprint failed: {ex.Message}"); }

                string unityVersion = TryReadUnityVersion(unityBase, size) ?? "<unresolved>";

                sb.AppendLine($"// UnityPlayer Dump — {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}Z");
                sb.AppendLine($"// Module Base       : 0x{unityBase:X}");
                sb.AppendLine($"// PE Timestamp      : 0x{ts:X8}");
                sb.AppendLine($"// PE SizeOfImage    : 0x{size:X}");
                sb.AppendLine($"// Unity Version     : {unityVersion}");
                sb.AppendLine();

                sb.AppendLine("// ── Resolved RVAs ───────────────────────────────────────────");
                AppendRva(sb, "AllCameras (fallback)", unityBase, _host.AllCamerasRva);
                AppendRva(sb, "GOM        (fallback)", unityBase, _host.GomFallbackRva);
                sb.AppendLine();

                sb.AppendLine("// ── Camera struct offsets (writable; sig-scanned at runtime) ─");
                sb.AppendLine($"//   ViewMatrix        : 0x{_host.CameraViewMatrix:X}");
                sb.AppendLine($"//   FOV               : 0x{_host.CameraFov:X}");
                sb.AppendLine($"//   AspectRatio       : 0x{_host.CameraAspectRatio:X}");
                sb.AppendLine($"//   DerefIsAddedOff   : 0x{_host.CameraDerefIsAddedOffset:X}");
                sb.AppendLine();

                sb.AppendLine("// ── GameObject / Component layout (UNCONFIRMED — verify via ground-truth dump) ─");
                sb.AppendLine($"//   GO_ObjectClass    : 0x{_host.GoObjectClass:X}");
                sb.AppendLine($"//   GO_Components     : 0x{_host.GoComponents:X}");
                sb.AppendLine($"//   GO_Name           : 0x{_host.GoName:X}");
                sb.AppendLine($"//   Comp_ObjectClass  : 0x{_host.CompObjectClass:X}");
                sb.AppendLine($"//   Comp_GameObject   : 0x{_host.CompGameObject:X}");
                sb.AppendLine();

                sb.AppendLine("// ── Transform / Hierarchy / Camera (CONFIRMED — radar works at runtime) ─");
                sb.AppendLine($"//   TA.HierarchyOff   : 0x{_host.TaHierarchyOff:X}");
                sb.AppendLine($"//   TA.IndexOff       : 0x{_host.TaIndexOff:X}");
                sb.AppendLine($"//   TH.WorldPosition  : 0x{_host.ThWorldPosition:X}");
                sb.AppendLine($"//   TH.WorldRotation  : 0x{_host.ThWorldRotation:X}");
                sb.AppendLine($"//   TH.Vertices       : 0x{_host.ThVertices:X}");
                sb.AppendLine($"//   TH.Indices        : 0x{_host.ThIndices:X}");
                sb.AppendLine();

                // ── A) PE sections + RTTI class-name dump ───────────────────
                try
                {
                    if (TryReadPeSections(unityBase, out var sections))
                    {
                        sb.AppendLine("// ── PE Sections ─────────────────────────────────────────────");
                        foreach (var s in sections)
                            sb.AppendLine($"//   {s.Name,-10} rva=0x{s.VirtualAddress:X8}  size=0x{s.VirtualSize:X8}  {(s.IsExecutable ? "X" : "-")}{(s.IsReadable ? "R" : "-")}{(s.IsWritable ? "W" : "-")}");
                        sb.AppendLine();

                        List<string> rttiNames;
                        try { rttiNames = ScanRttiClassNames(unityBase, sections); }
                        catch (Exception ex)
                        {
                            sb.AppendLine($"// ── RTTI scan failed: {ex.Message} ─────────────");
                            rttiNames = new List<string>();
                        }
                        sb.AppendLine($"// ── RTTI Classes ({rttiNames.Count}) ──────────────────────────────────");
                        foreach (var name in rttiNames)
                            sb.AppendLine($"//   {name}");
                        sb.AppendLine();
                    }
                    else
                    {
                        sb.AppendLine("// ── PE Sections: read failed ────────────────────────────────");
                        sb.AppendLine();
                    }
                }
                catch (Exception ex)
                {
                    sb.AppendLine($"// ── PE/RTTI section threw: {ex.Message} ─────────────");
                    sb.AppendLine();
                }

                // ── D) Live FPS camera instance hex + classifier ────────────
                try { AppendLiveCameraDump(sb); }
                catch (Exception ex) { sb.AppendLine($"// ── Live camera dump threw: {ex.Message} ───"); }

                // ── E) Ground-truth dump using CONFIRMED game-side offsets ──
                try { AppendGroundTruthDump(sb); }
                catch (Exception ex) { sb.AppendLine($"// ── Ground-truth dump threw: {ex.Message} ───"); }

                try { AppendLiveChainDumps(sb); }
                catch (Exception ex) { sb.AppendLine($"// ── Live chain dump threw: {ex.Message} ───"); }

                // ── F) Managed (IL2CPP) MonoBehaviour resolution ────────────
                //   Mirrors what the radar actually uses at runtime:
                //     GOM → GameObject named "X" → ComponentArray → entry → ObjectClass
                //     and a full GOM scan by IL2CPP class-name.
                try { AppendMonoBehaviourDump(sb); }
                catch (Exception ex) { sb.AppendLine($"// ── MonoBehaviour dump threw: {ex.Message} ───"); }

                try { AppendResolvedSummary(sb, unityBase); }
                catch (Exception ex) { sb.AppendLine($"// ── Resolved summary threw: {ex.Message} ───"); }
            }
            catch (Exception ex)
            {
                Log($"{LogTag} Dump body threw: {ex.Message}");
                sb.AppendLine();
                sb.AppendLine($"// ── FATAL: dump aborted: {ex.Message} ───");
            }

            // ── Always attempt to write whatever we collected ───────────────
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(DumpFilePath)!);
                File.WriteAllText(DumpFilePath, sb.ToString());
                if (isLive) _liveDumpWritten = true;
                Log($"{LogTag} Dump written → {DumpFilePath}{(isLive ? " (live)" : "")}");
            }
            catch (Exception ex)
            {
                Log($"{LogTag} Dump write FAILED: {ex.Message}");
            }
        }

        private static void AppendRva(StringBuilder sb, string label, ulong moduleBase, ulong rva)
        {
            ulong va = rva == 0 ? 0 : moduleBase + rva;
            sb.AppendLine($"//   {label,-22}: rva=0x{rva:X}  va=0x{va:X}");
        }

        // ── Final summary table ──────────────────────────────────────────────

        /// <summary>
        /// Pretty single-glance table of every offset/RVA the dumper resolves,
        /// with status flags so you can tell at a glance which values are
        /// CONFIRMED vs UNCONFIRMED. Always written as the last section of the
        /// dump file so a quick "Get-Content -Tail" tells you the full state.
        /// </summary>
        private static void AppendResolvedSummary(StringBuilder sb, ulong unityBase)
        {
            sb.AppendLine();
            sb.AppendLine("// ════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("// RESOLVED OFFSETS — final summary  (Resolved = this run, Cached = host fallback)");
            sb.AppendLine("// ════════════════════════════════════════════════════════════════════════════");
            sb.AppendLine("//   Status: ✓ resolved   ≈ matches cached   ! differs from cached   ? cached only   ✗ unknown");
            sb.AppendLine();
            sb.AppendLine("//   Group       Field                Resolved             Cached               Status");
            sb.AppendLine("//   ──────────  ───────────────────  ───────────────────  ───────────────────  ──────");

            // UnityPlayer-relative RVAs.
            AppendSummaryRow(sb, "Signature ", "AllCameras",       _host.AllCamerasRva,           _host.CachedAllCamerasRva,         isRva: true);
            AppendSummaryRow(sb, "Signature ", "GOM (fallback)",   _host.GomFallbackRva,          _host.CachedGomFallbackRva,        isRva: true);

            // Camera struct — sig-scanned by host (resolver doesn't redo this).
            AppendSummaryRow(sb, "Camera    ", "ViewMatrix",        _host.CameraViewMatrix,         _host.CachedCameraViewMatrix);
            AppendSummaryRow(sb, "Camera    ", "FOV",               _host.CameraFov,                _host.CachedCameraFov);
            AppendSummaryRow(sb, "Camera    ", "AspectRatio",       _host.CameraAspectRatio,        _host.CachedCameraAspectRatio);
            AppendSummaryRow(sb, "Camera    ", "DerefIsAddedOff",   _host.CameraDerefIsAddedOffset, _host.CachedCameraDerefIsAddedOff);

            // GameObject / Component header — resolved by the chain probe this run.
            AppendSummaryRow(sb, "GameObject", "GO_ObjectClass",   _resolvedGoObjectClass,   _host.CachedGoObjectClass);
            AppendSummaryRow(sb, "GameObject", "GO_Components",    _resolvedGoComponents,    _host.CachedGoComponents);
            AppendSummaryRow(sb, "GameObject", "GO_Name",          _resolvedGoName,          _host.CachedGoName);
            AppendSummaryRow(sb, "Component ", "Comp_ObjectClass", _resolvedCompObjectClass, _host.CachedCompObjectClass);
            AppendSummaryRow(sb, "Component ", "Comp_GameObject",  _resolvedCompGameObject,  _host.CachedCompGameObject);

            // Transform / Hierarchy chain — host-cached values (radar relies on these).
            AppendSummaryRow(sb, "TA        ", "HierarchyOff",     _host.TaHierarchyOff,     _host.CachedTaHierarchyOff);
            AppendSummaryRow(sb, "TA        ", "IndexOff",         _host.TaIndexOff,         _host.CachedTaIndexOff);
            AppendSummaryRow(sb, "TH        ", "WorldPosition",    _host.ThWorldPosition,    _host.CachedThWorldPosition);
            AppendSummaryRow(sb, "TH        ", "WorldRotation",    _host.ThWorldRotation,    _host.CachedThWorldRotation);
            AppendSummaryRow(sb, "TH        ", "Vertices",         _host.ThVertices,         _host.CachedThVertices);
            AppendSummaryRow(sb, "TH        ", "Indices",          _host.ThIndices,          _host.CachedThIndices);

            sb.AppendLine("// ════════════════════════════════════════════════════════════════════════════");
        }

        private static string FmtVal(uint v, bool isRva)
        {
            if (v == 0) return "<unresolved>";
            return isRva ? $"UnityPlayer+0x{v:X7}" : $"0x{v:X}";
        }

        private static void AppendSummaryRow(StringBuilder sb, string group, string field, uint resolved, uint cached, bool isRva = false)
        {
            string status;
            if (resolved != 0 && cached != 0)
                status = resolved == cached ? "≈" : "!";
            else if (resolved != 0)
                status = "✓";
            else if (cached != 0)
                status = "?";
            else
                status = "✗";

            string r = FmtVal(resolved, isRva).PadRight(19);
            string c = FmtVal(cached,   isRva).PadRight(19);
            sb.AppendLine($"//   {group}  {field,-19}  {r}  {c}  {status}");
        }

        // ── PE section walk ──────────────────────────────────────────────────

        public readonly record struct PeSection(
            string Name,
            uint VirtualAddress,
            uint VirtualSize,
            uint Characteristics)
        {
            public bool IsExecutable => (Characteristics & 0x20000000u) != 0;
            public bool IsReadable   => (Characteristics & 0x40000000u) != 0;
            public bool IsWritable   => (Characteristics & 0x80000000u) != 0;
        }

        private static bool TryReadPeSections(ulong moduleBase, out List<PeSection> sections)
        {
            sections = new List<PeSection>(16);
            try
            {
                if (!Mem.TryReadValue<uint>(moduleBase + 0x3C, out var eLfanew, false) ||
                    eLfanew == 0 || eLfanew > 0x1000)
                    return false;

                ulong ntHeaders = moduleBase + eLfanew;
                if (!Mem.TryReadValue<ushort>(ntHeaders + 0x6, out var numberOfSections, false))
                    return false;
                if (!Mem.TryReadValue<ushort>(ntHeaders + 0x14, out var sizeOfOptionalHeader, false))
                    return false;
                if (numberOfSections == 0 || numberOfSections > 96)
                    return false;

                ulong sectionTable = ntHeaders + 0x18 + sizeOfOptionalHeader;
                Span<byte> raw = stackalloc byte[40];
                for (int i = 0; i < numberOfSections; i++)
                {
                    if (!Mem.TryReadBuffer<byte>(sectionTable + (ulong)(i * 40), raw, false))
                        return false;
                    string name = System.Text.Encoding.ASCII.GetString(raw[..8]).TrimEnd('\0');
                    uint virtualSize = BitConverter.ToUInt32(raw.Slice(8, 4));
                    uint virtualAddr = BitConverter.ToUInt32(raw.Slice(12, 4));
                    uint chars       = BitConverter.ToUInt32(raw.Slice(36, 4));
                    sections.Add(new PeSection(name, virtualAddr, virtualSize, chars));
                }
                return sections.Count > 0;
            }
            catch
            {
                return false;
            }
        }

        // ── RTTI class-name scan (MSVC TypeDescriptor) ───────────────────────
        // MSVC TypeDescriptor layout (x64):
        //   +0x00  vtable ptr (RTTI Type Descriptor's vftable, lives in the same
        //          module — first qword of every TD points to the same address)
        //   +0x08  spare (0)
        //   +0x10  null-terminated decorated name, e.g. ".?AVCamera@@"
        //
        // Strategy: read .data + .rdata (where TDs live), find the unique
        // ".?AV" / ".?AU" name pattern, and reverse to the class name.

        private const int MaxRttiNames = 4096;

        private static List<string> ScanRttiClassNames(ulong moduleBase, List<PeSection> sections)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var s in sections)
            {
                if (s.VirtualSize == 0 || s.VirtualSize > 0x4000_0000) continue;
                // MSVC decorated names live in .data (writable TDs) or _RDATA / .rdata (read-only).
                // Unity ships as _RDATA instead of the standard .rdata name.
                if (s.Name is not (".data" or ".rdata" or "_RDATA" or "_rdata")) continue;
                ScanSectionForRttiNames(moduleBase + s.VirtualAddress, s.VirtualSize, names);
                if (names.Count >= MaxRttiNames) break;
            }
            return names.ToList();
        }

        private static void ScanSectionForRttiNames(ulong sectionVa, uint size, SortedSet<string> output)
        {
            // Smaller chunks: large DMA reads (1 MiB) fail silently on some adapters.
            // Overlap so a decorated name straddling a boundary is still captured intact.
            const int chunk   = 0x10000; // 64 KiB
            const int overlap = 0x200;   // 512 B
            var buffer = new byte[chunk];
            for (uint off = 0; off < size; )
            {
                int toRead = (int)Math.Min((uint)chunk, size - off);
                if (!Mem.TryReadBuffer<byte>(sectionVa + off, buffer.AsSpan(0, toRead), false))
                {
                    off += (uint)chunk;
                    continue;
                }

                int scanLimit = toRead - 8;
                int i = 0;
                while (i < scanLimit)
                {
                    if (buffer[i] == (byte)'.' && buffer[i + 1] == (byte)'?' &&
                        buffer[i + 2] == (byte)'A' &&
                        (buffer[i + 3] == (byte)'V' || buffer[i + 3] == (byte)'U'))
                    {
                        int end = i + 4;
                        while (end < toRead && buffer[end] != 0 && buffer[end] >= 0x20 && buffer[end] < 0x7F)
                            end++;
                        if (end < toRead && buffer[end] == 0)
                        {
                            int len = end - i;
                            if (len > 6 && len < 256)
                            {
                                string decorated = System.Text.Encoding.ASCII.GetString(buffer, i, len);
                                string undecorated = UndecorateRttiName(decorated);
                                if (!string.IsNullOrEmpty(undecorated))
                                {
                                    output.Add(undecorated);
                                    if (output.Count >= MaxRttiNames) return;
                                }
                            }
                            i = end + 1;
                            continue;
                        }
                    }
                    i++;
                }

                // Advance with overlap so names straddling chunks are fully read on the next pass.
                if (toRead < chunk) break;
                off += (uint)(chunk - overlap);
            }
        }

        /// <summary>
        /// Lightweight undecorator for MSVC RTTI names. Handles the common
        /// <c>.?AVName@@</c> and <c>.?AVName@Namespace@@</c> shapes. Returns the
        /// class name (with namespaces joined by <c>::</c>), or empty on failure.
        /// </summary>
        private static string UndecorateRttiName(string decorated)
        {
            // ".?AV" or ".?AU" prefix, "@@" suffix.
            if (decorated.Length < 6 || decorated[0] != '.' || decorated[1] != '?' || decorated[2] != 'A')
                return string.Empty;
            if (!decorated.EndsWith("@@", StringComparison.Ordinal))
                return string.Empty;

            string body = decorated.Substring(4, decorated.Length - 6);
            if (body.Length == 0) return string.Empty;

            // Body is "Class@Namespace@Outer" reversed; split on '@' and reverse.
            var parts = body.Split('@', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return string.Empty;
            // Filter unwanted templated/internal junk and very short noise.
            foreach (var p in parts)
                if (p.Length == 0 || p[0] == '?') return string.Empty;

            Array.Reverse(parts);
            return string.Join("::", parts);
        }

        // ── Live FPS camera instance dump (pointer-classified hex) ───────────

        private const int InstanceDumpSize = 0xC00;

        private static void AppendLiveCameraDump(StringBuilder sb)
        {
            ulong cam = SafeGetLiveCamera();
            if (!Mem.IsValidVirtualAddress(cam))
            {
                sb.AppendLine("// ── Live FPS Camera: not yet resolved (skipped) ─────────────");
                return;
            }

            var buffer = new byte[InstanceDumpSize];
            if (!Mem.TryReadBuffer<byte>(cam, buffer, false))
            {
                sb.AppendLine($"// ── Live FPS Camera: read failed @ 0x{cam:X} ───────────────");
                return;
            }

            sb.AppendLine($"// ── Live FPS Camera Instance @ 0x{cam:X}  ({InstanceDumpSize:X} bytes) ──");
            sb.AppendLine("//   off    qword              hint");

            ulong unityBase = Mem.UnityBase;
            ulong gaBase    = Mem.GameAssemblyBase;

            for (int off = 0; off + 8 <= InstanceDumpSize; off += 8)
            {
                ulong q = BitConverter.ToUInt64(buffer, off);
                string hint = ClassifyQword(q, off, buffer, unityBase, gaBase);
                sb.AppendLine($"//   +0x{off:X3}  0x{q:X16}  {hint}");
            }
        }

        private static string ClassifyQword(ulong q, int off, byte[] buf, ulong unityBase, ulong gaBase)
        {
            int vmOff = (int)_host.CameraViewMatrix;
            int fovOff = (int)_host.CameraFov;
            int arOff  = (int)_host.CameraAspectRatio;

            // ── Mat4 rows — 16 floats from ViewMatrix offset ─────────────
            if (off >= vmOff && off < vmOff + 64)
            {
                int row = (off - vmOff) / 16;
                int col = ((off - vmOff) % 16) / 4;
                float a = BitConverter.ToSingle(buf, off);
                float b = BitConverter.ToSingle(buf, off + 4);
                string rowLabel = off == vmOff ? "ViewMatrix row[0]" : $"ViewMatrix row[{row}] col[{col}..{col+1}]";
                return $"{rowLabel}  [{ForceFloat(a)}, {ForceFloat(b)}]";
            }

            // ── FOV / AspectRatio with live value ───────────────────────
            if (off == fovOff)
            {
                float fov = BitConverter.ToSingle(buf, off);
                return $"<-- Camera.FOV  = {ForceFloat(fov)}";
            }
            if (off == arOff)
            {
                float ar = BitConverter.ToSingle(buf, off);
                return $"<-- Camera.AspectRatio  = {ForceFloat(ar)}";
            }

            // ── x64 pointer: upper 32 bits must be non-zero (userspace VA) ─
            // Values like 0x000000003F800000 have hi32=0 and are float pairs, not ptrs.
            bool couldBePtr = (q >> 32) != 0 && q != 0 && Mem.IsValidVirtualAddress(q);
            if (couldBePtr)
            {
                if (unityBase != 0 && q >= unityBase && q < unityBase + 0x4000_0000)
                    return $"ptr -> UnityPlayer+0x{q - unityBase:X}";
                if (gaBase != 0 && q >= gaBase && q < gaBase + 0x4000_0000)
                    return $"ptr -> GameAssembly+0x{q - gaBase:X}";
                return "ptr (heap)";
            }

            if (q == 0) return "zero";

            // ── Scalar: try float pair first, then i32/u32 ───────────────
            float lo = BitConverter.ToSingle(buf, off);
            float hi = BitConverter.ToSingle(buf, off + 4);
            string fLo = ClassifyFloat(lo);
            string fHi = ClassifyFloat(hi);
            if (fLo.Length > 0 || fHi.Length > 0)
                return $"f32  [{(fLo.Length > 0 ? fLo : "0")}, {(fHi.Length > 0 ? fHi : "0")}]";

            int iLo = BitConverter.ToInt32(buf, off);
            int iHi = BitConverter.ToInt32(buf, off + 4);
            if (iHi == 0 && iLo != 0) return $"i32  lo={iLo}";
            if (iHi != 0)             return $"i32  lo={iLo}  hi={iHi}";
            return "";
        }

        private static string ForceFloat(float f)
            => float.IsNaN(f) || float.IsInfinity(f)
                ? f.ToString()
                : f.ToString("F4", System.Globalization.CultureInfo.InvariantCulture);

        // ── Live chain: Camera → GameObject → Component[] → Transform → TA → TH ──

        private const int ChainDumpSize = 0x200; // 512 B per node

        // ─────────────────────────────────────────────────────────────────────
        // Ground-truth dump: uses ONLY currently-working game-side offsets
        // (GOM, GO_ObjectClass, GO_Components, GO_Name, Comp_ObjectClass,
        // Comp_GameObject, Camera ViewMatrix/FOV/AspectRatio) and the live
        // GameWorld pointers. Speculative Unity-side offsets (TA/TH) are
        // surfaced as raw hex windows for manual verification — never
        // "resolved" by heuristic here.
        // ─────────────────────────────────────────────────────────────────────
        private const int GroundTruthMaxGameObjects = 8;
        private const int GroundTruthMaxComponents  = 24;

        // ── IL2CPP klass whitelist (read once per dump) ─────────────────────
        //   Materializes every klass pointer from the live TypeInfoTable into
        //   a HashSet so the GO/comp probes can filter qwords by membership
        //   instead of trusting Il2CppClass.ReadName's heuristic on random
        //   memory (which produced "@SH��..." style false positives).
        private static HashSet<ulong>? _klassSet;
        private static int _klassSetSize;

        private static HashSet<ulong> BuildKlassWhitelist()
        {
            var set = new HashSet<ulong>(64 * 1024);
            ulong gaBase = Mem.GameAssemblyBase;
            ulong rva    = SDK.Offsets.Special.TypeInfoTableRva;
            if (!Mem.IsValidVirtualAddress(gaBase) || rva == 0) return set;
            if (!Mem.TryReadPtr(gaBase + rva, out var tablePtr, false) ||
                !Mem.IsValidVirtualAddress(tablePtr)) return set;

            const int chunkSize = 4096;
            const int maxClasses = 100_000;
            for (int offset = 0; offset < maxClasses; offset += chunkSize)
            {
                int toRead = Math.Min(chunkSize, maxClasses - offset);
                ulong[] chunk;
                try { chunk = Mem.ReadArray<ulong>(tablePtr + (ulong)offset * 8, toRead, false); }
                catch { break; }

                bool anyValid = false;
                foreach (var p in chunk)
                {
                    if (Mem.IsValidVirtualAddress(p))
                    {
                        set.Add(p);
                        anyValid = true;
                    }
                }
                if (!anyValid) break;
            }
            return set;
        }

        private static void AppendGroundTruthDump(StringBuilder sb)
        {
            // Build / refresh the IL2CPP klass whitelist once per dump.
            _klassSet = BuildKlassWhitelist();
            _klassSetSize = _klassSet.Count;

            sb.AppendLine();
            sb.AppendLine("// ═══ Ground-truth dump ═════════════════════════════════════════════");
            sb.AppendLine("// CONFIRMED   : GOM walk, Camera (ViewMatrix/FOV/AspectRatio), TA/TH");
            sb.AppendLine("//               offsets — radar reads them every frame and works.");
            sb.AppendLine("// UNCONFIRMED : GO_ObjectClass / GO_Components / GO_Name /");
            sb.AppendLine("//               Comp_ObjectClass / Comp_GameObject — used here to");
            sb.AppendLine("//               READ live data so they can be visually verified");
            sb.AppendLine("//               against the CONFIRMED TA/TH chain (Transform ->");
            sb.AppendLine("//               TA.HierarchyOff -> TH -> TH.WorldPosition).");
            sb.AppendLine("// NOTE        : GameObject walked via GOM is a NATIVE Unity engine");
            sb.AppendLine("//               struct, not an IL2CPP managed object — managed");
            sb.AppendLine("//               klass pointers only live inside components.");
            sb.AppendLine($"// IL2CPP klass whitelist: {_klassSetSize} pointers from TypeInfoTable.");

            ulong gomAddr = Mem.GomAddress;
            if (!Mem.IsValidVirtualAddress(gomAddr))
            {
                sb.AppendLine("// GOM not resolved yet — ground-truth dump skipped.");
                return;
            }

            var (gomLast, gomActive) = ProbeGom(gomAddr);
            if (!Mem.IsValidVirtualAddress(gomActive) ||
                !Mem.IsValidVirtualAddress(gomLast))
            {
                sb.AppendLine("// ProbeGom() failed to probe a valid layout — skipped.");
                return;
            }

            sb.AppendLine($"// GOM @ 0x{gomAddr:X}  (CONFIRMED)");
            sb.AppendLine($"//   ActiveNodes    = 0x{gomActive:X}");
            sb.AppendLine($"//   LastActiveNode = 0x{gomLast:X}");

            if (!Mem.TryReadValue<LinkedListObjectLayout>(gomActive, out var firstNode, false) ||
                !Mem.IsValidVirtualAddress(firstNode.ThisObject))
            {
                sb.AppendLine("// ActiveNodes head unreadable — skipped.");
                return;
            }

            // Walk the GOM active list (CONFIRMED) and collect every reachable
            // GameObject pointer. Do NOT gate this on speculative GO offsets —
            // the whole point is to surface raw GO bytes so those offsets can
            // be visually verified.
            var gos = new List<ulong>(GroundTruthMaxGameObjects * 8);
            var current = firstNode;
            for (int i = 0; i < 8192 && gos.Count < GroundTruthMaxGameObjects * 8; i++)
            {
                if (!Mem.IsValidVirtualAddress(current.ThisObject)) break;
                gos.Add(current.ThisObject);
                if (current.ThisObject == gomLast) break;
                if (!Mem.TryReadValue<LinkedListObjectLayout>(current.NextObjectLink, out current, false)) break;
            }

            sb.AppendLine();
            sb.AppendLine($"// Walked GOM list (CONFIRMED) — {gos.Count} GameObject pointer(s) reachable.");

            // Per-GO ground-truth section: dump the raw GO header + try the
            // unconfirmed offsets, then validate by following the CONFIRMED
            // TA -> TH -> WorldPosition chain.
            int dumped = 0;
            int validatedTransformChains = 0;
            foreach (var go in gos)
            {
                if (dumped >= GroundTruthMaxGameObjects) break;

                // Speculative GameObject struct read — may produce garbage if
                // GO_* offsets are wrong on this build.
                bool gotStruct = TryReadGoSnapshot(go, out var goStruct);

                string name = "<no-name>";
                if (gotStruct &&
                    Mem.IsValidVirtualAddress(goStruct.NamePtr) &&
                    Mem.TryReadString(goStruct.NamePtr, out var n, 64, false) &&
                    !string.IsNullOrEmpty(n))
                {
                    name = n;
                }

                sb.AppendLine();
                sb.AppendLine($"// ▼ GameObject [{dumped}] @ 0x{go:X}  speculative-name=\"{name}\"");

                // Raw GO header window (CONFIRMED to be a real GameObject pointer
                // because GOM walk gave it; offsets within are UNCONFIRMED).
                DumpInstance(sb, "GameObject header (raw window)", go, 0x100);

                if (gotStruct)
                {
                    sb.AppendLine();
                    sb.AppendLine($"//     Speculative reads using current UnityOffsets:");
                    sb.AppendLine($"//       GO+0x{_host.GoObjectClass:X} (GO_ObjectClass)  = 0x{goStruct.ObjectClass:X16}  {(Mem.IsValidVirtualAddress(goStruct.ObjectClass) ? "ptr-shaped" : "INVALID")}");
                    sb.AppendLine($"//       GO+0x{_host.GoName:X} (GO_Name)         = 0x{goStruct.NamePtr:X16}  {(Mem.IsValidVirtualAddress(goStruct.NamePtr) ? "ptr-shaped" : "INVALID")}");
                    sb.AppendLine($"//       GO+0x{_host.GoComponents:X} (GO_Components.Base)  = 0x{goStruct.CompArrayBase:X16}");
                    sb.AppendLine($"//       GO+0x{_host.GoComponents + 0x10:X} (GO_Components.Size)  = {goStruct.CompArraySize}");
                    sb.AppendLine($"//       GO+0x{_host.GoComponents + 0x18:X} (GO_Components.Cap)   = {goStruct.CompArrayCapacity}");

                    // ── IL2CPP-combined probe over GO header ─────────────────
                    //   NOTE: a Unity GameObject walked via GOM is a NATIVE
                    //   engine struct (Object/EditorExtension/GameObject), not
                    //   an IL2CPP managed object — so we do NOT expect a
                    //   "GameObject" klass pointer here. Managed klass
                    //   pointers only appear in COMPONENTS (Transform,
                    //   MonoBehaviour wrappers, etc.). Any whitelist hit at
                    //   this level is therefore informational only and likely
                    //   represents a managed reference embedded in the GO
                    //   (e.g. a cached Transform wrapper).
                    Span<byte> gobuf = stackalloc byte[0x100];
                    if (Mem.TryReadBuffer<byte>(go, gobuf, false))
                    {
                        sb.AppendLine($"//     ── IL2CPP klass-whitelist probe over GO header (informational) ──");
                        int hits = 0;
                        var klassSet = _klassSet;
                        for (int o = 0; o + 8 <= gobuf.Length && hits < 16; o += 8)
                        {
                            ulong q = BitConverter.ToUInt64(gobuf[o..(o + 8)]);
                            if (klassSet is null || !klassSet.Contains(q)) continue;
                            var nm = SafeReadKlassName(q) ?? "<no-name>";
                            sb.AppendLine($"//       GO+0x{o:X3}  -> klass 0x{q:X16}  name=\"{nm}\"");
                            hits++;
                        }
                        if (hits == 0)
                            sb.AppendLine($"//       (no whitelisted klass pointers — expected: GO header is native Unity, not managed)");
                    }
                }

                // Walk components only if the speculative array looks sane.
                if (gotStruct &&
                    Mem.IsValidVirtualAddress(goStruct.CompArrayBase) &&
                    goStruct.CompArraySize > 0 && goStruct.CompArraySize < 0x400)
                {
                    int compCount = (int)Math.Min(goStruct.CompArraySize, (ulong)GroundTruthMaxComponents);
                    var entries = new ComponentArrayEntry[compCount];
                    if (Mem.TryReadBuffer<ComponentArrayEntry>(goStruct.CompArrayBase, entries, false))
                    {
                        sb.AppendLine();
                        sb.AppendLine($"//     ── Components ({compCount}) — klass via IL2CPP whitelist scan of comp header ──");
                        sb.AppendLine($"//       idx  comp                 klassName (whitelist hit)");
                        ulong firstTransformLike = 0;
                        string firstTransformKlass = "";
                        var compKlassSet = _klassSet;
                        for (int i = 0; i < compCount; i++)
                        {
                            ulong comp = entries[i].Component;
                            string kname = "<?>";
                            if (Mem.IsValidVirtualAddress(comp) && compKlassSet is not null)
                            {
                                Span<byte> ch = stackalloc byte[0x40];
                                if (Mem.TryReadBuffer<byte>(comp, ch, false))
                                {
                                    for (int o = 0; o + 8 <= ch.Length; o += 8)
                                    {
                                        ulong q = BitConverter.ToUInt64(ch[o..(o + 8)]);
                                        if (!compKlassSet.Contains(q)) continue;
                                        kname = SafeReadKlassName(q) ?? "<no-name>";
                                        break;
                                    }
                                }
                                if (firstTransformLike == 0 &&
                                    (kname == "Transform" || kname == "RectTransform"))
                                {
                                    firstTransformLike = comp;
                                    firstTransformKlass = kname;
                                }
                            }
                            sb.AppendLine($"//       [{i,2}]  0x{comp:X16}  {kname}");
                        }

                        // Speculative back-pointer check.
                        if (compCount > 0 && Mem.IsValidVirtualAddress(entries[0].Component) &&
                            Mem.TryReadValue<ulong>(entries[0].Component + _host.CompGameObject, out var backGo, false))
                        {
                            bool ok = backGo == go;
                            sb.AppendLine($"//     comp[0] + Comp_GameObject(0x{_host.CompGameObject:X}) = 0x{backGo:X}  {(ok ? "✓ matches GO (Comp_GameObject likely correct)" : "✗ MISMATCH (Comp_GameObject likely wrong)")}");
                        }

                        // ── Probe Comp_GameObject + Comp_ObjectClass on comp[0] ───
                        //   comp[0] is a real Component pointer — brute-force the
                        //   first 0x80 bytes for (a) a qword == GO (back-pointer)
                        //   and (b) a qword whose +0x10 chain reads ASCII (klass
                        //   name). This locates Comp_GameObject and Comp_ObjectClass
                        //   for this build.
                        if (compCount > 0 && Mem.IsValidVirtualAddress(entries[0].Component))
                        {
                            ulong comp0 = entries[0].Component;
                            sb.AppendLine();
                            sb.AppendLine($"//     ── comp[0] @ 0x{comp0:X} brute-force offset probe ──");
                            DumpInstance(sb, "comp[0] (raw window)", comp0, 0x80);

                            Span<byte> cbuf = stackalloc byte[0x80];
                            if (Mem.TryReadBuffer<byte>(comp0, cbuf, false))
                            {
                                int backOff = -1;
                                int klassOff = -1;
                                string klassName = "";
                                var klassSet = _klassSet;
                                for (int o = 0; o + 8 <= cbuf.Length; o += 8)
                                {
                                    ulong q = BitConverter.ToUInt64(cbuf[o..(o + 8)]);
                                    if (backOff < 0 && q == go) backOff = o;
                                    if (klassOff < 0 && klassSet is not null && klassSet.Contains(q))
                                    {
                                        klassOff = o;
                                        klassName = SafeReadKlassName(q) ?? "<no-name>";
                                    }
                                }
                                sb.AppendLine($"//     probe Comp_GameObject  : {(backOff >= 0 ? $"comp+0x{backOff:X} == GO ✓" : "not found in first 0x80")}  (current 0x{_host.CompGameObject:X})");
                                sb.AppendLine($"//     probe Comp_ObjectClass : {(klassOff >= 0 ? $"comp+0x{klassOff:X} -> klass -> name=\"{klassName}\" ✓" : "not found in first 0x80")}  (current 0x{_host.CompObjectClass:X})");

                                // Also list ALL whitelisted klass hits so we see
                                // every class pointer the component carries.
                                if (klassSet is not null)
                                {
                                    int extras = 0;
                                    for (int o = 0; o + 8 <= cbuf.Length && extras < 8; o += 8)
                                    {
                                        ulong q = BitConverter.ToUInt64(cbuf[o..(o + 8)]);
                                        if (!klassSet.Contains(q)) continue;
                                        var nm = SafeReadKlassName(q) ?? "<no-name>";
                                        sb.AppendLine($"//       comp+0x{o:X3}  -> klass 0x{q:X16}  name=\"{nm}\"");
                                        extras++;
                                    }
                                }

                                // ── Indirection probe ────────────────────────
                                //   In IL2CPP, the managed wrapper for a native
                                //   component is a SEPARATE allocation whose
                                //   +0x00 is the Il2CppClass*. The native comp
                                //   header holds a pointer to that wrapper.
                                //   So: for every pointer-shaped qword P in the
                                //   header, deref P and read [P+0x00], [P+0x08],
                                //   ... [P+0x18] looking for a whitelist hit.
                                //   The (compOff, wrapperOff) pair that is
                                //   consistent across all components reveals
                                //   the real Comp_ObjectClass chain.
                                if (klassSet is not null)
                                {
                                    sb.AppendLine($"//     ── indirection probe: comp+OFF -> [+0..+0x18] klass scan ──");
                                    int indirectHits = 0;
                                    for (int o = 0; o + 8 <= cbuf.Length && indirectHits < 12; o += 8)
                                    {
                                        ulong p = BitConverter.ToUInt64(cbuf[o..(o + 8)]);
                                        if (!Mem.IsValidVirtualAddress(p)) continue;
                                        if (klassSet.Contains(p)) continue; // already reported as direct
                                        Span<byte> wbuf = stackalloc byte[0x20];
                                        if (!Mem.TryReadBuffer<byte>(p, wbuf, false)) continue;
                                        for (int wo = 0; wo + 8 <= wbuf.Length; wo += 8)
                                        {
                                            ulong wq = BitConverter.ToUInt64(wbuf[wo..(wo + 8)]);
                                            if (!klassSet.Contains(wq)) continue;
                                            var nm = SafeReadKlassName(wq) ?? "<no-name>";
                                            sb.AppendLine($"//       comp+0x{o:X3} -> 0x{p:X} +0x{wo:X} -> klass 0x{wq:X}  name=\"{nm}\"");
                                            indirectHits++;
                                            break;
                                        }
                                    }
                                    if (indirectHits == 0)
                                        sb.AppendLine($"//       (no managed wrapper found via 1-level indirection)");
                                }
                            }
                        }

                        // CONFIRMED-chain validation: if we have a Transform-like
                        // component, follow TA.HierarchyOff -> TH -> TH.WorldPosition.
                        // A sane Vec3 here proves GO_Components + Comp_ObjectClass
                        // are also correct (since we got to a real Transform).
                        if (firstTransformLike != 0)
                        {
                            sb.AppendLine();
                            sb.AppendLine($"//     ── CONFIRMED TA/TH validation via {firstTransformKlass} @ 0x{firstTransformLike:X} ──");
                            DumpInstance(sb, $"{firstTransformKlass} (raw window)", firstTransformLike, 0x100);

                            if (Mem.TryReadValue<ulong>(firstTransformLike + _host.TaHierarchyOff, out var thPtr, false) &&
                                Mem.IsValidVirtualAddress(thPtr))
                            {
                                sb.AppendLine();
                                sb.AppendLine($"//     TransformHierarchy ptr (via TA.HierarchyOff=0x{_host.TaHierarchyOff:X}) = 0x{thPtr:X}");
                                if (Mem.TryReadValue<Vector3>(thPtr + _host.ThWorldPosition, out var wp))
                                {
                                    bool sane = !float.IsNaN(wp.X) && !float.IsInfinity(wp.X) &&
                                                Math.Abs(wp.X) < 1e6f && Math.Abs(wp.Y) < 1e6f && Math.Abs(wp.Z) < 1e6f;
                                    sb.AppendLine($"//     TH+0x{_host.ThWorldPosition:X} (TH.WorldPosition) = ({wp.X:F3}, {wp.Y:F3}, {wp.Z:F3})  {(sane ? "✓ sane Vec3 — UNCONFIRMED GO/Comp offsets are CORRECT for this GO" : "✗ junk — speculative GO/Comp offsets likely wrong")}");
                                    if (sane) validatedTransformChains++;
                                }
                                DumpInstance(sb, "TransformHierarchy (raw window)", thPtr, 0x100);
                            }
                            else
                            {
                                sb.AppendLine($"//     [TA.HierarchyOff(0x{_host.TaHierarchyOff:X}) does not deref — speculative Transform pointer suspect]");
                            }
                        }
                    }
                    else
                    {
                        sb.AppendLine("//     [component array unreadable]");
                    }
                }
                else if (gotStruct)
                {
                    sb.AppendLine("//     [speculative ComponentArray rejected (size/base out of range) — GO_Components likely wrong]");
                }

                dumped++;
            }

            sb.AppendLine();
            sb.AppendLine($"// ── Validation summary ─────────────────────────────────────────");
            sb.AppendLine($"//   GameObjects dumped              : {dumped}");
            sb.AppendLine($"//   Validated Transform chains      : {validatedTransformChains}");
            sb.AppendLine($"//   (each validation = unconfirmed GO_Components + Comp_ObjectClass produced a real Transform whose CONFIRMED TA/TH chain returned a sane Vec3)");

            // ── Native vtable histogram + Transform auto-detect ──────────────
            //   The walked components are NATIVE Unity engine objects. Each
            //   carries a UnityPlayer.dll vtable at comp+0x00. Across many
            //   components, the Transform vtable will repeat far more than
            //   any other (every GO has exactly one Transform). For each
            //   candidate vtable we test the CONFIRMED TA/TH chain — the one
            //   whose comp+TA.HierarchyOff dereferences to a TH whose
            //   +WorldPositionOffset is a sane Vec3 IS the Transform vtable.
            try
            {
                AppendNativeVtableHistogram(sb, gos);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"// vtable histogram failed: {ex.Message}");
            }

            // ── Camera section: CONFIRMED offsets, live values ───────────────
            sb.AppendLine();
            sb.AppendLine("// ── CONFIRMED Camera offsets (radar uses these) ──────────────");
            sb.AppendLine($"//   ViewMatrix   = 0x{_host.CameraViewMatrix:X}");
            sb.AppendLine($"//   FOV          = 0x{_host.CameraFov:X}");
            sb.AppendLine($"//   AspectRatio  = 0x{_host.CameraAspectRatio:X}");
            ulong liveCam = SafeGetLiveCamera();
            if (Mem.IsValidVirtualAddress(liveCam))
            {
                if (Mem.TryReadValue<float>(liveCam + _host.CameraFov, out var fov))
                    sb.AppendLine($"//   live FOV         = {fov:F3}");
                if (Mem.TryReadValue<float>(liveCam + _host.CameraAspectRatio, out var ar))
                    sb.AppendLine($"//   live AspectRatio = {ar:F3}");
            }
            else
            {
                sb.AppendLine("//   (no live FPSCamera pointer — values omitted)");
            }

            sb.AppendLine("// ═══ End ground-truth dump ═════════════════════════════════════════");
        }

        // ── GO/Comp dump driven by GOM (the *confirmed* anchor) ─────────────
        // Camera/VM/AR/FOV and TA/TH offsets are confirmed working at runtime,
        // so we don't need to re-discover them. GO_Components / GO_Name /
        // Comp_ObjectClass / Comp_GameObject are NOT confirmed for this engine
        // build, so we dump real engine data sourced from GOM (the verified
        // GameObjectManager) and let visual inspection / future code verify
        // those offsets.

        private const int MaxChainGameObjects = 4;   // dump this many named GOs (full hex)
        private const int MaxComponentRows    = 32;  // per GO
        private const int MaxNamedGoListing   = 100; // compact name listing only

        private static void AppendLiveChainDumps(StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("// ═══ GOM-driven GO / Component dump (offset verification) ══════════");

            ulong gomAddr = Mem.GomAddress;
            if (!Mem.IsValidVirtualAddress(gomAddr))
            {
                sb.AppendLine("// ── GOM not resolved yet — chain skipped ───────────────────");
                return;
            }

            sb.AppendLine($"// GOM address          : 0x{gomAddr:X}");

            // GOM struct head (Arena layout: LastActiveNode @0x20, ActiveNodes @0x28).
            DumpInstance(sb, "GOM head (first 0x100 bytes)", gomAddr, 0x100);

            // Two known GOM layouts exist in the wild — probe both and pick the
            // one whose ActiveNodes ptr leads to a LinkedListObject with a valid
            // ThisObject (i.e. an actual GameObject pointer).
            (uint LastOff, uint ActiveOff, string Tag)[] layouts =
            {
                (0x20u, 0x28u, "0x20/0x28"),
                (0x18u, 0x20u, "0x18/0x20"),
            };

            ulong activeNodes = 0;
            ulong lastActive  = 0;
            string layoutTag  = "?";
            sb.AppendLine();
            sb.AppendLine("// ── GOM layout probe (LastActiveNode / ActiveNodes) ─────────");
            foreach (var (lOff, aOff, tag) in layouts)
            {
                Mem.TryReadValue<ulong>(gomAddr + lOff, out var l, false);
                Mem.TryReadValue<ulong>(gomAddr + aOff, out var a, false);
                bool ok = Mem.IsValidVirtualAddress(l) &&
                          Mem.IsValidVirtualAddress(a) &&
                          Mem.TryReadValue<ulong>(a + 0x10, out var firstThis, false) &&
                          Mem.IsValidVirtualAddress(firstThis);
                sb.AppendLine($"//   {tag,-10}  last=0x{l:X}  active=0x{a:X}  {(ok ? "✓ valid" : "(no)")}");
                if (ok && activeNodes == 0)
                {
                    lastActive  = l;
                    activeNodes = a;
                    layoutTag   = tag;
                }
            }

            if (activeNodes == 0)
            {
                sb.AppendLine("// ── No GOM layout produced a valid ActiveNodes — chain skipped ───");
                return;
            }

            sb.AppendLine();
            sb.AppendLine($"// GOM layout selected : {layoutTag}");
            sb.AppendLine($"// GOM.LastActiveNode  : 0x{lastActive:X}");
            sb.AppendLine($"// GOM.ActiveNodes     : 0x{activeNodes:X}");

            // Walk the active list — collect named GameObjects so the dump is
            // grounded in real engine data and not random uninitialized slots.
            sb.AppendLine();
            sb.AppendLine("// ── GOM list walk (first GameObjects with readable names) ───");

            // Phase 1: collect candidate GameObject pointers (no name probing yet —
            // GO_Name offset is not trusted on this build). 256 GOs lets us reach past
            // the UI block at the head of the GOM active list (EventSystem / Canvas /
            // LayoutGroup) and into world-space objects whose Transform components
            // actually populate TA.HierarchyOffset.
            const int maxGoCandidates = 256;
            var goCandidates = new List<ulong>(maxGoCandidates);
            ulong sweep = activeNodes;
            for (int i = 0; i < 8192 && goCandidates.Count < maxGoCandidates; i++)
            {
                if (!Mem.IsValidVirtualAddress(sweep)) break;
                if (!Mem.TryReadValue<ulong>(sweep + 0x08, out var nextLink, false)) break;
                if (Mem.TryReadValue<ulong>(sweep + 0x10, out var thisGo, false) &&
                    Mem.IsValidVirtualAddress(thisGo))
                {
                    goCandidates.Add(thisGo);
                }
                if (nextLink == 0 || nextLink == sweep) break;
                sweep = nextLink;
            }

            sb.AppendLine($"// Collected {goCandidates.Count} GameObject candidate(s) for offset probing");

            // Phase 2: brute-force GO_Name offset across +0x40..+0xE0 in 8-byte
            // strides. For each candidate offset, deref once and score by readable
            // ASCII run length across the candidate set.
            int bestNameOff   = -1;
            int bestNameScore = 0;
            var nameProbes    = new System.Text.StringBuilder();
            Span<byte> nameBuf = stackalloc byte[32];
            for (int off = 0x40; off <= 0xE0; off += 8)
            {
                int hits = 0;
                int totalLen = 0;
                foreach (var go in goCandidates)
                {
                    if (!Mem.TryReadValue<ulong>(go + (ulong)off, out var p, false)) continue;
                    if (!Mem.IsValidVirtualAddress(p)) continue;
                    if (!Mem.TryReadBuffer<byte>(p, nameBuf, false)) continue;
                    int len = 0;
                    while (len < nameBuf.Length && nameBuf[len] >= 0x20 && nameBuf[len] < 0x7F) len++;
                    if (len >= 3 && len < nameBuf.Length) { hits++; totalLen += len; }
                }
                if (hits > 0)
                {
                    int score = hits * 16 + totalLen;
                    nameProbes.AppendLine($"//   GO+0x{off:X2}  hits={hits}/{goCandidates.Count}  totalLen={totalLen}  score={score}");
                    if (score > bestNameScore) { bestNameScore = score; bestNameOff = off; }
                }
            }

            sb.AppendLine();
            sb.AppendLine("// ── GO name-offset probe (deref once, ASCII run >= 3) ──────");
            sb.Append(nameProbes.ToString());
            if (bestNameOff >= 0)
                sb.AppendLine($"// → best GO name offset: 0x{bestNameOff:X}  (current _host.GoName = 0x{_host.GoName:X})");
            else
                sb.AppendLine("// → no GO name offset produced any readable strings");

            // ════════════════════════════════════════════════════════════════
            // Chained offset resolver. Inputs: GOM only. Derives:
            //   GO_ObjectClass, GO_Components (with stride & instOff),
            //   Comp_ObjectClass, Comp_GameObject — keeping ranked candidates.
            // ════════════════════════════════════════════════════════════════

            // ── Phase A: GO_ObjectClass — slot whose [deref → +0x10 → ASCII]
            //    reads "GameObject". Same Klass→name chain Il2Cpp uses.
            sb.AppendLine();
            sb.AppendLine("// ── GO_ObjectClass probe (slot whose klass-name == \"GameObject\") ──");
            var goObjClassCandidates = new List<(int Off, int Hits)>();
            Span<byte> ocNameBuf = stackalloc byte[24];
            for (int off = 0; off <= 0xC0; off += 8)
            {
                int hits = 0;
                foreach (var go in goCandidates)
                {
                    if (!Mem.TryReadValue<ulong>(go + (ulong)off, out var oc, false)) continue;
                    if (!Mem.IsValidVirtualAddress(oc)) continue;
                    if (!Mem.TryReadValue<ulong>(oc + 0x10, out var knp, false)) continue;
                    if (!Mem.IsValidVirtualAddress(knp)) continue;
                    if (!Mem.TryReadBuffer<byte>(knp, ocNameBuf, false)) continue;
                    if (ocNameBuf[0] != (byte)'G') continue; // fast-fail
                    int len = 0;
                    while (len < ocNameBuf.Length && ocNameBuf[len] >= 0x20 && ocNameBuf[len] < 0x7F) len++;
                    if (len < 3 || len >= ocNameBuf.Length) continue;
                    var s = System.Text.Encoding.ASCII.GetString(ocNameBuf[..len]);
                    if (s == "GameObject") hits++;
                }
                if (hits > 0) goObjClassCandidates.Add((off, hits));
            }
            goObjClassCandidates.Sort((a, b) => b.Hits.CompareTo(a.Hits));
            foreach (var (off, hits) in goObjClassCandidates)
                sb.AppendLine($"//   GO+0x{off:X2}  klass-name==\"GameObject\" hits={hits}/{goCandidates.Count}");
            int goObjClassOff = goObjClassCandidates.Count > 0 ? goObjClassCandidates[0].Off : (int)_host.GoObjectClass;
            sb.AppendLine($"// → GO_ObjectClass = 0x{goObjClassOff:X}  (current _host.GoObjectClass = 0x{_host.GoObjectClass:X})");

            // ── Phase B: GO_Components — for every (off, stride, instOff)
            //    candidate triple, accept any valid component pointer (= a
            //    non-self pointer that itself dereferences cleanly). We do NOT
            //    require Comp_ObjectClass to be known yet — instead, for each
            //    array slot we compute the FIRST valid component pointer and
            //    store it for Phase C/D to inspect.
            // Phase B retains the full pair list per candidate so Phase C/D can
            // operate without re-reading any GOs (DMA cache-friendly).
            var compCandidates = new List<(int Off, int Stride, int InstOff, ulong SampleComp, ulong SampleParentGo, List<(ulong comp, ulong go)> Pairs)>();
            {
                (int stride, int instOff)[] sh = { (0x10, 0x08), (0x20, 0x10), (0x08, 0x00) };
                Span<byte> compHead = stackalloc byte[0x40];
                for (int off = 0x20; off <= 0xC0; off += 8)
                {
                    foreach (var (stride, instOff) in sh)
                    {
                        var pairs = new List<(ulong comp, ulong go)>(goCandidates.Count);
                        ulong sampleComp = 0, sampleParent = 0;
                        foreach (var go in goCandidates)
                        {
                            if (!Mem.TryReadValue<ulong>(go + (ulong)off, out var arr, false)) continue;
                            if (!Mem.IsValidVirtualAddress(arr) || arr == go) continue;
                            ulong firstValid = 0;
                            for (int i = 0; i < 4; i++)
                            {
                                if (!Mem.TryReadValue<ulong>(arr + (ulong)(i * stride + instOff), out var c, false)) break;
                                if (!Mem.IsValidVirtualAddress(c) || c == go || c == arr) continue;
                                // sanity: comp must itself contain at least one valid pointer in its first 0x40 bytes
                                if (!Mem.TryReadBuffer<byte>(c, compHead, false)) continue;
                                bool hasPtr = false;
                                for (int p = 0; p + 8 <= compHead.Length; p += 8)
                                {
                                    ulong q = BitConverter.ToUInt64(compHead[p..(p + 8)]);
                                    if (q != 0 && (q >> 32) != 0 && Mem.IsValidVirtualAddress(q)) { hasPtr = true; break; }
                                }
                                if (!hasPtr) continue;
                                firstValid = c;
                                if (sampleComp == 0) { sampleComp = c; sampleParent = go; }
                                break;
                            }
                            if (firstValid != 0) pairs.Add((firstValid, go));
                        }
                        if (pairs.Count >= 3)
                            compCandidates.Add((off, stride, instOff, sampleComp, sampleParent, pairs));
                    }
                }
                compCandidates.Sort((a, b) => b.Pairs.Count.CompareTo(a.Pairs.Count));
            }

            sb.AppendLine();
            sb.AppendLine("// ── GO_Components probe (any valid comp ptr, >=3 GOs hit) ──");
            foreach (var c in compCandidates)
                sb.AppendLine($"//   GO+0x{c.Off:X2}  stride=0x{c.Stride:X}  inst=+0x{c.InstOff:X}  GOs={c.Pairs.Count}/{goCandidates.Count}  sample={c.SampleComp:X}");
            if (compCandidates.Count == 0)
                sb.AppendLine("//   (no candidate produced ≥3 valid component pointers)");

            // ── Phase C/D: derive Comp_ObjectClass & Comp_GameObject from the
            //    pair lists Phase B already built — no extra GO reads.
            sb.AppendLine();
            sb.AppendLine("// ── Comp_ObjectClass / Comp_GameObject derivation per GO_Components candidate ──");
            (int CompOff, int Stride, int InstOff, int CompObjClassOff, int CompGoOff, int Score) bestChain = (-1, 0, 0, 0, 0, 0);
            foreach (var c in compCandidates)
            {
                var pairs = c.Pairs;

                // Comp_ObjectClass sweep.
                // Note: offset +0x00 will trivially hit on every IL2CPP-managed component
                // (it's the Il2CppObject->klass head pointer). The *native* Unity
                // Component->m_Klass slot is further in the struct, so we prefer the
                // highest offset that still hits >= 80% of pairs. The +0x00 head is kept
                // only as a fallback if nothing else qualifies.
                int bestOcOff = -1, bestOcHits = 0;
                int preferredOcOff = -1, preferredOcHits = 0;
                int qualifyThreshold = (int)Math.Ceiling(pairs.Count * 0.80);
                var ocLines = new System.Text.StringBuilder();
                // The IL2CPP class-name chain is TWO indirections:
                //   ObjectClass → +0x00 (klass*) → +0x10 (namePtr) → ASCII
                // Reading namePtr at oc+0x10 directly (single deref) is wrong and
                // causes the real native Comp_ObjectClass slot (e.g. comp+0x20)
                // to score near-zero, which lets bogus offsets win the chain
                // probe. Use the host's ReadIl2CppClassName callback (which
                // walks ObjClass_ToNamePtr correctly) and fall back to a raw
                // probe only if the host hasn't wired one.
                Span<byte> ocBuf = stackalloc byte[40];
                for (int off = 0; off <= 0x40; off += 8)
                {
                    int hits = 0;
                    foreach (var (comp, _) in pairs)
                    {
                        if (!Mem.TryReadValue<ulong>(comp + (ulong)off, out var oc, true)) continue;
                        if (!Mem.IsValidVirtualAddress(oc)) continue;
                        if (_host.ReadIl2CppClassName != null)
                        {
                            var nm = SafeReadKlassName(oc);
                            if (!string.IsNullOrEmpty(nm) && nm.Length >= 3 && nm.Length <= 40 &&
                                IsCleanIdentifier(nm)) hits++;
                            continue;
                        }
                        // Fallback: walk the chain manually (oc → +0x0 → +0x10 → ASCII)
                        if (!Mem.TryReadPtr(oc, out var klass, true)) continue;
                        if (!Mem.IsValidVirtualAddress(klass)) continue;
                        if (!Mem.TryReadValue<ulong>(klass + 0x10, out var knp, true)) continue;
                        if (!Mem.IsValidVirtualAddress(knp)) continue;
                        if (!Mem.TryReadBuffer<byte>(knp, ocBuf, true)) continue;
                        int len = 0;
                        bool clean = true;
                        while (len < ocBuf.Length && ocBuf[len] >= 0x20 && ocBuf[len] < 0x7F)
                        {
                            byte ch = ocBuf[len];
                            bool ok = (ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') ||
                                      (ch >= '0' && ch <= '9') || ch == '_' || ch == '<' || ch == '>' || ch == '`';
                            if (!ok) { clean = false; break; }
                            len++;
                        }
                        if (clean && len >= 3 && len <= 40) hits++;
                    }
                    if (hits > 0) ocLines.AppendLine($"//       comp+0x{off:X2}  klass-chain hits={hits}/{pairs.Count}");
                    if (hits > bestOcHits) { bestOcHits = hits; bestOcOff = off; }
                    // Track the highest non-zero offset that qualifies (>= 80%) — this
                    // skips the IL2CPP head trap at +0x00 in favour of the native slot.
                    if (off >= 0x10 && hits >= qualifyThreshold && off > preferredOcOff)
                    {
                        preferredOcOff = off;
                        preferredOcHits = hits;
                    }
                }
                if (preferredOcOff >= 0)
                {
                    bestOcOff = preferredOcOff;
                    bestOcHits = preferredOcHits;
                }

                // Self-reference trap: if every "component" at the chosen OC offset
                // resolves to klass "GameObject", this chain is actually a GameObject*[]
                // (e.g. a name-table or self-referential m_Children array), not a
                // Component*[]. Such chains can artificially win scoring because
                // comp+InstOff trivially equals the parent GO. Disqualify them.
                int gameObjectKlassHits = 0;
                int klassSampleCount = 0;
                if (bestOcOff >= 0)
                {
                    int sampleCap = Math.Min(pairs.Count, 64);
                    for (int i = 0; i < sampleCap; i++)
                    {
                        var (comp, _) = pairs[i];
                        if (!Mem.TryReadValue<ulong>(comp + (ulong)bestOcOff, out var oc, true)) continue;
                        if (!Mem.IsValidVirtualAddress(oc)) continue;
                        var nm = SafeReadKlassName(oc);
                        if (string.IsNullOrEmpty(nm)) continue;
                        klassSampleCount++;
                        if (nm == "GameObject") gameObjectKlassHits++;
                    }
                }
                // ≥25% GameObject-klass entries = self-ref trap (real Component*[] is 0%)
                bool selfRefTrap = klassSampleCount >= 4 && gameObjectKlassHits * 4 >= klassSampleCount;

                // Comp_GameObject sweep
                int bestGoOff = -1, bestGoHits = 0;
                var goLines = new System.Text.StringBuilder();
                for (int off = 0; off <= 0x80; off += 8)
                {
                    int hits = 0;
                    foreach (var (comp, parent) in pairs)
                    {
                        if (!Mem.TryReadValue<ulong>(comp + (ulong)off, out var v, true)) continue;
                        if (v == parent) hits++;
                    }
                    if (hits > 0) goLines.AppendLine($"//       comp+0x{off:X2}  ==parent-GO hits={hits}/{pairs.Count}");
                    if (hits > bestGoHits) { bestGoHits = hits; bestGoOff = off; }
                }

                int chainScore = bestOcHits + bestGoHits;
                if (selfRefTrap) chainScore = 0;
                // Require meaningful klass-chain coverage: a real Component*[] should
                // resolve a clean klass name on a nontrivial share of entries. Without
                // this guard, a structure where comp+InstOff trivially equals parent-GO
                // (e.g. an internal wrapper list) wins on GO-hits alone with only 1-2
                // valid klass-chain hits, and Phase E sees no Transforms. 3% is enough
                // to reject those near-zero-klass chains while keeping real ones, since
                // many native Components aren't IL2CPP-managed and won't resolve here.
                int ocCoverageThreshold = Math.Max(3, (int)Math.Ceiling(pairs.Count * 0.03));
                bool weakKlass = bestOcHits < ocCoverageThreshold;
                if (weakKlass) chainScore = 0;
                sb.AppendLine($"//   ▸ GO+0x{c.Off:X2} stride=0x{c.Stride:X} inst=+0x{c.InstOff:X}  (pairs={pairs.Count})");
                sb.Append(ocLines.ToString());
                sb.Append(goLines.ToString());
                sb.AppendLine($"//     → Comp_ObjectClass=0x{(bestOcOff < 0 ? 0 : bestOcOff):X}({bestOcHits}h) Comp_GameObject=0x{(bestGoOff < 0 ? 0 : bestGoOff):X}({bestGoHits}h)  score={chainScore}  [trap: GO-klass={gameObjectKlassHits}/{klassSampleCount}]{(selfRefTrap ? "  (self-ref trap: GameObject*[])" : "")}{(weakKlass ? "  (weak klass coverage)" : "")}");

                // Prefer the canonical Unity dynamic_array<Component*> shape on ties:
                //   stride=0x10 inst=+0x8  is the real layout (ptr at +8, padding/ref at 0).
                //   stride=0x08 inst=+0x0  also passes scoring because it picks up the
                //   same pointers, but at half stride it interleaves garbage half-qwords
                //   into the iterator — which corrupts Phase E (Transforms become path
                //   metadata structs). Treat 0x10/+0x8 as the better candidate when
                //   chainScore matches.
                bool isCanonical(int stride, int instOff) => stride == 0x10 && instOff == 0x8;
                bool replace =
                    chainScore > bestChain.Score ||
                    (chainScore == bestChain.Score && isCanonical(c.Stride, c.InstOff) && !isCanonical(bestChain.Stride, bestChain.InstOff));

                if (replace && bestOcOff >= 0 && bestGoOff >= 0)
                {
                    bestChain = (c.Off, c.Stride, c.InstOff, bestOcOff, bestGoOff, chainScore);
                }
            }

            sb.AppendLine();
            if (bestChain.Score > 0)
            {
                int resolvedGoName = bestNameOff >= 0 ? bestNameOff : (int)_host.GoName;
                _resolvedGoObjectClass  = (uint)goObjClassOff;
                _resolvedGoName         = (uint)resolvedGoName;
                _resolvedGoComponents   = (uint)bestChain.CompOff;
                _resolvedCompObjectClass = (uint)bestChain.CompObjClassOff;
                _resolvedCompGameObject  = (uint)bestChain.CompGoOff;
                _resolvedCompStride      = bestChain.Stride;
                _resolvedCompInstOff     = bestChain.InstOff;

                sb.AppendLine("// ── Final resolved offsets ──────────────────────────────────");
                sb.AppendLine($"//   GO_ObjectClass   = 0x{goObjClassOff:X}  (was 0x{_host.GoObjectClass:X})");
                sb.AppendLine($"//   GO_Name          = 0x{resolvedGoName:X}  (was 0x{_host.GoName:X})");
                sb.AppendLine($"//   GO_Components    = 0x{bestChain.CompOff:X}  stride=0x{bestChain.Stride:X}  inst=+0x{bestChain.InstOff:X}  (was 0x{_host.GoComponents:X})");
                sb.AppendLine($"//   Comp_ObjectClass = 0x{bestChain.CompObjClassOff:X}  (was 0x{_host.CompObjectClass:X})");
                sb.AppendLine($"//   Comp_GameObject  = 0x{bestChain.CompGoOff:X}  (was 0x{_host.CompGameObject:X})");
            }
            else
            {
                sb.AppendLine("// ── Could not derive a complete chain — falling back to UnityOffsets ──");
            }

            // ── Phase E: Transform discovery (TA / TH candidates) ─────────────
            //   Walk the resolved component arrays, classify each component by its
            //   klass-name (read via Comp_ObjectClass → +0x10 → ASCII), and look for
            //   a "Transform" / "RectTransform". From that Transform pointer the
            //   TransformAccess (TA) and TransformHierarchy (TH) layouts can be
            //   visualized so the developer can pick TA / TH offsets manually.
            if (bestChain.Score > 0)
            {
                try { AppendTransformProbe(sb, goCandidates, bestChain.CompOff, bestChain.Stride, bestChain.InstOff, bestChain.CompObjClassOff); }
                catch (Exception ex) { sb.AppendLine($"// ── Transform probe failed: {ex.Message} ──"); }
            }

            // Phase 4: dump the first few GameObjects using the chain-resolved offsets.
            int nameOff = bestNameOff >= 0 ? bestNameOff : (int)_host.GoName;
            int compOff    = bestChain.CompOff > 0 ? bestChain.CompOff : (int)_host.GoComponents;
            int compStride = bestChain.Stride  > 0 ? bestChain.Stride  : 0x10;
            int compInst   = bestChain.InstOff >= 0 && bestChain.Score > 0 ? bestChain.InstOff : 0x08;
            int compObjClassOff = bestChain.CompObjClassOff > 0 ? bestChain.CompObjClassOff : (int)_host.CompObjectClass;

            // ── GOM name listing (compact: up to MaxNamedGoListing GOs) ───────
            //   Helps eyeball which named roots ("GameWorld", "FPS Camera",
            //   "TarkovApplication", etc.) are reachable on the live build
            //   without needing the full per-GO hex dump that follows.
            sb.AppendLine();
            sb.AppendLine($"// ── GOM name listing (first {Math.Min(MaxNamedGoListing, goCandidates.Count)} of {goCandidates.Count} candidates) ──");
            sb.AppendLine($"//   idx  go                   compSize  name");
            int listed = 0;
            foreach (var thisGo in goCandidates)
            {
                if (listed >= MaxNamedGoListing) break;
                string goName = TryReadAsciiAt(thisGo, (uint)nameOff) ?? "<no-name>";
                ulong compSize = 0;
                if (compOff > 0)
                    Mem.TryReadValue<ulong>(thisGo + (ulong)compOff + 0x10, out compSize, false);
                if (compSize > 0xFFFF) compSize = 0;
                sb.AppendLine($"//   [{listed,3}] 0x{thisGo:X16}  {compSize,6}    \"{goName}\"");
                listed++;
            }

            sb.AppendLine();
            sb.AppendLine($"// ── Dumping first {MaxChainGameObjects} GameObject(s) using name=0x{nameOff:X}, components=0x{compOff:X} (stride=0x{compStride:X},+0x{compInst:X}) ───");

            int dumped = 0;
            foreach (var thisGo in goCandidates)
            {
                if (dumped >= MaxChainGameObjects) break;
                string goName = TryReadAsciiAt(thisGo, (uint)nameOff) ?? "<no-name>";
                sb.AppendLine();
                sb.AppendLine($"// ▼ GameObject [{dumped}] @ 0x{thisGo:X}  name=\"{goName}\"");
                DumpInstance(sb, $"GameObject \"{goName}\"", thisGo, 0x100);
                DumpComponentsVectorAt(sb, thisGo, (uint)compOff, compStride, compInst);
                dumped++;
            }

            if (dumped == 0)
            {
                sb.AppendLine("// ── Walked GOM but found no GameObject candidates ───");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine($"// ── GOM dump complete: {dumped} GameObject(s) emitted ─────");
            }
        }

        // Phase E: walk components of every candidate GO, classify by klass-name,
        // collect many Transform samples, and statistically resolve TA / TH offsets.
        //   TA layout (TransformAccess):
        //     +TA.HierarchyOffset  -> ptr to TransformHierarchy (shared by sibling transforms)
        //     +TA.IndexOffset      -> uint32 small index into the hierarchy's vertex array
        //   TH layout (TransformHierarchy):
        //     +TH.WorldPositionOffset  -> Vec3 (sane world-space coord)
        //     +TH.WorldRotationOffset  -> Quat (unit-norm)
        //     +TH.VerticesOffset       -> dynamic_array<TransformVertex> (ptr + count + cap)
        //     +TH.IndicesOffset        -> dynamic_array<int> (ptr + count + cap)
        private static void AppendTransformProbe(
            StringBuilder sb,
            List<ulong> goCandidates,
            int compOff, int compStride, int compInstOff, int compObjClassOff)
        {
            sb.AppendLine();
            sb.AppendLine("// ── Phase E: Transform discovery (for TA / TH offset analysis) ──");

            var classCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            // Track plain Transforms separately from RectTransforms — UI RectTransforms
            // tend to have null/different hierarchy fields, so a plain Transform sample
            // produces much cleaner TA / TH offset signal.
            var plainTransforms = new List<ulong>(256);
            var rectTransforms  = new List<ulong>(256);

            foreach (var go in goCandidates)
            {
                if (!Mem.TryReadValue<ulong>(go + (ulong)compOff, out var arr, true)) continue;
                if (!Mem.IsValidVirtualAddress(arr)) continue;

                Mem.TryReadValue<uint>(go + 0x48, out var sz, true);
                int count = (int)Math.Min(sz == 0 ? 16 : sz, 32u);

                for (int i = 0; i < count; i++)
                {
                    ulong slot = arr + (ulong)(i * compStride) + (ulong)compInstOff;
                    if (!Mem.TryReadValue<ulong>(slot, out var comp, true)) continue;
                    if (!Mem.IsValidVirtualAddress(comp)) continue;
                    if (!Mem.TryReadValue<ulong>(comp + (ulong)compObjClassOff, out var klass, true)) continue;
                    if (!Mem.IsValidVirtualAddress(klass)) continue;

                    string? name = TryReadAsciiAt(klass, 0x10);
                    if (string.IsNullOrEmpty(name)) continue;

                    if (!classCounts.TryGetValue(name, out var c)) c = 0;
                    classCounts[name] = c + 1;

                    if (name.Equals("Transform", StringComparison.Ordinal) && plainTransforms.Count < 256)
                        plainTransforms.Add(comp);
                    else if (name.Equals("RectTransform", StringComparison.Ordinal) && rectTransforms.Count < 256)
                        rectTransforms.Add(comp);
                }
            }

            sb.AppendLine($"//   Component klass histogram (top 16 of {classCounts.Count}):");
            foreach (var kv in classCounts.OrderByDescending(k => k.Value).Take(16))
                sb.AppendLine($"//     {kv.Value,4}x  {kv.Key}");
            sb.AppendLine($"//   Transforms collected: plain={plainTransforms.Count}  rect={rectTransforms.Count}");

            // Prefer plain Transforms; if too few (< 5), fall back to combined set.
            List<ulong> transforms;
            string sampleSetTag;
            if (plainTransforms.Count >= 5)
            {
                transforms = plainTransforms;
                sampleSetTag = "plain Transform";
            }
            else
            {
                transforms = new List<ulong>(plainTransforms);
                transforms.AddRange(rectTransforms);
                sampleSetTag = "Transform+RectTransform (plain too few)";
            }

            if (transforms.Count == 0)
            {
                sb.AppendLine("//   No Transform / RectTransform component found — TA/TH discovery skipped.");
                return;
            }

            sb.AppendLine();
            sb.AppendLine($"//   Sample {sampleSetTag} @ 0x{transforms[0]:X}  (probing {transforms.Count} transforms)");
            DumpInstance(sb, sampleSetTag, transforms[0], 0x200);

            // ── TA.HierarchyOffset ────────────────────────────────────────────
            //   Sibling transforms in the same GO sub-tree share one TransformHierarchy.
            //   Score each candidate offset by the size of the largest pointer-frequency
            //   bucket: real TA.HierarchyOffset shows ONE pointer hit by many transforms.
            //   Reject klass / type-info traps by requiring the target struct to contain
            //   at least 6 reasonable floats (Vec3 + Quat) in its first 0x80 bytes.
            sb.AppendLine();
            sb.AppendLine("//   ── TA.HierarchyOffset probe (most-shared pointer slot, target has floats) ──");
            int bestTaHierOff = -1, bestTaHierShare = 0;
            ulong bestTaHierPtr = 0;
            Span<byte> hierHead = stackalloc byte[0x80];
            for (int off = 0; off <= 0x1F8; off += 8)
            {
                var freq = new Dictionary<ulong, int>(transforms.Count);
                int valid = 0;
                foreach (var t in transforms)
                {
                    if (!Mem.TryReadValue<ulong>(t + (ulong)off, out var p, true)) continue;
                    if (!Mem.IsValidVirtualAddress(p) || p == t) continue;
                    valid++;
                    freq[p] = freq.TryGetValue(p, out var cc) ? cc + 1 : 1;
                }
                if (valid < transforms.Count / 2) continue;
                int top = 0; ulong topPtr = 0;
                foreach (var kv in freq) if (kv.Value > top) { top = kv.Value; topPtr = kv.Key; }
                int distinct = freq.Count;
                if (top < 4 || distinct >= transforms.Count * 0.6) continue;

                // Klass-trap filter: target struct must have several reasonable floats
                // in its first 0x80 (a real TransformHierarchy has Vec3 + Quat early).
                if (!Mem.TryReadBuffer<byte>(topPtr, hierHead, true)) continue;
                int floats = 0;
                for (int i = 0; i + 4 <= 0x80; i += 4)
                {
                    float f = BitConverter.ToSingle(hierHead[i..(i + 4)]);
                    if (float.IsNaN(f) || float.IsInfinity(f)) continue;
                    if (f == 0f) continue;
                    if (Math.Abs(f) > 1e5f || Math.Abs(f) < 1e-6f) continue;
                    floats++;
                }
                if (floats < 6)
                {
                    sb.AppendLine($"//     transform+0x{off:X2}  topPtr=0x{topPtr:X}  share={top}/{transforms.Count}  distinct={distinct}  floats={floats}  (klass-trap, skipped)");
                    continue;
                }

                if (top > bestTaHierShare)
                {
                    bestTaHierShare = top; bestTaHierOff = off; bestTaHierPtr = topPtr;
                }
                sb.AppendLine($"//     transform+0x{off:X2}  topPtr=0x{topPtr:X}  share={top}/{transforms.Count}  distinct={distinct}  floats={floats}");
            }
            if (bestTaHierOff < 0)
                sb.AppendLine("//     (no candidate found — every shared-pointer slot was a klass-trap)");

            // ── TA.IndexOffset ────────────────────────────────────────────────
            //   Small uint32 index into TH's vertex array. Score by being a small
            //   non-negative int (< 4096) for most transforms AND being unique-ish
            //   per transform that share the same TH.
            sb.AppendLine();
            sb.AppendLine("//   ── TA.IndexOffset probe (small-uint that varies per transform) ──");
            int bestTaIdxOff = -1, bestTaIdxScore = 0;
            for (int off = 0; off <= 0x1FC; off += 4)
            {
                int small = 0;
                var seen = new HashSet<uint>();
                foreach (var t in transforms)
                {
                    if (!Mem.TryReadValue<uint>(t + (ulong)off, out var v, true)) continue;
                    if (v < 4096) { small++; seen.Add(v); }
                }
                if (small < transforms.Count * 0.7) continue;
                // Reject pointer/zero slots: must vary across transforms.
                if (seen.Count < 4) continue;
                int score = small + seen.Count;
                if (score > bestTaIdxScore)
                {
                    bestTaIdxScore = score; bestTaIdxOff = off;
                    sb.AppendLine($"//     transform+0x{off:X2}  small={small}/{transforms.Count}  distinct={seen.Count}");
                }
            }
            if (bestTaIdxOff < 0)
                sb.AppendLine("//     (no plausible small-uint index slot)");

            // ── TH.* via the resolved TransformHierarchy ──────────────────────
            int bestThPos = -1, bestThRot = -1, bestThVerts = -1, bestThIdx = -1;
            ulong thPtr = bestTaHierPtr;
            if (bestTaHierOff >= 0 && thPtr != 0)
            {
                sb.AppendLine();
                sb.AppendLine($"//   Sample TransformHierarchy @ 0x{thPtr:X}");
                DumpInstance(sb, "TransformHierarchy", thPtr, 0x100);

                // Collect distinct TH pointers for cross-validation.
                var thSet = new HashSet<ulong>();
                foreach (var t in transforms)
                {
                    if (!Mem.TryReadValue<ulong>(t + (ulong)bestTaHierOff, out var p, true)) continue;
                    if (Mem.IsValidVirtualAddress(p)) thSet.Add(p);
                }

                // TH.WorldRotation: quaternion w/ |q| ≈ 1.0 in most THs.
                sb.AppendLine();
                sb.AppendLine("//   ── TH.WorldRotation probe (quaternion with |q| ≈ 1.0) ──");
                int bestRotHits = 0;
                for (int off = 0; off + 16 <= 0x100; off += 4)
                {
                    int hits = 0;
                    foreach (var th in thSet)
                    {
                        Span<byte> q = stackalloc byte[16];
                        if (!Mem.TryReadBuffer<byte>(th + (ulong)off, q, true)) continue;
                        float x = BitConverter.ToSingle(q[..4]);
                        float y = BitConverter.ToSingle(q[4..8]);
                        float z = BitConverter.ToSingle(q[8..12]);
                        float w = BitConverter.ToSingle(q[12..16]);
                        if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z) || float.IsNaN(w)) continue;
                        double n = Math.Sqrt((double)x * x + (double)y * y + (double)z * z + (double)w * w);
                        if (n > 0.98 && n < 1.02) hits++;
                    }
                    if (hits >= Math.Max(3, thSet.Count / 2) && hits > bestRotHits)
                    {
                        bestRotHits = hits; bestThRot = off;
                        sb.AppendLine($"//     th+0x{off:X2}  unit-quat hits={hits}/{thSet.Count}");
                    }
                }

                // TH.WorldPosition: Vec3, finite, |x|,|y|,|z| < 1e5, not all zero, NOT overlapping rotation.
                sb.AppendLine();
                sb.AppendLine("//   ── TH.WorldPosition probe (finite Vec3, sane range) ──");
                int bestPosHits = 0;
                for (int off = 0; off + 12 <= 0x100; off += 4)
                {
                    if (bestThRot >= 0 && off >= bestThRot - 8 && off <= bestThRot + 12) continue;
                    int hits = 0;
                    foreach (var th in thSet)
                    {
                        Span<byte> v = stackalloc byte[12];
                        if (!Mem.TryReadBuffer<byte>(th + (ulong)off, v, true)) continue;
                        float x = BitConverter.ToSingle(v[..4]);
                        float y = BitConverter.ToSingle(v[4..8]);
                        float z = BitConverter.ToSingle(v[8..12]);
                        if (float.IsNaN(x) || float.IsNaN(y) || float.IsNaN(z)) continue;
                        if (float.IsInfinity(x) || float.IsInfinity(y) || float.IsInfinity(z)) continue;
                        if (Math.Abs(x) > 1e5f || Math.Abs(y) > 1e5f || Math.Abs(z) > 1e5f) continue;
                        if (x == 0f && y == 0f && z == 0f) continue;
                        hits++;
                    }
                    if (hits >= Math.Max(3, thSet.Count / 2) && hits > bestPosHits)
                    {
                        bestPosHits = hits; bestThPos = off;
                        sb.AppendLine($"//     th+0x{off:X2}  sane-vec3 hits={hits}/{thSet.Count}");
                    }
                }

                // TH.Vertices / TH.Indices: dynamic_array<T> shape.
                //   layout: ptr(+0x00), count(+0x08, u32 or u64), capacity nearby; ptr must
                //   be a valid heap address whose first 16 bytes are dense and reasonable.
                sb.AppendLine();
                sb.AppendLine("//   ── TH.Vertices / TH.Indices probe (dynamic_array<T> shape) ──");
                var arrCands = new List<(int Off, int Hits, int Stride)>();
                for (int off = 0; off + 16 <= 0x100; off += 8)
                {
                    int hits = 0;
                    foreach (var th in thSet)
                    {
                        if (!Mem.TryReadValue<ulong>(th + (ulong)off, out var p, true)) continue;
                        if (!Mem.IsValidVirtualAddress(p)) continue;
                        if (!Mem.TryReadValue<uint>(th + (ulong)(off + 8), out var cnt, true)) continue;
                        if (cnt == 0 || cnt > 100000) continue;
                        // sanity: first dword of array is non-zero
                        if (!Mem.TryReadValue<uint>(p, out var head, true) || head == 0) continue;
                        hits++;
                    }
                    if (hits >= Math.Max(3, thSet.Count / 2))
                        arrCands.Add((off, hits, 0));
                }
                arrCands.Sort((a, b) => b.Hits.CompareTo(a.Hits));
                foreach (var c in arrCands.Take(6))
                    sb.AppendLine($"//     th+0x{c.Off:X2}  dyn-array hits={c.Hits}/{thSet.Count}");
                if (arrCands.Count > 0) bestThVerts = arrCands[0].Off;
                if (arrCands.Count > 1) bestThIdx = arrCands[1].Off;
            }

            // ── Final TA / TH summary ─────────────────────────────────────────
            sb.AppendLine();
            sb.AppendLine("// ── Final resolved TA / TH offsets ─────────────────────────");
            string fmt(int v, ulong was) => v >= 0 ? $"0x{v:X}  (was 0x{was:X})" : $"<unresolved>  (was 0x{was:X})";
            sb.AppendLine($"//   TA.HierarchyOff   = {fmt(bestTaHierOff, _host.TaHierarchyOff)}");
            sb.AppendLine($"//   TA.IndexOff       = {fmt(bestTaIdxOff,  _host.TaIndexOff)}");
            sb.AppendLine($"//   TH.WorldPosition  = {fmt(bestThPos,     _host.ThWorldPosition)}");
            sb.AppendLine($"//   TH.WorldRotation  = {fmt(bestThRot,     _host.ThWorldRotation)}");
            sb.AppendLine($"//   TH.Vertices       = {fmt(bestThVerts,   _host.ThVertices)}");
            sb.AppendLine($"//   TH.Indices        = {fmt(bestThIdx,     _host.ThIndices)}");
        }

        // Read a single pointer at base+off, then read up to 32 ASCII bytes from it.
        private static string? TryReadAsciiAt(ulong baseAddr, uint off)
        {
            if (!Mem.TryReadValue<ulong>(baseAddr + off, out var p, false)) return null;
            if (!Mem.IsValidVirtualAddress(p)) return null;
            Span<byte> buf = stackalloc byte[64];
            if (!Mem.TryReadBuffer<byte>(p, buf, false)) return null;
            int len = 0;
            while (len < buf.Length && buf[len] >= 0x20 && buf[len] < 0x7F) len++;
            if (len < 1 || len >= buf.Length) return null;
            return System.Text.Encoding.ASCII.GetString(buf[..len]);
        }

        // Read the IL2CPP klass name for a Component pointer using the
        // CONFIRMED chain: Component + Comp_ObjectClass -> ObjectClass -> +0x10 -> ASCII.
        private static string? ReadComponentKlassName(ulong comp)
        {
            if (!Mem.IsValidVirtualAddress(comp)) return null;
            if (!Mem.TryReadValue<ulong>(comp + _host.CompObjectClass, out var objectClass, true)) return null;
            if (!Mem.IsValidVirtualAddress(objectClass)) return null;
            return SafeReadKlassName(objectClass);
        }

        // ── Managed (IL2CPP) MonoBehaviour resolution ────────────────────────
        //   This is the path the radar actually uses at runtime to find game
        //   state (e.g. ClientLocalGameWorld → MainPlayer → MovementContext →
        //   State → Position). It complements the engine-side TA/TH chain
        //   above:
        //
        //     • NamedGameObjectRoots: GOM → GameObject named "X" →
        //       ComponentArray → entry → Comp_ObjectClass → klass name.
        //     • BehaviourClassNames : full GOM scan, returning the first
        //       component whose Comp_ObjectClass matches the requested
        //       IL2CPP class name (mirrors FindBehaviourByClassName).
        //
        //   For each hit we print a small hex window of the ObjectClass body
        //   so managed-side field offsets can be eyeballed alongside the
        //   native offsets the rest of the dump tracks.
        private const int MonoBehaviourBodyBytes  = 0x200;
        private const int MonoBehaviourMaxScanned = 16384;

        private static void AppendMonoBehaviourDump(StringBuilder sb)
        {
            string[] roots      = _host.NamedGameObjectRoots ?? Array.Empty<string>();
            string[] behaviours = _host.BehaviourClassNames  ?? Array.Empty<string>();
            if (roots.Length == 0 && behaviours.Length == 0) return;

            sb.AppendLine();
            sb.AppendLine("// ═══ Managed MonoBehaviour resolution (IL2CPP) ═══════════════════");
            sb.AppendLine("// Path: GOM → GameObject → ComponentArray → entry → Comp_ObjectClass");
            sb.AppendLine("// (mirrors Unity.GOM.FindBehaviourByClassName / GetGameObjectByName)");
            sb.AppendLine();

            ulong gomAddr = Mem.GomAddress;
            if (!Mem.IsValidVirtualAddress(gomAddr))
            {
                sb.AppendLine("// GOM address invalid — managed dump skipped.");
                return;
            }

            var (last, active) = ProbeGom(gomAddr);
            if (!Mem.IsValidVirtualAddress(active) || !Mem.IsValidVirtualAddress(last))
            {
                sb.AppendLine("// ProbeGom failed — managed dump skipped.");
                return;
            }
            sb.AppendLine($"// GOM @ 0x{gomAddr:X}  ActiveNodes=0x{active:X}  LastActiveNode=0x{last:X}");

            // ── Use the early-Dump GOM snapshot ──
            // The live GOM list mutates continuously while the game runs. By
            // the time this section executes (after Il2CppDumper, ground
            // truth, live-chain probes, etc.) the list has churned and a
            // stale Next pointer drifts into freed memory, producing a 16k
            // garbage walk. We reuse the snapshot taken at Dump() start so
            // the managed pass operates on the same GOs the early sections
            // saw.
            var nodes = _gomSnapshot ?? new List<ulong>();
            if (nodes.Count == 0)
            {
                sb.AppendLine("// GOM snapshot empty — managed dump skipped.");
                return;
            }
            sb.AppendLine($"// Walked {nodes.Count} GameObject(s) (snapshot taken at Dump() start).");
            sb.AppendLine();

            // ── (1) NamedGameObjectRoots ────────────────────────────────────
            uint goNameOff = EffGoName;
            if (roots.Length > 0)
            {
                sb.AppendLine("// ── Named GameObject roots ─────────────────────────────────");
                foreach (var rootName in roots)
                {
                    if (string.IsNullOrEmpty(rootName)) continue;
                    // Multiple GameObjects can share the same name (e.g. a
                    // pre-init "GameWorld" placeholder plus the real
                    // ClientLocalGameWorld host). Walk every match and prefer
                    // the first one whose ComponentArray actually resolves;
                    // fall back to the very first hit so the dump still
                    // prints something if none have a component table.
                    ulong hitGo = 0;
                    ulong firstHit = 0;
                    int matchCount = 0;
                    foreach (var go in nodes)
                    {
                        if (!Mem.TryReadPtr(go + goNameOff, out var np, true)) continue;
                        if (!Mem.IsValidVirtualAddress(np)) continue;
                        if (!Mem.TryReadString(np, out var s, 64, true)) continue;
                        if (s != rootName) continue;
                        matchCount++;
                        if (firstHit == 0) firstHit = go;
                        if (TryReadGoSnapshot(go, out var snap) &&
                            Mem.IsValidVirtualAddress(snap.CompArrayBase) &&
                            snap.CompArraySize > 0 && snap.CompArraySize <= 0x400)
                        {
                            hitGo = go;
                            break;
                        }
                    }
                    if (hitGo == 0) hitGo = firstHit;

                    if (hitGo == 0)
                    {
                        sb.AppendLine($"// [\"{rootName}\"] NOT FOUND in GOM walk.");
                        continue;
                    }
                    sb.AppendLine($"// [\"{rootName}\"] @ GO 0x{hitGo:X}  (matches in GOM: {matchCount})");
                    DumpNamedGoComponentTable(sb, hitGo);
                    sb.AppendLine();
                }
            }

            // ── (2) BehaviourClassNames (full GOM scan) ─────────────────────
            if (behaviours.Length > 0)
            {
                sb.AppendLine("// ── Behaviour class-name scan (GOM-wide) ───────────────────");
                foreach (var className in behaviours)
                {
                    if (string.IsNullOrEmpty(className)) continue;
                    var (matchGo, matchComp, matchObjClass, gosWithComps, totalCompsScanned) =
                        FindBehaviourByClassNameLocal(nodes, className);
                    if (matchObjClass == 0)
                    {
                        sb.AppendLine($"// [\"{className}\"] NOT FOUND in GOM scan.  (GOs with comps: {gosWithComps}/{nodes.Count}, components scanned: {totalCompsScanned})");
                        continue;
                    }
                    sb.AppendLine($"// [\"{className}\"]  (GOs with comps: {gosWithComps}/{nodes.Count}, components scanned: {totalCompsScanned})");
                    sb.AppendLine($"//   GameObject  : 0x{matchGo:X}");
                    sb.AppendLine($"//   Component   : 0x{matchComp:X}");
                    sb.AppendLine($"//   ObjectClass : 0x{matchObjClass:X}  (managed instance — field offsets relative here)");
                    DumpInstance(sb, $"{className} ObjectClass body", matchObjClass, MonoBehaviourBodyBytes);
                    sb.AppendLine();
                }
            }
        }

        private static void DumpNamedGoComponentTable(StringBuilder sb, ulong go)
        {
            if (!TryReadGoSnapshot(go, out var snap) ||
                !Mem.IsValidVirtualAddress(snap.CompArrayBase) ||
                snap.CompArraySize == 0 || snap.CompArraySize > 0x400)
            {
                sb.AppendLine("//   [ComponentArray unreadable]");
                return;
            }

            int stride          = EffCompStride;
            int instOff         = EffCompInstOff;
            uint compObjClassOff = EffCompObjectClass;
            int count = (int)Math.Min(snap.CompArraySize, 32UL);

            sb.AppendLine($"//   ComponentArray @ 0x{snap.CompArrayBase:X}  size={count}  stride=0x{stride:X}  inst=+0x{instOff:X}");
            sb.AppendLine($"//     idx  component            objectClass          klass-name");

            ulong entry1Comp = 0;
            for (int i = 0; i < count; i++)
            {
                ulong entryAddr = snap.CompArrayBase + (ulong)(i * stride);
                if (!Mem.TryReadValue<ulong>(entryAddr + (ulong)instOff, out var c, false)) continue;
                if (i == 1) entry1Comp = c;
                if (!Mem.IsValidVirtualAddress(c))
                {
                    sb.AppendLine($"//     [{i,2}]  0x{c:X16}   <invalid>");
                    continue;
                }
                ulong objClass = 0;
                if (compObjClassOff != 0)
                    Mem.TryReadPtr(c + compObjClassOff, out objClass, true);
                string nm = ReadComponentKlassNameAt(c, compObjClassOff) ?? "<?>";
                sb.AppendLine($"//     [{i,2}]  0x{c:X16}   0x{objClass:X16}   {nm}");
            }

            if (count >= 2 &&
                Mem.IsValidVirtualAddress(entry1Comp) &&
                compObjClassOff != 0 &&
                Mem.TryReadPtr(entry1Comp + compObjClassOff, out var entry1ObjClass, false) &&
                Mem.IsValidVirtualAddress(entry1ObjClass))
            {
                string entry1Klass = SafeReadKlassName(entry1ObjClass) ?? "<no-name>";
                sb.AppendLine();
                sb.AppendLine($"//   ── FindGameWorldViaGOM-style walk ──");
                sb.AppendLine($"//     entry[1].Component        : 0x{entry1Comp:X16}");
                sb.AppendLine($"//     +Comp_ObjectClass(0x{compObjClassOff:X}) : 0x{entry1ObjClass:X16}  klass=\"{entry1Klass}\"");
                DumpInstance(sb, $"\"{entry1Klass}\" ObjectClass body (entry[1].Component)", entry1ObjClass, MonoBehaviourBodyBytes);
            }
        }

        private static string? ReadComponentKlassNameAt(ulong comp, uint compObjClassOff)
        {
            if (compObjClassOff == 0) return null;
            if (!Mem.IsValidVirtualAddress(comp)) return null;
            if (!Mem.TryReadValue<ulong>(comp + compObjClassOff, out var oc, true)) return null;
            if (!Mem.IsValidVirtualAddress(oc)) return null;
            return SafeReadKlassName(oc);
        }

        private static (ulong Go, ulong Comp, ulong ObjectClass, int GosWithComps, int CompsScanned) FindBehaviourByClassNameLocal(
            List<ulong> nodes, string className)
        {
            uint compObjClassOff = EffCompObjectClass;
            if (compObjClassOff == 0) return (0, 0, 0, 0, 0);
            int stride  = EffCompStride;
            int instOff = EffCompInstOff;
            int scanned = 0;
            int gosWithComps = 0;
            int compsScanned = 0;
            foreach (var go in nodes)
            {
                if (++scanned > MonoBehaviourMaxScanned) break;
                if (!TryReadGoSnapshot(go, out var snap)) continue;
                if (!Mem.IsValidVirtualAddress(snap.CompArrayBase)) continue;
                if (snap.CompArraySize == 0 || snap.CompArraySize > 0x400) continue;
                gosWithComps++;

                int count = (int)Math.Min(snap.CompArraySize, 64UL);
                for (int i = 0; i < count; i++)
                {
                    ulong entryAddr = snap.CompArrayBase + (ulong)(i * stride);
                    if (!Mem.TryReadValue<ulong>(entryAddr + (ulong)instOff, out var c, true)) continue;
                    if (!Mem.IsValidVirtualAddress(c)) continue;
                    if (!Mem.TryReadPtr(c + compObjClassOff, out var objClass, true)) continue;
                    if (!Mem.IsValidVirtualAddress(objClass)) continue;
                    compsScanned++;
                    var name = SafeReadKlassName(objClass);
                    if (name is not null && name.Equals(className, StringComparison.Ordinal))
                        return (go, c, objClass, gosWithComps, compsScanned);
                }
            }
            return (0, 0, 0, gosWithComps, compsScanned);
        }

        // ── Native vtable histogram ─────────────────────────────────────────
        //   Walks every component of every reachable GameObject and bins the
        //   comp+0x00 vtable RVA (within UnityPlayer.dll). The Transform
        //   vtable repeats once per GO so it dominates the histogram. For
        //   each candidate, we test the CONFIRMED TA/TH chain: comp +
        //   TA.HierarchyOff -> TH; TH + TH.WorldPosition must be a sane Vec3.
        //   The vtable that satisfies this on ≥ TransformProbeMinHits
        //   distinct comps is the Transform vtable on this UnityPlayer build.
        private const int TransformProbeMinHits = 8;
        private const int TopVtablesToReport    = 12;

        private static void AppendNativeVtableHistogram(StringBuilder sb, List<ulong> gos)
        {
            ulong unityBase = Mem.UnityBase;
            if (!Mem.IsValidVirtualAddress(unityBase))
            {
                sb.AppendLine();
                sb.AppendLine("// ── Native vtable histogram skipped (UnityPlayer base unknown) ──");
                return;
            }

            // (vtableRVA -> (count, exampleComp)) and per-vtable comp samples.
            var counts = new Dictionary<ulong, int>(2048);
            var samples = new Dictionary<ulong, List<ulong>>(2048);
            int compsScanned = 0;

            foreach (var go in gos)
            {
                if (!TryReadGoSnapshot(go, out var goStruct)) continue;
                if (!Mem.IsValidVirtualAddress(goStruct.CompArrayBase)) continue;
                int count = (int)Math.Min(goStruct.CompArraySize, 32UL);
                if (count <= 0) continue;
                Span<ComponentArrayEntry> entries = count <= 64
                    ? stackalloc ComponentArrayEntry[count]
                    : new ComponentArrayEntry[count];
                if (!Mem.TryReadBuffer(goStruct.CompArrayBase, entries, false)) continue;

                for (int i = 0; i < count; i++)
                {
                    ulong comp = entries[i].Component;
                    if (!Mem.IsValidVirtualAddress(comp)) continue;
                    if (!Mem.TryReadValue<ulong>(comp, out var vtable, false)) continue;
                    if (vtable < unityBase || vtable - unityBase > 0x1000_0000) continue;
                    ulong rva = vtable - unityBase;
                    counts[rva] = counts.GetValueOrDefault(rva) + 1;
                    if (!samples.TryGetValue(rva, out var lst))
                    {
                        lst = new List<ulong>(8);
                        samples[rva] = lst;
                    }
                    if (lst.Count < 8) lst.Add(comp);
                    compsScanned++;
                }
            }

            sb.AppendLine();
            sb.AppendLine("// ── Native vtable histogram (UnityPlayer-relative) ────────────");
            sb.AppendLine($"//   GameObjects scanned : {gos.Count}");
            sb.AppendLine($"//   Components scanned  : {compsScanned}");
            sb.AppendLine($"//   Distinct vtables    : {counts.Count}");
            sb.AppendLine($"//   Top {TopVtablesToReport} by frequency:");

            var top = counts.OrderByDescending(kv => kv.Value).Take(TopVtablesToReport).ToList();

            uint thOff = _host.TaHierarchyOff;
            uint wpOff = _host.ThWorldPosition;
            ulong transformRva = 0;
            int transformValidations = 0;
            var detected = new HashSet<ulong>();

            foreach (var (rva, cnt) in top)
            {
                int validHits = 0;
                if (samples.TryGetValue(rva, out var compList))
                {
                    foreach (var comp in compList)
                    {
                        if (!Mem.TryReadValue<ulong>(comp + thOff, out var thPtr, false)) continue;
                        if (!Mem.IsValidVirtualAddress(thPtr)) continue;
                        if (!Mem.TryReadValue<Vector3>(thPtr + wpOff, out var wp)) continue;
                        if (float.IsNaN(wp.X) || float.IsInfinity(wp.X)) continue;
                        if (Math.Abs(wp.X) > 1e6f || Math.Abs(wp.Y) > 1e6f || Math.Abs(wp.Z) > 1e6f) continue;
                        validHits++;
                    }
                }

                bool isTransform = validHits >= TransformProbeMinHits ||
                                   (validHits >= 2 && validHits >= compList!.Count - 1 && cnt >= TransformProbeMinHits);
                string mark = isTransform ? "  ✓ Transform vtable (TA/TH chain valid)" : "";
                sb.AppendLine($"//     UnityPlayer+0x{rva:X7}  count={cnt,5}  TA/TH-valid={validHits}/{compList?.Count ?? 0}{mark}");

                if (isTransform)
                {
                    detected.Add(rva);
                    if (transformRva == 0)
                    {
                        transformRva = rva;
                        transformValidations = validHits;
                    }
                }
            }

            if (transformRva != 0)
            {
                sb.AppendLine();
                sb.AppendLine($"// ✓ Transform vtable(s) detected: {detected.Count}");
                foreach (var rva in detected.OrderBy(x => x))
                    sb.AppendLine($"//     UnityPlayer+0x{rva:X}");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("// ✗ No vtable produced ≥ TransformProbeMinHits sane TA/TH chains.");
                sb.AppendLine("//   Possibly TA.HierarchyOff / TH.WorldPosition need re-validation,");
                sb.AppendLine("//   or the GOM walk reached only UI GameObjects whose Transforms are");
                sb.AppendLine("//   RectTransforms (different vtable, identical chain layout).");
            }
        }

        // Dump the m_Components vector of a GO so GO_Components / Comp_ObjectClass
        // / Comp_GameObject can be visually confirmed from real data.
        private static void DumpComponentsVector(StringBuilder sb, ulong go)
            => DumpComponentsVectorAt(sb, go, _host.GoComponents, 0x10, 0x08);

        private static void DumpComponentsVectorAt(StringBuilder sb, ulong go, uint compOff, int stride, int instOff)
        {
            sb.AppendLine();
            sb.AppendLine($"//   m_Components @ GO+0x{compOff:X}  stride=0x{stride:X}  inst=+0x{instOff:X}");
            if (!Mem.TryReadPtr(go + compOff, out var begin, false))
            {
                sb.AppendLine("//     [unreadable begin ptr]");
                return;
            }
            // ComponentArray layout (matches Unity.ComponentArray + GoSnapshot):
            //   +0x00 ArrayBase   +0x08 MemLabelId   +0x10 Size   +0x18 Capacity
            Mem.TryReadValue<ulong>(go + compOff + 0x08, out var memLabel, false);
            Mem.TryReadValue<ulong>(go + compOff + 0x10, out var size,     false);
            Mem.TryReadValue<ulong>(go + compOff + 0x18, out var cap,      false);

            sb.AppendLine($"//     begin = 0x{begin:X}");
            sb.AppendLine($"//     label = 0x{memLabel:X}  (GO+0x{compOff + 0x08:X})");
            sb.AppendLine($"//     size  = 0x{size:X}  (read from GO+0x{compOff + 0x10:X})");
            sb.AppendLine($"//     cap   = 0x{cap:X}  (read from GO+0x{compOff + 0x18:X})");

            if (!Mem.IsValidVirtualAddress(begin))
            {
                sb.AppendLine("//     [begin not a valid VA — skipping component rows]");
                return;
            }

            // Walk the component array using the discovered stride/instOff and
            // resolve each entry's klass name (Component → Comp_ObjectClass → Klass+0x10 → ASCII).
            int rows = (int)Math.Min(MaxComponentRows, size > 0 && size < 256 ? size : 16);
            sb.AppendLine();
            sb.AppendLine($"//   ── ComponentArray dump ({rows} rows, stride=0x{stride:X}, inst=+0x{instOff:X}) ──");
            sb.AppendLine($"//     row  entryAddr           comp                 klassName");
            for (int i = 0; i < rows; i++)
            {
                ulong entry = begin + (ulong)(i * stride);
                Mem.TryReadValue<ulong>(entry + (ulong)instOff, out var compPtr);
                string kname = "<?>";
                if (Mem.IsValidVirtualAddress(compPtr) &&
                    Mem.TryReadValue<ulong>(compPtr + _host.CompObjectClass, out var oc) &&
                    Mem.IsValidVirtualAddress(oc) &&
                    Mem.TryReadValue<ulong>(oc, out var klass) &&
                    Mem.IsValidVirtualAddress(klass) &&
                    Mem.TryReadValue<ulong>(klass + 0x10, out var knamePtr) &&
                    Mem.IsValidVirtualAddress(knamePtr))
                {
                    Span<byte> kb = stackalloc byte[64];
                    if (Mem.TryReadBuffer<byte>(knamePtr, kb, false))
                    {
                        int klen = 0;
                        while (klen < kb.Length && kb[klen] >= 0x20 && kb[klen] < 0x7F) klen++;
                        if (klen >= 1 && klen < kb.Length)
                            kname = System.Text.Encoding.ASCII.GetString(kb[..klen]);
                    }
                }
                sb.AppendLine($"//     [{i,2}]  0x{entry:X16}  0x{compPtr:X16}  {kname}");
            }

            // Dump the first component instance so Comp_ObjectClass / Comp_GameObject
            // can be visually confirmed.
            if (Mem.TryReadPtr(begin + (ulong)instOff, out var firstInst, false) &&
                Mem.IsValidVirtualAddress(firstInst) && firstInst != go)
                DumpInstance(sb, $"First Component (stride 0x{stride:X}, +0x{instOff:X})", firstInst, 0x80);
        }

        // Scan the first `range` bytes of `inst` for a qword equal to `target`.
        // Returns the offset, or -1 if not found.
        private static int FindBackrefOffset(ulong inst, ulong target, int range)
        {
            try
            {
                Span<byte> buf = stackalloc byte[256];
                int n = Math.Min(range, buf.Length) & ~7;
                if (!Mem.TryReadBuffer<byte>(inst, buf[..n], false))
                    return -1;
                for (int o = 0; o + 8 <= n; o += 8)
                    if (BitConverter.ToUInt64(buf.Slice(o, 8)) == target)
                        return o;
                return -1;
            }
            catch { return -1; }
        }

        // GameObject validation: GO_Name is a pointer to a C-string buffer.
        private static bool TryProbeGameObjectName(ulong go, out string name)
        {
            name = string.Empty;
            try
            {
                if (!Mem.TryReadPtr(go + _host.GoName, out var namePtr, false) ||
                    !Mem.IsValidVirtualAddress(namePtr))
                    return false;
                Span<byte> buf = stackalloc byte[64];
                if (!Mem.TryReadBuffer<byte>(namePtr, buf, false))
                    return false;
                int len = 0;
                while (len < buf.Length && buf[len] >= 0x20 && buf[len] < 0x7F) len++;
                if (len < 1 || len >= buf.Length) return false;
                name = System.Text.Encoding.ASCII.GetString(buf[..len]);
                return true;
            }
            catch { return false; }
        }

        private static void DumpInstance(StringBuilder sb, string label, ulong addr, int size = ChainDumpSize)
        {
            if (size <= 0) size = ChainDumpSize;
            var buffer = new byte[size];
            if (!Mem.TryReadBuffer<byte>(addr, buffer, false))
            {
                sb.AppendLine();
                sb.AppendLine($"// ── {label} @ 0x{addr:X}: read failed ─────────────────");
                return;
            }

            sb.AppendLine();
            sb.AppendLine($"// ── {label} @ 0x{addr:X}  ({size:X} bytes) ───────────────");
            sb.AppendLine("//   off    qword              hint");

            ulong unityBase = Mem.UnityBase;
            ulong gaBase    = Mem.GameAssemblyBase;
            for (int off = 0; off + 8 <= size; off += 8)
            {
                ulong q = BitConverter.ToUInt64(buffer, off);
                string hint = ClassifyChainQword(q, off, buffer, unityBase, gaBase);
                sb.AppendLine($"//   +0x{off:X3}  0x{q:X16}  {hint}");
            }
        }

        // Generic classifier (no Camera-specific offsets baked in).
        private static string ClassifyChainQword(ulong q, int off, byte[] buf, ulong unityBase, ulong gaBase)
        {
            bool couldBePtr = (q >> 32) != 0 && q != 0 && Mem.IsValidVirtualAddress(q);
            if (couldBePtr)
            {
                if (unityBase != 0 && q >= unityBase && q < unityBase + 0x4000_0000)
                    return $"ptr -> UnityPlayer+0x{q - unityBase:X}";
                if (gaBase != 0 && q >= gaBase && q < gaBase + 0x4000_0000)
                    return $"ptr -> GameAssembly+0x{q - gaBase:X}";
                return "ptr (heap)";
            }
            if (q == 0) return "zero";

            float lo = BitConverter.ToSingle(buf, off);
            float hi = BitConverter.ToSingle(buf, off + 4);
            string fLo = ClassifyFloat(lo);
            string fHi = ClassifyFloat(hi);
            if (fLo.Length > 0 || fHi.Length > 0)
                return $"f32  [{(fLo.Length > 0 ? fLo : "0")}, {(fHi.Length > 0 ? fHi : "0")}]";

            int iLo = BitConverter.ToInt32(buf, off);
            int iHi = BitConverter.ToInt32(buf, off + 4);
            if (iHi == 0 && iLo != 0) return $"i32  lo={iLo}";
            if (iHi != 0)             return $"i32  lo={iLo}  hi={iHi}";
            return "";
        }

        private static string ClassifyFloat(float f)
        {
            if (float.IsNaN(f) || float.IsInfinity(f)) return string.Empty;
            float abs = Math.Abs(f);
            if (abs == 0f) return string.Empty;
            if (abs < 1e-6f || abs > 1e9f) return string.Empty;
            return f.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
