using System.IO;
using eft_dma_radar.Silk.Tarkov.Unity;
using eft_dma_radar.Silk.Tarkov.Unity.Collections;
using VmmSharpEx;
using VmmSharpEx.Options;

using static eft_dma_radar.Silk.Tarkov.Unity.UnityOffsets;

namespace eft_dma_radar.Silk.Tarkov.GameWorld
{
    /// <summary>
    /// Resolves FPS + Optic cameras and provides scatter-batched ViewMatrix/FOV reads.
    /// Exposes a static <see cref="WorldToScreen"/> method used by the advanced aimview
    /// and (future) ESP overlay.
    /// <para>
    /// <b>Resolution order:</b>
    /// <list type="number">
    ///   <item>IL2CPP EFT.CameraControl.CameraManager.Instance (primary).</item>
    ///   <item>Unity AllCameras static + GameObject name search (fallback).</item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed class CameraManager
    {
        #region Static State

        private static ulong _eftCameraManagerInstance;
        private static ulong _eftCameraManagerClassPtr;
        private static ulong _allCamerasAddr;
        private static bool _staticInitDone;

        // -- Camera offset cache -------------------------------------------------

        private static readonly string CameraCacheFilePath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                         "eft-dma-radar-silk", "camera_offsets.json");

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private sealed class CameraOffsetCache
        {
            public uint UnityPlayerTimestamp { get; set; }
            public uint UnityPlayerSizeOfImage { get; set; }
            public ulong AllCamerasRva { get; set; }
            public uint ViewMatrix { get; set; }
            public uint FOV { get; set; }
            public uint AspectRatio { get; set; }
        }

        #endregion

        #region Static W2S State

        private const int VIEWPORT_TOLERANCE = 800;

        /// <summary>True if the CameraManager is active and reading data.</summary>
        public static bool IsActive { get; private set; }

        /// <summary>Game Viewport width (pixels).</summary>
        public static int ViewportWidth { get; private set; }

        /// <summary>Game Viewport height (pixels).</summary>
        public static int ViewportHeight { get; private set; }

        /// <summary>Center of the game viewport.</summary>
        public static Vector2 ViewportCenter => new(ViewportWidth / 2f, ViewportHeight / 2f);

        /// <summary>True if the local player's optic camera is active (scope).</summary>
        public static bool IsScoped { get; private set; }

        /// <summary>True if the local player is Aiming Down Sights.</summary>
        public static bool IsADS { get; private set; }

        private static float _fov;
        private static float _aspect;
        private static readonly ViewMatrix _viewMatrix = new();

        private static float _jitterX;
        private static float _jitterY;

        // Cached scoped projection values — recomputed in UpdateCamera when FOV/Aspect changes,
        // avoids MathF.Cos/Sin on every WorldToScreen call while scoped.
        private static float _scopedScaleX;
        private static float _scopedScaleY;

        /// <summary>
        /// Update the Viewport dimensions for W2S calculations.
        /// Call once at CameraManager init or when config changes.
        /// </summary>
        public static void UpdateViewportRes(int width, int height)
        {
            ViewportWidth = width;
            ViewportHeight = height;
            Log.WriteLine($"[CameraManager] Viewport set to {width}x{height}");
        }

        #endregion

        #region Instance Fields

        /// <summary>FPS Camera pointer (unscoped).</summary>
        public ulong FPSCamera { get; }

        /// <summary>Optic Camera pointer (ads/scoped).</summary>
        public ulong OpticCamera { get; }

        #endregion

        #region Constructor / Init

        static CameraManager()
        {
            Memory.GameStopped += (_, _) =>
            {
                _eftCameraManagerInstance = default;
                _eftCameraManagerClassPtr = default;
                _allCamerasAddr = default;
                _staticInitDone = false;
                IsActive = false;
                IsScoped = false;
                IsADS = false;
            };
        }

        public CameraManager()
        {
            if (!TryResolveCameras(out var fpsCam, out var opticCam))
                throw new InvalidOperationException("[CameraManager] Failed to resolve FPS/Optic cameras.");

            FPSCamera = fpsCam;
            OpticCamera = opticCam;
            IsActive = true;

            Log.WriteLine($"[CameraManager] FPSCamera:   0x{FPSCamera:X}");
            Log.WriteLine($"[CameraManager] OpticCamera: 0x{OpticCamera:X}");
        }

