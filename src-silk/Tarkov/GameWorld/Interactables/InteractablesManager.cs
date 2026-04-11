using eft_dma_radar.Silk.Misc.Data;
using eft_dma_radar.Silk.Tarkov.Unity;
using eft_dma_radar.Silk.Tarkov.Unity.Collections;
using VmmSharpEx.Options;

namespace eft_dma_radar.Silk.Tarkov.GameWorld.Interactables
{
    /// <summary>
    /// Discovers and tracks interactive doors in the GameWorld.
    /// Doors are discovered once at startup, then their state is refreshed periodically.
    /// Uses batched scatter reads for efficient DMA access.
    /// </summary>
    internal sealed class InteractablesManager
    {
        private readonly ulong _lgw;
        private volatile IReadOnlyList<Door> _doors = [];
        private bool _initialized;
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(750);
        private DateTime _lastRefresh;

        /// <summary>Current door snapshot (thread-safe read).</summary>
        public IReadOnlyList<Door> Doors => _doors;

        public InteractablesManager(ulong localGameWorld)
        {
            _lgw = localGameWorld;
        }

        /// <summary>
        /// Refreshes door states from memory. Discovery runs once; state updates are rate-limited.
        /// Call from the registration worker thread.
        /// </summary>
        public void Refresh()
        {
            var now = DateTime.UtcNow;
            if (now - _lastRefresh < RefreshInterval)
                return;
            _lastRefresh = now;

            if (!_initialized)
            {
                try
                {
                    DiscoverDoors();
                    _initialized = true;
                }
                catch (Exception ex)
                {
                    Log.WriteLine($"[Interactables] Discovery failed: {ex.Message}");
                }
                return;
            }

            RefreshDoorStates();
        }

        /// <summary>
        /// Walks the World → interactables array, identifies doors by IL2CPP class name,
        /// and reads their key ID, door ID, position, and initial state.
        /// </summary>
        private void DiscoverDoors()
        {
            if (!Memory.TryReadPtr(_lgw + Offsets.ClientLocalGameWorld.World, out var world)
                || !world.IsValidVirtualAddress())
            {
                Log.WriteLine("[Interactables] World pointer invalid.");
                return;
            }

            if (!Memory.TryReadPtr(world + 0x30, out var interactableArrayPtr)
                || !interactableArrayPtr.IsValidVirtualAddress())
            {
                Log.WriteLine("[Interactables] Interactables array pointer invalid.");
                return;
            }

            using var ptrs = MemArray<ulong>.Get(interactableArrayPtr, true);
            if (ptrs.Count == 0)
            {
                Log.WriteLine("[Interactables] Interactables array is empty.");
                return;
            }

            // Filter to doors only by reading class names
            var doorPtrs = new List<ulong>();
            for (int i = 0; i < ptrs.Count; i++)
            {
                var ptr = ptrs[i];
                if (!ptr.IsValidVirtualAddress())
                    continue;

                try
                {
                    var className = Il2CppClass.ReadName(ptr);
                    if (className is not null && className.Equals("Door", StringComparison.Ordinal))
                        doorPtrs.Add(ptr);
                }
                catch { }
            }

            if (doorPtrs.Count == 0)
            {
                Log.WriteLine("[Interactables] No doors found.");
                _doors = [];
                return;
            }

            // Read all door data in batched operations
            var doors = ReadDoorsBatched(doorPtrs);
            _doors = doors;
            Log.WriteLine($"[Interactables] Discovered {doors.Count} keyed doors (from {doorPtrs.Count} total doors).");
        }

        /// <summary>
        /// Reads door data (key ID, door ID, position, state) for all discovered doors
        /// using batched VmmScatter reads.
        /// </summary>
        private static List<Door> ReadDoorsBatched(List<ulong> doorPtrs)
        {
            int count = doorPtrs.Count;
            var keyIdPtrs = new ulong[count];
            var doorIdPtrs = new ulong[count];
            var doorStates = new byte[count];
            var transformInternals = new ulong[count];

            // ── Batch 1: Read key ID ptr, door ID ptr, door state, and start transform chain ──
            using (var s1 = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < count; i++)
                {
                    var ptr = doorPtrs[i];
                    s1.PrepareReadValue<ulong>(ptr + Offsets.Interactable.KeyId);
                    s1.PrepareReadValue<ulong>(ptr + Offsets.Interactable.Id);
                    s1.PrepareReadValue<byte>(ptr + Offsets.Interactable._doorState);
                    // Transform chain step 1: ObjectClass → MonoBehaviour
                    s1.PrepareReadValue<ulong>(ptr + 0x10);
                }
                s1.Execute();

                for (int i = 0; i < count; i++)
                {
                    var ptr = doorPtrs[i];
                    s1.ReadValue<ulong>(ptr + Offsets.Interactable.KeyId, out keyIdPtrs[i]);
                    s1.ReadValue<ulong>(ptr + Offsets.Interactable.Id, out doorIdPtrs[i]);
                    s1.ReadValue<byte>(ptr + Offsets.Interactable._doorState, out doorStates[i]);
                    s1.ReadValue<ulong>(ptr + 0x10, out transformInternals[i]);
                }
            }

