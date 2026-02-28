using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Misc.Pools;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace eft_dma_radar.Common.Unity
{
    public sealed class UnityTransform
    {
        private const int MAX_ITER = 4096;
        private const float MIN_VALID_DIST = 4f;

        private readonly bool _useCache;
        private readonly ReadOnlyMemory<int> _indices;

        private Vector3 _position;
        public bool HasValidPosition { get; private set; }

        public ref Vector3 Position => ref _position;

        public ulong TransformInternal { get; }
        public ulong HierarchyAddr { get; }
        public ulong IndicesAddr { get; }
        public ulong VerticesAddr { get; }
        public int Index { get; }
        public int Count => Index + 1;

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // CONSTRUCTOR
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        public UnityTransform(ulong transformInternal, bool useCache = false)
            : this(transformInternal, null, useCache) { }

        public UnityTransform(ulong transformInternal, Vector3? fallbackPosition, bool useCache = false)
        {
            TransformInternal = transformInternal;
            _useCache = useCache;

            // Read TransformAccess
            var ta = Memory.ReadValue<TransformAccess>(transformInternal, useCache);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(ta.Index, 150000, nameof(ta.Index));
            ta.Hierarchy.ThrowIfInvalidVirtualAddress();

            Index = ta.Index;
            HierarchyAddr = ta.Hierarchy;

            // Read TransformHierarchy
            var hier = Memory.ReadValue<TransformHierarchy>(HierarchyAddr, useCache);
            hier.Vertices.ThrowIfInvalidVirtualAddress();
            hier.Indices.ThrowIfInvalidVirtualAddress();

            VerticesAddr = hier.Vertices;
            IndicesAddr = hier.Indices;

            // Cache indices for life of transform (never change)
            _indices = ReadIndices();

            // Fallback
            if (fallbackPosition.HasValue)
            {
                _position = fallbackPosition.Value;
                HasValidPosition = true;
            }
        }

        private ReadOnlySpan<int> Indices => _indices.Span;

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // FAST POSITION UPDATE
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref Vector3 UpdatePosition(SharedArray<TrsX> verts = null)
        {
            SharedArray<TrsX> localVerts = null;

            try
            {
                verts ??= (localVerts = ReadVertices());

                if ((uint)Index >= (uint)verts.Count)
                    throw new InvalidOperationException($"Transform index {Index} is out of bounds.");

                // Start transform
                Vector3 pos = verts[Index].t;

                int parent = Indices[Index];
                int iter = 0;

                // Walk hierarchy
                while (parent >= 0)
                {
                    if (parent >= verts.Count)
                        throw new InvalidOperationException("Parent index outside transform vertex array.");

                    ArgumentOutOfRangeException.ThrowIfGreaterThan(iter++, MAX_ITER);

                    ref readonly var p = ref verts[parent];

                    pos = p.q.Multiply(pos);
                    pos *= p.s;
                    pos += p.t;

                    parent = Indices[parent];
                }

                ValidatePosition(ref pos);

                _position = pos;
                HasValidPosition = true;
                return ref _position;
            }
            finally
            {
                localVerts?.Dispose();
            }
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // ROTATION
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        public Quaternion GetRotation(SharedArray<TrsX> verts = null)
        {
            SharedArray<TrsX> local = null;
            try
            {
                verts ??= (local = ReadVertices());

                Quaternion q = verts[Index].q;
                int parent = Indices[Index];
                int iter = 0;

                while (parent >= 0)
                {
                    if (parent >= verts.Count)
                        throw new InvalidOperationException("Parent index out of range.");

                    ArgumentOutOfRangeException.ThrowIfGreaterThan(iter++, MAX_ITER);

                    q = verts[parent].q * q;
                    parent = Indices[parent];
                }

                q.ThrowIfAbnormal();
                return q;
            }
            finally
            {
                local?.Dispose();
            }
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // ROOT METHODS
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        public Vector3 GetRootPosition()
        {
            var p = Memory.ReadValue<TrsX>(HierarchyAddr + TransformHierarchy.RootPosOffset, _useCache).t;
            p.ThrowIfAbnormal();
            return p;
        }

        public void UpdateRootPosition(ref Vector3 newPos)
        {
            newPos.ThrowIfAbnormal();
            Memory.WriteValue(HierarchyAddr + TransformHierarchy.RootPosOffset, ref newPos);
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // LOCAL READS
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        public Vector3 GetLocalPosition() =>
            Memory.ReadValue<TrsX>(VerticesAddr + (uint)Index * (uint)Unsafe.SizeOf<TrsX>(), _useCache).t;

        public Quaternion GetLocalRotation() =>
            Memory.ReadValue<TrsX>(VerticesAddr + (uint)Index * (uint)Unsafe.SizeOf<TrsX>(), _useCache).q;

        public Vector3 GetLocalScale() =>
            Memory.ReadValue<TrsX>(VerticesAddr + (uint)Index * (uint)Unsafe.SizeOf<TrsX>(), _useCache).s;

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // TRANSFORM HELPERS
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        public Vector3 TransformPoint(Vector3 localPt, SharedArray<TrsX> verts = null)
        {
            SharedArray<TrsX> local = null;
            try
            {
                verts ??= (local = ReadVertices());

                Vector3 p = localPt;
                int parent = Index;

                int iter = 0;
                while (parent >= 0)
                {
                    ref readonly var t = ref verts[parent];

                    p *= t.s;
                    p = t.q.Multiply(p);
                    p += t.t;

                    parent = Indices[parent];
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(iter++, MAX_ITER);
                }

                p.ThrowIfAbnormal();
                return p;
            }
            finally
            {
                local?.Dispose();
            }
        }

        public Vector3 InverseTransformPoint(Vector3 worldPt, SharedArray<TrsX> verts = null)
        {
            SharedArray<TrsX> local = null;
            try
            {
                verts ??= (local = ReadVertices());

                Vector3 pos = verts[Index].t;
                Quaternion rot = verts[Index].q;
                Vector3 scale = verts[Index].s;

                int parent = Indices[Index];
                int iter = 0;

                // Build full transform
                while (parent >= 0)
                {
                    ref readonly var t = ref verts[parent];
                    pos = t.q.Multiply(pos) * t.s + t.t;
                    rot = t.q * rot;

                    parent = Indices[parent];
                    ArgumentOutOfRangeException.ThrowIfGreaterThan(iter++, MAX_ITER);
                }

                // Inverse
                Vector3 localPt = Quaternion.Conjugate(rot).Multiply(worldPt - pos);
                return localPt / scale;
            }
            finally
            {
                local?.Dispose();
            }
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // VALIDATION
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidatePosition(ref Vector3 p)
        {
            p.ThrowIfAbnormal();

            // Prevent (0,0,0) flickers
            if (p.LengthSquared() < MIN_VALID_DIST * MIN_VALID_DIST)
                throw new ArgumentOutOfRangeException(nameof(p), "Invalid (origin) position");
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // STRUCTS
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        [StructLayout(LayoutKind.Explicit)]
        private readonly ref struct TransformAccess
        {
            [FieldOffset((int)UnityOffsets.TransformAccess.IndexOffset)]
            public readonly int Index;

            [FieldOffset((int)UnityOffsets.TransformAccess.HierarchyOffset)]
            public readonly ulong Hierarchy;
        }

        [StructLayout(LayoutKind.Explicit)]
        public readonly ref struct TransformHierarchy
        {
            [FieldOffset((int)UnityOffsets.TransformAccess.Vertices)]
            public readonly ulong Vertices;

            [FieldOffset((int)UnityOffsets.TransformAccess.Indices)]
            public readonly ulong Indices;

            public const uint RootPosOffset = 0x90;
        }

        [StructLayout(LayoutKind.Sequential)]
        public readonly struct TrsX
        {
            public readonly Vector3 t;
            public readonly float pad0;
            public readonly Quaternion q;
            public readonly Vector3 s;
            public readonly float pad1;
        }

        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        // READ MEMORY
        // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        private int[] ReadIndices()
        {
            var arr = new int[Count];
            Memory.ReadBuffer(IndicesAddr, arr.AsSpan(), _useCache);
            return arr;
        }

        public SharedArray<TrsX> ReadVertices()
        {
            var verts = SharedArray<TrsX>.Get(Count);
            try
            {
                Memory.ReadBuffer(VerticesAddr, verts.Span, _useCache);
                return verts;
            }
            catch
            {
                verts.Dispose();
                throw;
            }
        }
    }

    // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    // QUATERNION EXTENSIONS
    // ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
    public static class UnityTransformExtensions
    {
        private static readonly Vector3 L = new(-1, 0, 0);
        private static readonly Vector3 R = new(1, 0, 0);
        private static readonly Vector3 U = new(0, 1, 0);
        private static readonly Vector3 D = new(0, -1, 0);
        private static readonly Vector3 F = new(0, 0, 1);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Left(this Quaternion q) => q.Multiply(L);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Right(this Quaternion q) => q.Multiply(R);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Up(this Quaternion q) => q.Multiply(U);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Down(this Quaternion q) => q.Multiply(D);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Forward(this Quaternion q) => q.Multiply(F);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Vector3 Multiply(this Quaternion q, Vector3 v)
        {
            Matrix4x4 m = Matrix4x4.CreateFromQuaternion(q);
            return Vector3.Transform(v, m);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(this Vector3 v) =>
            float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsFinite(this Quaternion q) =>
            float.IsFinite(q.X) && float.IsFinite(q.Y) &&
            float.IsFinite(q.Z) && float.IsFinite(q.W);        
    }
}