        /// <summary>
        /// Pre-warms static camera data on game startup (once per game session).
        /// Tries to restore AllCameras address and Camera struct offsets from a
        /// cached file, falling back to signature scans if the cache is stale.
        /// </summary>
        public static void Initialize()
        {
            if (_staticInitDone)
                return;
            try
            {
                if (TryLoadCameraCache())
                {
                    _staticInitDone = true;
                    return;
                }

                _allCamerasAddr = ResolveAllCamerasAddr();
                ResolveCameraOffsets();
                _staticInitDone = true;

                SaveCameraCache();
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[CameraManager] Static init failed: {ex.Message}");
            }
        }

        #endregion

        #region WorldToScreen

        /// <summary>
        /// Translates a 3D world position to a 2D screen position using the live ViewMatrix.
        /// </summary>
        /// <param name="worldPos">World-space position.</param>
        /// <param name="scrPos">Screen-space position (pixels).</param>
        /// <param name="onScreenCheck">If true, returns false when off-screen.</param>
        /// <param name="useTolerance">If true, expands the on-screen check by <see cref="VIEWPORT_TOLERANCE"/>.</param>
        /// <returns>True if the projection succeeded.</returns>
        public static bool WorldToScreen(ref Vector3 worldPos, out Vector2 scrPos, bool onScreenCheck = false, bool useTolerance = false)
        {
            // Reject positions at or near world origin
            if (worldPos.LengthSquared() < 1f)
            {
                scrPos = default;
                return false;
            }

            float w = Vector3.Dot(_viewMatrix.Translation, worldPos) + _viewMatrix.M44;

            if (w < 0.098f)
            {
                scrPos = default;
                return false;
            }

            float x = Vector3.Dot(_viewMatrix.Right, worldPos) + _viewMatrix.M14;
            float y = Vector3.Dot(_viewMatrix.Up, worldPos) + _viewMatrix.M24;

            // TAA / DLSS jitter compensation
            x += _jitterX * w;
            y += _jitterY * w;

            if (IsScoped)
            {
                x *= _scopedScaleX;
                y *= _scopedScaleY;
            }

            var center = ViewportCenter;
            scrPos = new Vector2(
                center.X * (1f + x / w),
                center.Y * (1f - y / w));

            if (onScreenCheck)
            {
                int left = useTolerance ? -VIEWPORT_TOLERANCE : 0;
                int right = useTolerance ? ViewportWidth + VIEWPORT_TOLERANCE : ViewportWidth;
                int top = useTolerance ? -VIEWPORT_TOLERANCE : 0;
                int bottom = useTolerance ? ViewportHeight + VIEWPORT_TOLERANCE : ViewportHeight;

                if (scrPos.X < left || scrPos.X > right ||
                    scrPos.Y < top || scrPos.Y > bottom)
                {
                    scrPos = default;
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Returns the FOV magnitude (distance from screen center) for a screen point.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetFovMagnitude(Vector2 point)
        {
            return Vector2.Distance(ViewportCenter, point);
        }

        /// <summary>
        /// Builds a synthetic ViewMatrix from a world-space position and EFT rotation angles,
        /// using the same transposed convention as the live game view matrix.
        /// </summary>
        public static ViewMatrix BuildViewMatrix(Vector3 position, float yawDeg, float pitchDeg)
        {
            float yaw = yawDeg * (MathF.PI / 180f);
            float pitch = -pitchDeg * (MathF.PI / 180f);

            float cy = MathF.Cos(yaw), sy = MathF.Sin(yaw);
            float cp = MathF.Cos(pitch), sp = MathF.Sin(pitch);

            var forward = new Vector3(sy * cp, sp, cy * cp);
            var right = new Vector3(cy, 0f, -sy);
            var up = new Vector3(-sy * sp, cp, -cy * sp);

            return new ViewMatrix
            {
                Translation = forward,
                Right = right,
                Up = up,
                M44 = -Vector3.Dot(forward, position),
                M14 = -Vector3.Dot(right, position),
                M24 = -Vector3.Dot(up, position),
            };
        }

        #endregion

        #region Scatter Read (Camera Worker)

        /// <summary>
        /// Updates camera data via VmmScatter — called from the camera worker.
        /// Reads ViewMatrix from the active camera and FOV/Aspect from FPS camera.
        /// </summary>
        public void UpdateCamera(LocalPlayer? localPlayer)
        {
            IsADS = localPlayer?.IsADS ?? false;
            IsScoped = IsADS && CheckIfScoped(localPlayer!);

            ulong camera = (IsADS && IsScoped && OpticCamera.IsValidVirtualAddress())
                ? OpticCamera
                : FPSCamera;

            if (!camera.IsValidVirtualAddress())
                return;

            ulong vmAddr = camera + Camera.ViewMatrix;

            // Single scatter: ViewMatrix + FOV + Aspect
            using var scatter = Memory.GetScatter(VmmFlags.NOCACHE);
            scatter.PrepareReadValue<Matrix4x4>(vmAddr);

            if (FPSCamera.IsValidVirtualAddress())
            {
                scatter.PrepareReadValue<float>(FPSCamera + Camera.FOV);
                scatter.PrepareReadValue<float>(FPSCamera + Camera.AspectRatio);
            }

            scatter.Execute();

            // Process ViewMatrix
            if (scatter.ReadValue<Matrix4x4>(vmAddr, out var vm))
            {
                _viewMatrix.Update(ref vm);
                _jitterX = _viewMatrix.JitterX;
                _jitterY = _viewMatrix.JitterY;
            }

            // Process FOV + Aspect
            if (FPSCamera.IsValidVirtualAddress())
            {
                bool fovChanged = false;
                if (scatter.ReadValue<float>(FPSCamera + Camera.FOV, out var fov) && fov > 1f && fov < 180f)
                {
                    fovChanged = fov != _fov;
                    _fov = fov;
                }

                if (scatter.ReadValue<float>(FPSCamera + Camera.AspectRatio, out var aspect) && aspect > 0.1f && aspect < 5f)
                {
                    fovChanged |= aspect != _aspect;
                    _aspect = aspect;
                }

                // Recompute cached scoped projection scale when FOV/Aspect changes
                if (fovChanged && _fov > 0f && _aspect > 0f)
                {
                    float angleRadHalf = (MathF.PI / 180f) * _fov * 0.5f;
                    float angleCtg = MathF.Cos(angleRadHalf) / MathF.Sin(angleRadHalf);
                    _scopedScaleX = 1f / (angleCtg * _aspect * 0.5f);
                    _scopedScaleY = 1f / (angleCtg * 0.5f);
                }
            }
        }

        /// <summary>
        /// Checks if the local player is currently scoped (zoom > 1x).
        /// </summary>
        private bool CheckIfScoped(LocalPlayer localPlayer)
        {
            try
            {
                if (!OpticCamera.IsValidVirtualAddress())
                    return false;

                if (localPlayer.PWA == 0)
                    return false;

                if (!Memory.TryReadPtr(localPlayer.PWA + Offsets.ProceduralWeaponAnimation._optics, out var opticsPtr, false))
                    return false;

                using var optics = MemList<ulong>.Get(opticsPtr);
                if (optics.Count <= 0)
                    return false;

                var pSightComponent = Memory.ReadPtr(optics[0] + Offsets.SightNBone.Mod);
                if (!pSightComponent.IsValidVirtualAddress())
                    return false;

                var scopeZoomValue = Memory.ReadValue<float>(pSightComponent + Offsets.SightComponent.ScopeZoomValue, false);
                return scopeZoomValue > 1f;
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Camera Resolution

        /// <summary>
        /// Multi-path resolver:
        ///  1) EFT.CameraControl.CameraManager.Instance
        ///  2) Unity AllCameras + GameObject name search
        /// </summary>
        private static bool TryResolveCameras(out ulong fpsCamera, out ulong opticCamera)
        {
            _eftCameraManagerInstance = FindCameraManagerInstance();
            if (_eftCameraManagerInstance.IsValidVirtualAddress())
            {
                Log.WriteLine($"[CameraManager] CameraManager.Instance @ 0x{_eftCameraManagerInstance:X}");
                if (TryResolveViaCameraManagerInstance(out fpsCamera, out opticCamera))
                {
                    Log.WriteLine("[CameraManager] Using CameraManager.Instance cameras.");
                    return true;
                }
                Log.WriteLine("[CameraManager] Instance found but camera fields unreadable — falling back.");
            }

            if (TryResolveViaAllCamerasByName(out fpsCamera, out opticCamera))
            {
                Log.WriteLine("[CameraManager] Using Unity AllCameras fallback.");
                return true;
            }

            fpsCamera = 0;
            opticCamera = 0;
            Log.WriteLine("[CameraManager] Could not resolve cameras via any path.");
            return false;
        }

        /// <summary>
        /// Primary path: use EFT.CameraControl.CameraManager.Instance.
        /// </summary>
        private static bool TryResolveViaCameraManagerInstance(out ulong fpsCamera, out ulong opticCamera)
        {
            fpsCamera = 0;
            opticCamera = 0;

            if (!_eftCameraManagerInstance.IsValidVirtualAddress())
                return false;

            // FPS camera
            if (!Memory.TryReadPtr(_eftCameraManagerInstance + Offsets.EFTCameraManager.Camera, out var fpsCameraRef, false))
                return false;

            if (!TryReadObjectClassName(fpsCameraRef, out var name, 32)
                || !string.Equals(name, "Camera", StringComparison.Ordinal))
                return false;

            if (!Memory.TryReadPtr(fpsCameraRef + ObjectClass.MonoBehaviourOffset, out fpsCamera, false))
                return false;

            if (!ValidateCameraMatrix(fpsCamera))
            {
                fpsCamera = 0;
                return false;
            }

            // Optic camera
            if (!Memory.TryReadPtr(_eftCameraManagerInstance + Offsets.EFTCameraManager.OpticCameraManager, out var opticCameraManager, false))
                return false;

            if (!Memory.TryReadPtr(opticCameraManager + Offsets.OpticCameraManager.Camera, out var opticCameraRef, false))
                return false;

            if (!TryReadObjectClassName(opticCameraRef, out name, 32)
                || !string.Equals(name, "Camera", StringComparison.Ordinal))
                return false;

            if (!Memory.TryReadPtr(opticCameraRef + ObjectClass.MonoBehaviourOffset, out opticCamera, false))
                return false;

            return true;
        }

        /// <summary>
        /// Backup path: Unity AllCameras static + GameObject name search.
        /// </summary>
        private static bool TryResolveViaAllCamerasByName(out ulong fpsCamera, out ulong opticCamera)
        {
            fpsCamera = 0;
            opticCamera = 0;

            try
            {
                if (!_allCamerasAddr.IsValidVirtualAddress())
                    return false;

                if (!Memory.TryReadPtr(_allCamerasAddr, out var allCamerasPtr, false))
                    return false;

                if (!Memory.TryReadPtr(allCamerasPtr + 0x0, out var itemsPtr, false) ||
                    !Memory.TryReadValue<int>(allCamerasPtr + 0x8, out var count, false))
                    return false;

                if (!itemsPtr.IsValidVirtualAddress() || count <= 0 || count > 1024)
                    return false;

                FindCamerasByName(itemsPtr, count, out fpsCamera, out opticCamera);

                if (!fpsCamera.IsValidVirtualAddress() || !ValidateCameraMatrix(fpsCamera))
                    fpsCamera = 0;

                if (!opticCamera.IsValidVirtualAddress())
                    opticCamera = 0;

                return fpsCamera != 0 && opticCamera != 0;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[CameraManager] AllCameras fallback error: {ex.Message}");
                fpsCamera = 0;
                opticCamera = 0;
                return false;
            }
        }

        /// <summary>
        /// Scans AllCameras list for "FPS Camera" / "Optic Camera" style names.
        /// </summary>
        private static void FindCamerasByName(ulong itemsPtr, int count, out ulong fpsCamera, out ulong opticCamera)
        {
            fpsCamera = 0;
            opticCamera = 0;

            int max = Math.Min(count, 100);

            for (int i = 0; i < max; i++)
            {
                ulong entryAddr = itemsPtr + (uint)(i * 0x8);
                if (!Memory.TryReadPtr(entryAddr, out var cameraPtr, false))
                    continue;

                // Component -> GameObject -> Name
                if (!Memory.TryReadPtr(cameraPtr + GO_ObjectClass, out var gameObject, false))
                    continue;

                if (!Memory.TryReadPtr(gameObject + GO_Name, out var namePtr, false))
                    continue;

                // GameObject names are native C-strings (UTF-8), not Unity managed strings
                if (!Memory.TryReadString(namePtr, out var goName, 64, false) || string.IsNullOrEmpty(goName))
                    continue;

                bool isFps =
                    goName.Contains("FPS", StringComparison.OrdinalIgnoreCase) &&
                    goName.Contains("Camera", StringComparison.OrdinalIgnoreCase);

                bool isOptic =
                    (goName.Contains("Optic", StringComparison.OrdinalIgnoreCase) ||
                     goName.Contains("BaseOptic", StringComparison.OrdinalIgnoreCase)) &&
                    goName.Contains("Camera", StringComparison.OrdinalIgnoreCase);

                if (isFps && fpsCamera == 0)
                    fpsCamera = cameraPtr;

                if (isOptic && opticCamera == 0)
                    opticCamera = cameraPtr;

                if (fpsCamera != 0 && opticCamera != 0)
                    break;
            }
        }

        /// <summary>
        /// Quick sanity check for a camera's view matrix.
        /// </summary>
        private static bool ValidateCameraMatrix(ulong cameraPtr)
        {
            if (!Memory.TryReadValue<Matrix4x4>(cameraPtr + Camera.ViewMatrix, out var vm, false))
                return false;

            if (float.IsNaN(vm.M11) || float.IsInfinity(vm.M11) ||
                float.IsNaN(vm.M22) || float.IsInfinity(vm.M22) ||
                float.IsNaN(vm.M33) || float.IsInfinity(vm.M33) ||
                float.IsNaN(vm.M44) || float.IsInfinity(vm.M44))
                return false;

            if (vm.M11 == 0f && vm.M22 == 0f && vm.M33 == 0f && vm.M44 == 0f)
                return false;

            if (Math.Abs(vm.M41) > 5000f || Math.Abs(vm.M42) > 5000f || Math.Abs(vm.M43) > 5000f)
                return false;

            return true;
        }

        /// <summary>
        /// Reads the ObjectClass name from a given object pointer (ObjectClass → +0x0 → +0x10 → C-string).
        /// This is an IL2CPP class name (null-terminated UTF-8), NOT a Unity managed string.
        /// </summary>
        private static bool TryReadObjectClassName(ulong objectClassPtr, out string? name, int maxLen)
        {
            name = null;
            if (!objectClassPtr.IsValidVirtualAddress())
                return false;

            if (!Memory.TryReadPtrChain(objectClassPtr, ObjClass_ToNamePtr, out var namePtr, false))
                return false;

            return Memory.TryReadString(namePtr, out name, maxLen, false) && !string.IsNullOrEmpty(name);
        }

        #endregion

        #region CameraManager.Instance Resolution

        /// <summary>
        /// Pattern scan to find EFT.CameraControl.CameraManager.Instance via GameAssembly.dll.
        /// </summary>
        private static ulong FindCameraManagerInstance()
        {
            try
            {
                var gameAssemblyBase = Memory.GameAssemblyBase;
                if (!gameAssemblyBase.IsValidVirtualAddress())
                    return 0;

                ulong methodAddr = gameAssemblyBase + Offsets.EFTCameraManager.GetInstance_RVA;

                Span<byte> methodBytes = stackalloc byte[128];
                if (!Memory.TryReadBuffer(methodAddr, methodBytes, false))
                    return 0;

                // Pattern 1: lea rcx, [rip+offset] → class metadata
                for (int i = 0; i < methodBytes.Length - 7; i++)
                {
                    if (methodBytes[i] == 0x48 && methodBytes[i + 1] == 0x8D && methodBytes[i + 2] == 0x0D)
                    {
                        int disp32 = BitConverter.ToInt32(methodBytes.Slice(i + 3, 4));
                        ulong classMetadataAddr = methodAddr + (ulong)i + 7 + (ulong)disp32;

                        if (!Memory.TryReadPtr(classMetadataAddr, out var classPtr, false))
                            continue;

                        var knownOffset = Offsets.Il2CppClass.StaticFields;
                        ReadOnlySpan<uint> fallbackOffsets = [knownOffset - 0x10, knownOffset - 0x08, knownOffset + 0x08, knownOffset + 0x10, knownOffset + 0x18];

                        if (TryReadStaticInstance(classPtr, knownOffset, out var instance))
                        {
                            _eftCameraManagerClassPtr = classPtr;
                            return instance;
                        }

                        foreach (var offset in fallbackOffsets)
                        {
                            if (offset == knownOffset) continue;
                            if (TryReadStaticInstance(classPtr, offset, out instance))
                            {
                                _eftCameraManagerClassPtr = classPtr;
                                return instance;
                            }
                        }
                    }
                }

                // Pattern 2: mov rax, [rip+offset] → direct static field
                for (int i = 32; i < methodBytes.Length - 7; i++)
                {
                    if (methodBytes[i] == 0x48 && methodBytes[i + 1] == 0x8B && methodBytes[i + 2] == 0x05)
                    {
                        int disp32 = BitConverter.ToInt32(methodBytes.Slice(i + 3, 4));
                        ulong staticFieldAddr = methodAddr + (ulong)i + 7 + (ulong)disp32;

                        if (!Memory.TryReadPtr(staticFieldAddr, out var instancePtr, false))
                            continue;

                        if (Memory.TryReadPtr(instancePtr + Offsets.EFTCameraManager.Camera, out var testCamera, false)
                            && testCamera.IsValidVirtualAddress())
                            return instancePtr;
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[CameraManager] FindInstance error: {ex.Message}");
                return 0;
            }
        }

        private static bool TryReadStaticInstance(ulong classPtr, uint staticFieldsOffset, out ulong instance)
        {
            instance = 0;

            if (!Memory.TryReadPtr(classPtr + staticFieldsOffset, out var staticFieldsPtr, false))
                return false;

            if (!Memory.TryReadPtr(staticFieldsPtr, out var instancePtr, false))
                return false;

            if (!Memory.TryReadPtr(instancePtr + Offsets.EFTCameraManager.Camera, out var testCamera, false))
                return false;

            instance = instancePtr;
            return true;
        }

        #endregion

        #region AllCameras Resolution

        /// <summary>
        /// Candidate signatures for locating AllCameras global in UnityPlayer.dll.
        /// </summary>
        private static readonly (string Sig, int RelOffset, int InstrLen, string Desc)[] AllCamerasSigs =
        [
            ("48 8B 05 ? ? ? ? 49 C7 C6 ? ? ? ? 8B 48 ? 85 C9 0F 84 ? ? ? ? 48 89 9C 24", 3, 7, "AllCameras: mov rax,[rip]; mov r14,imm; test ecx; jz; mov [rsp],rbx"),
            ("4C 8B 05 ? ? ? ? 33 D2 49 8B 48", 3, 7, "AllCameras: mov r8,[rip]; xor edx; mov rcx,[r8]"),
            ("48 8B 05 ? ? ? ? 49 C7 C6 ? ? ? ? 8B 48 ? 85 C9 0F 84 ? ? ? ? 48 89 B4 24", 3, 7, "AllCameras: mov rax,[rip]; mov r14,imm; test ecx; jz; mov [rsp],rsi"),
            ("48 8B 1D ? ? ? ? 48 8B 73 ? 48 8B 43 ? 48 FF C6", 3, 7, "AllCameras: mov rbx,[rip]; mov rsi,[rbx]; mov rax,[rbx]; inc rsi"),
        ];

        private static ulong ResolveAllCamerasAddr()
        {
            var unityBase = Memory.UnityBase;
            if (!unityBase.IsValidVirtualAddress())
                return 0;

            foreach (var (sig, relOff, instrLen, desc) in AllCamerasSigs)
            {
                var sigAddr = Memory.FindSignature(sig, "UnityPlayer.dll");
                if (sigAddr == 0)
                    continue;

                if (!Memory.TryReadValue<int>(sigAddr + (ulong)relOff, out var disp32, false))
                    continue;

                ulong resolved = sigAddr + (ulong)instrLen + (ulong)(long)disp32;
                if (!resolved.IsValidVirtualAddress())
                    continue;

                if (!Memory.TryReadPtr(resolved, out var listPtr, false))
                    continue;

                if (!Memory.TryReadPtr(listPtr, out var items, false))
                    continue;

                if (!Memory.TryReadValue<int>(listPtr + 0x8, out var count, false))
                    continue;

                if (items.IsValidVirtualAddress() && count >= 0 && count < 1024)
                    return resolved;
            }

            // Fallback: hardcoded offset
            var fallbackAddr = unityBase + AllCameras;
            if (fallbackAddr.IsValidVirtualAddress())
            {
                Log.WriteLine("[CameraManager] AllCameras sig scan missed — using hardcoded fallback.");
                return fallbackAddr;
            }

            Log.WriteLine("[CameraManager] AllCameras resolution FAILED.");
            return 0;
        }

        private static bool ValidateAllCamerasAddr(ulong addr)
        {
            if (!addr.IsValidVirtualAddress())
                return false;

            if (!Memory.TryReadPtr(addr, out var listPtr, false))
                return false;

            if (!Memory.TryReadPtr(listPtr, out var items, false))
                return false;

            if (!Memory.TryReadValue<int>(listPtr + 0x8, out var count, false))
                return false;

            return items.IsValidVirtualAddress() && count >= 0 && count < 1024;
        }

        #endregion

        #region Camera Struct Offset Sig Scan

        private readonly record struct CameraOffsetSig(
            string Sig,
            int OffsetPos,
            int DispSize,
            bool IsCallSite,
            int TargetBodyDispOffset,
            int TargetBodyDispSize,
            string Desc);

        private static readonly CameraOffsetSig[] ViewMatrixSigs =
        [
            new("E8 ? ? ? ? 48 3B 58 ? 0F 83 ? ? ? ? ? ? ? 48 8D 0C 5D ? ? ? ? 48 03 CB ? ? ? ? E8 ? ? ? ? 4C 8B C7 49 FF C0 ? ? ? ? ? 75",
                0, 4, IsCallSite: true, TargetBodyDispOffset: 3, TargetBodyDispSize: 4,
                "ViewMatrix call-site: call GetWorldToCameraMatrix"),
        ];

        private static readonly CameraOffsetSig[] FovSigs =
        [
            new("83 B9 ? ? ? ? 02 75 ? F3 0F 10 81 ? ? ? ? C3 F3 0F 10 81 ? ? ? ? C3", 22, 4, IsCallSite: false, 0, 0,
                "GetFieldOfView: cmp [rcx+?],2; movss xmm0,[rcx+FOV]; ret"),
        ];

        private static readonly CameraOffsetSig[] AspectRatioSigs =
        [
            new("E8 ? ? ? ? F3 44 0F 59 05 ? ? ? ? F3 0F 59 C6",
                0, 4, IsCallSite: true, TargetBodyDispOffset: 4, TargetBodyDispSize: 4,
                "AspectRatio call-site: call get_aspect"),
        ];

        private static void ResolveCameraOffsets()
        {
            var unityBase = Memory.UnityBase;
            if (!unityBase.IsValidVirtualAddress())
                return;

            ApplyCameraOffset(ViewMatrixSigs, "ViewMatrix", unityBase, ref Camera.ViewMatrix);
            ApplyCameraOffset(FovSigs, "FOV", unityBase, ref Camera.FOV);
            ApplyCameraOffset(AspectRatioSigs, "AspectRatio", unityBase, ref Camera.AspectRatio);
        }

        private static void ApplyCameraOffset(CameraOffsetSig[] sigs, string fieldName, ulong unityBase, ref uint target)
        {
            var resolved = TryResolveCameraOffset(sigs, unityBase);
            if (resolved.HasValue && resolved.Value != target)
            {
                Log.WriteLine($"[CameraManager] Camera.{fieldName} UPDATED: 0x{target:X} → 0x{resolved.Value:X}");
                target = resolved.Value;
            }
            else if (!resolved.HasValue)
            {
                Log.WriteLine($"[CameraManager] Camera.{fieldName} sig scan FAILED — using hardcoded 0x{target:X}");
            }
        }

        private static uint? TryResolveCameraOffset(CameraOffsetSig[] sigs, ulong unityBase)
        {
            foreach (var entry in sigs)
            {
                var sigAddr = Memory.FindSignature(entry.Sig, "UnityPlayer.dll");
                if (sigAddr == 0)
                    continue;

                uint offset;

                if (entry.IsCallSite)
                {
                    if (!Memory.TryReadValue<int>(sigAddr + (ulong)entry.OffsetPos + 1, out var callRel32, false))
                        continue;
                    ulong callTarget = sigAddr + 5 + (ulong)(long)callRel32;

                    if (!callTarget.IsValidVirtualAddress())
                        continue;

                    offset = entry.TargetBodyDispSize switch
                    {
                        1 => Memory.TryReadValue<byte>(callTarget + (ulong)entry.TargetBodyDispOffset, out var b, false) ? b : 0u,
                        4 => Memory.TryReadValue<uint>(callTarget + (ulong)entry.TargetBodyDispOffset, out var u, false) ? u : 0u,
                        _ => 0,
                    };
                }
                else
                {
                    offset = entry.DispSize switch
                    {
                        1 => Memory.TryReadValue<byte>(sigAddr + (ulong)entry.OffsetPos, out var b, false) ? b : 0u,
                        4 => Memory.TryReadValue<uint>(sigAddr + (ulong)entry.OffsetPos, out var u, false) ? u : 0u,
                        _ => 0,
                    };
                }

                if (offset > 0 && offset < 0x1000)
                    return offset;
            }

            return null;
        }

        #endregion

        #region Camera Offset Cache

        private static bool TryLoadCameraCache()
        {
            try
            {
                if (!File.Exists(CameraCacheFilePath))
                    return false;

                var unityBase = Memory.UnityBase;
                if (!unityBase.IsValidVirtualAddress())
                    return false;

                var (timestamp, sizeOfImage) = Memory.ReadPeFingerprint(unityBase);
                if (timestamp == 0 || sizeOfImage == 0)
                    return false;

                var json = File.ReadAllText(CameraCacheFilePath);
                var cache = JsonSerializer.Deserialize<CameraOffsetCache>(json, _jsonOpts);
                if (cache is null)
                    return false;

                if (cache.UnityPlayerTimestamp != timestamp || cache.UnityPlayerSizeOfImage != sizeOfImage)
                {
                    Log.WriteLine("[CameraManager] Camera cache PE mismatch — will sig-scan.");
                    return false;
                }

                if (cache.AllCamerasRva == 0 || cache.ViewMatrix == 0 || cache.FOV == 0 || cache.AspectRatio == 0)
                    return false;

                ulong resolvedAddr = unityBase + cache.AllCamerasRva;
                if (!ValidateAllCamerasAddr(resolvedAddr))
                {
                    Log.WriteLine("[CameraManager] Camera cache AllCameras validation failed — will sig-scan.");
                    return false;
                }

                _allCamerasAddr = resolvedAddr;
                Camera.ViewMatrix = cache.ViewMatrix;
                Camera.FOV = cache.FOV;
                Camera.AspectRatio = cache.AspectRatio;

                Log.WriteLine($"[CameraManager] Offsets restored from cache (VM=0x{cache.ViewMatrix:X}, FOV=0x{cache.FOV:X}, AR=0x{cache.AspectRatio:X})");
                return true;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[CameraManager] Cache load failed: {ex.Message}");
                return false;
            }
        }

        private static void SaveCameraCache()
        {
            try
            {
                var unityBase = Memory.UnityBase;
                if (!unityBase.IsValidVirtualAddress() || !_allCamerasAddr.IsValidVirtualAddress())
                    return;

                var (timestamp, sizeOfImage) = Memory.ReadPeFingerprint(unityBase);

                var cache = new CameraOffsetCache
                {
                    UnityPlayerTimestamp = timestamp,
                    UnityPlayerSizeOfImage = sizeOfImage,
                    AllCamerasRva = _allCamerasAddr - unityBase,
                    ViewMatrix = Camera.ViewMatrix,
                    FOV = Camera.FOV,
                    AspectRatio = Camera.AspectRatio,
                };

                var json = JsonSerializer.Serialize(cache, _jsonOpts);
                Directory.CreateDirectory(Path.GetDirectoryName(CameraCacheFilePath)!);
                File.WriteAllText(CameraCacheFilePath, json);
                Log.WriteLine($"[CameraManager] Cache saved → {CameraCacheFilePath}");
            }
            catch (Exception ex)
            {
                Log.WriteLine($"[CameraManager] Cache save failed: {ex.Message}");
            }
        }

        #endregion
    }
}