            // Read key ID and door ID strings, and continue transform chain
            var keyIds = new string?[count];
            var doorIds = new string?[count];
            var keyNames = new string?[count];
            var gameObjects = new ulong[count];

            // ── Read strings + transform chain step 2 ──
            using (var s2 = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < count; i++)
                {
                    if (keyIdPtrs[i].IsValidVirtualAddress())
                        s2.PrepareRead(keyIdPtrs[i] + 0x14, 128); // Unity string chars
                    if (doorIdPtrs[i].IsValidVirtualAddress())
                        s2.PrepareRead(doorIdPtrs[i] + 0x14, 128);
                    // MonoBehaviour → GameObject
                    if (transformInternals[i].IsValidVirtualAddress())
                        s2.PrepareReadValue<ulong>(transformInternals[i] + UnityOffsets.Comp_GameObject);
                }
                s2.Execute();

                for (int i = 0; i < count; i++)
                {
                    if (keyIdPtrs[i].IsValidVirtualAddress())
                    {
                        var raw = s2.ReadString(keyIdPtrs[i] + 0x14, 128, Encoding.Unicode);
                        if (!string.IsNullOrEmpty(raw))
                        {
                            int nt = raw.IndexOf('\0');
                            keyIds[i] = nt >= 0 ? raw[..nt] : raw;

                            if (keyIds[i] is not null
                                && EftDataManager.AllItems.TryGetValue(keyIds[i]!, out var keyItem))
                            {
                                keyNames[i] = keyItem.ShortName;
                            }
                        }
                    }

                    if (doorIdPtrs[i].IsValidVirtualAddress())
                    {
                        var raw = s2.ReadString(doorIdPtrs[i] + 0x14, 128, Encoding.Unicode);
                        if (!string.IsNullOrEmpty(raw))
                        {
                            int nt = raw.IndexOf('\0');
                            doorIds[i] = nt >= 0 ? raw[..nt] : raw;
                        }
                    }

                    if (transformInternals[i].IsValidVirtualAddress())
                        s2.ReadValue<ulong>(transformInternals[i] + UnityOffsets.Comp_GameObject, out gameObjects[i]);
                }
            }

            // ── Transform chain steps 3-4: GameObject → Components → Transform ──
            var components = new ulong[count];
            using (var s3 = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < count; i++)
                {
                    if (gameObjects[i].IsValidVirtualAddress())
                        s3.PrepareReadValue<ulong>(gameObjects[i] + UnityOffsets.GO_Components);
                }
                s3.Execute();

                for (int i = 0; i < count; i++)
                {
                    if (gameObjects[i].IsValidVirtualAddress())
                        s3.ReadValue<ulong>(gameObjects[i] + UnityOffsets.GO_Components, out components[i]);
                }
            }

            // Components[0] (Transform) → ObjectClass → TransformInternal
            var transforms1 = new ulong[count];
            using (var s4 = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < count; i++)
                {
                    if (components[i].IsValidVirtualAddress())
                        s4.PrepareReadValue<ulong>(components[i] + 0x08); // First component = Transform
                }
                s4.Execute();

                for (int i = 0; i < count; i++)
                {
                    if (components[i].IsValidVirtualAddress())
                        s4.ReadValue<ulong>(components[i] + 0x08, out transforms1[i]);
                }
            }

            var transforms2 = new ulong[count];
            using (var s5 = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < count; i++)
                {
                    if (transforms1[i].IsValidVirtualAddress())
                        s5.PrepareReadValue<ulong>(transforms1[i] + UnityOffsets.Comp_ObjectClass);
                }
                s5.Execute();

                for (int i = 0; i < count; i++)
                {
                    if (transforms1[i].IsValidVirtualAddress())
                        s5.ReadValue<ulong>(transforms1[i] + UnityOffsets.Comp_ObjectClass, out transforms2[i]);
                }
            }

            // ObjectClass → TransformInternal
            var tis = new ulong[count];
            using (var s6 = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < count; i++)
                {
                    if (transforms2[i].IsValidVirtualAddress())
                        s6.PrepareReadValue<ulong>(transforms2[i] + 0x10);
                }
                s6.Execute();

                for (int i = 0; i < count; i++)
                {
                    if (transforms2[i].IsValidVirtualAddress())
                        s6.ReadValue<ulong>(transforms2[i] + 0x10, out tis[i]);
                }
            }

            // ── Read transform hierarchy data and compute positions ──
            var hierarchies = new ulong[count];
            var indices = new int[count];

            using (var s7 = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < count; i++)
                {
                    if (tis[i].IsValidVirtualAddress())
                    {
                        s7.PrepareReadValue<ulong>(tis[i] + UnityOffsets.TransformAccess.HierarchyOffset);
                        s7.PrepareReadValue<int>(tis[i] + UnityOffsets.TransformAccess.IndexOffset);
                    }
                }
                s7.Execute();

                for (int i = 0; i < count; i++)
                {
                    if (tis[i].IsValidVirtualAddress())
                    {
                        s7.ReadValue<ulong>(tis[i] + UnityOffsets.TransformAccess.HierarchyOffset, out hierarchies[i]);
                        s7.ReadValue<int>(tis[i] + UnityOffsets.TransformAccess.IndexOffset, out indices[i]);
                    }
                }
            }

            var verticesPtrs = new ulong[count];
            var indicesPtrs = new ulong[count];

            using (var s8 = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < count; i++)
                {
                    if (hierarchies[i].IsValidVirtualAddress() && indices[i] >= 0 && indices[i] <= 150_000)
                    {
                        s8.PrepareReadValue<ulong>(hierarchies[i] + UnityOffsets.TransformHierarchy.VerticesOffset);
                        s8.PrepareReadValue<ulong>(hierarchies[i] + UnityOffsets.TransformHierarchy.IndicesOffset);
                    }
                }
                s8.Execute();

                for (int i = 0; i < count; i++)
                {
                    if (hierarchies[i].IsValidVirtualAddress() && indices[i] >= 0 && indices[i] <= 150_000)
                    {
                        s8.ReadValue<ulong>(hierarchies[i] + UnityOffsets.TransformHierarchy.VerticesOffset, out verticesPtrs[i]);
                        s8.ReadValue<ulong>(hierarchies[i] + UnityOffsets.TransformHierarchy.IndicesOffset, out indicesPtrs[i]);
                    }
                }
            }

            var positions = new Vector3[count];

            using (var s9 = Memory.GetScatter(VmmFlags.NOCACHE))
            {
                for (int i = 0; i < count; i++)
                {
                    if (verticesPtrs[i].IsValidVirtualAddress() && indicesPtrs[i].IsValidVirtualAddress())
                    {
                        int vertCount = indices[i] + 1;
                        s9.PrepareReadArray<TrsX>(verticesPtrs[i], vertCount);
                        s9.PrepareReadArray<int>(indicesPtrs[i], vertCount);
                    }
                }
                s9.Execute();

                for (int i = 0; i < count; i++)
                {
                    if (!verticesPtrs[i].IsValidVirtualAddress() || !indicesPtrs[i].IsValidVirtualAddress())
                        continue;

                    try
                    {
                        int vertCount = indices[i] + 1;
                        var vertices = s9.ReadArray<TrsX>(verticesPtrs[i], vertCount);
                        var parentIndices = s9.ReadArray<int>(indicesPtrs[i], vertCount);

                        if (vertices is null || parentIndices is null ||
                            vertices.Length < vertCount || parentIndices.Length < vertCount)
                            continue;

                        positions[i] = ComputeTransformPosition(vertices, parentIndices, indices[i]);
                    }
                    catch { }
                }
            }

            // Build Door objects (only include keyed doors with valid positions)
            var result = new List<Door>();
            for (int i = 0; i < count; i++)
            {
                var doorId = doorIds[i] ?? string.Empty;
                var state = (EDoorState)doorStates[i];

                if (positions[i] == Vector3.Zero)
                    continue;

                result.Add(new Door(doorPtrs[i], doorId, keyIds[i], keyNames[i], positions[i], state));
            }

            return result;
        }

        /// <summary>
        /// Refreshes door states for all discovered doors using a single batched scatter read.
        /// </summary>
        private void RefreshDoorStates()
        {
            var doors = _doors;
            if (doors.Count == 0)
                return;

            using var scatter = Memory.GetScatter(VmmFlags.NOCACHE);
            for (int i = 0; i < doors.Count; i++)
                scatter.PrepareReadValue<byte>(doors[i].Base + Offsets.Interactable._doorState);

            scatter.Execute();

            for (int i = 0; i < doors.Count; i++)
            {
                if (scatter.ReadValue<byte>(doors[i].Base + Offsets.Interactable._doorState, out var state))
                    doors[i].DoorState = (EDoorState)state;
            }
        }

        /// <summary>
        /// Pure math — computes world position from pre-read vertices + indices.
        /// </summary>
        private static Vector3 ComputeTransformPosition(TrsX[] vertices, int[] parentIndices, int index)
        {
            var pos = vertices[index].T;
            int parent = parentIndices[index];
            int iter = 0;

            while (parent >= 0 && parent < vertices.Length && iter++ < 4096)
            {
                ref readonly var p = ref vertices[parent];
                pos = Vector3.Transform(pos, p.Q);
                pos *= p.S;
                pos += p.T;
                parent = parentIndices[parent];
            }

            if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z))
                return Vector3.Zero;

            if (pos.LengthSquared() < 16f)
                return Vector3.Zero;

            return pos;
        }
    }
}
