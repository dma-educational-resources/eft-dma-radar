/*
 * XM EFT DMA Radar
 * Brought to you by XM (XM DMA)
 * 
 * MIT License
 */

using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.DMA.ScatterAPI;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity;
using eft_dma_radar.Common.Unity.Collections;
using eft_dma_radar.Tarkov.EFTPlayer;
using eft_dma_radar.Tarkov.Unity.IL2CPP;
using static eft_dma_radar.Common.Unity.UnityOffsets;
using ObjectClass = eft_dma_radar.Common.Unity.ObjectClass;
using SDK;

namespace eft_dma_radar.Tarkov.GameWorld
{
    /// <summary>
    /// IL2CPP Camera manager:
    ///  - Primary: EFT.CameraControl.CameraManager.Instance
    ///  - Backup:  Unity AllCameras + GameObject name search
    /// </summary>
    public sealed class CameraManager : CameraManagerBase
    {
        private static ulong _eftCameraManagerInstance;

        /// <summary>
        /// FPS Camera (unscoped).
        /// </summary>
        public override ulong FPSCamera { get; }

        /// <summary>
        /// Optic Camera (ads/scoped).
        /// </summary>
        public override ulong OpticCamera { get; }

        // Optional debug fields
        public static ulong ThermalVision;
        public static ulong NightVision;
        public static ulong FPSCamera_;

        public CameraManager() : base()
        {
            if (!TryResolveCameras(out var fpsCam, out var opticCam))
                throw new InvalidOperationException("[CameraManager] Failed to resolve FPS/Optic cameras via any path.");

            FPSCamera = fpsCam;
            OpticCamera = opticCam;

            FPSCamera_ = FPSCamera;

            XMLogging.WriteLine($"[CameraManager] FPSCamera:   0x{FPSCamera:X}");
            XMLogging.WriteLine($"[CameraManager] OpticCamera: 0x{OpticCamera:X}");
        }

        static CameraManager()
        {
            MemDMABase.GameStopped += MemDMA_GameStopped;
        }

        /// <summary>
        /// Initialize static data on game startup.
        /// This only pre-resolves CameraManager.Instance; actual cameras are resolved in ctor via TryResolveCameras().
        /// </summary>
        public static void Initialize()
        {
            try
            {
                _eftCameraManagerInstance = FindCameraManagerInstance();

                if (!_eftCameraManagerInstance.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine("[CameraManager] WARNING CameraManager.Instance not found - will fall back to AllCameras.");
                    XMLogging.WriteLine("[CameraManager] Radar will still work (cameras are optional).");
                    return;
                }

                XMLogging.WriteLine($"[CameraManager] OK Initialized CameraManager.Instance @ 0x{_eftCameraManagerInstance:X}");
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[CameraManager] FAILED Init: {ex.Message}");
                XMLogging.WriteLine("[CameraManager] Radar will still work (cameras are optional).");
                _eftCameraManagerInstance = 0;
            }
        }

        /// <summary>
        /// Multi-path resolver:
        ///  1) EFT.CameraControl.CameraManager.Instance
        ///  2) Unity AllCameras + GameObject name search
        /// </summary>
        private static bool TryResolveCameras(out ulong fpsCamera, out ulong opticCamera)
        {
            // 1) Primary: IL2CPP CameraManager singleton
            if (TryResolveViaCameraManagerInstance(out fpsCamera, out opticCamera))
            {
                XMLogging.WriteLine("[CameraManager] Using CameraManager.Instance cameras.");
                return true;
            }

            // 2) Backup: Unity AllCameras + name-based search
            if (TryResolveViaAllCamerasByName(out fpsCamera, out opticCamera))
            {
                XMLogging.WriteLine("[CameraManager] Using Unity AllCameras + name search fallback.");
                return true;
            }

            fpsCamera = 0;
            opticCamera = 0;
            XMLogging.WriteLine("[CameraManager] ERROR: Could not resolve cameras via any path.");
            return false;
        }

        /// <summary>
        /// Primary path: use EFT.CameraControl.CameraManager.Instance and its Camera / OpticCameraManager fields.
        /// </summary>
        private static bool TryResolveViaCameraManagerInstance(out ulong fpsCamera, out ulong opticCamera)
        {
            fpsCamera = 0;
            opticCamera = 0;

            try
            {
                if (!_eftCameraManagerInstance.IsValidVirtualAddress())
                {
                    var inst = FindCameraManagerInstance();
                    if (!inst.IsValidVirtualAddress())
                        return false;

                    _eftCameraManagerInstance = inst;
                }

                // FPS camera
                var fpsCameraRef = Memory.ReadPtr(_eftCameraManagerInstance + Offsets.EFTCameraManager.Camera, false);
                if (!fpsCameraRef.IsValidVirtualAddress())
                    return false;

                var name = ObjectClass.ReadName(fpsCameraRef, 32, false);
                if (!string.Equals(name, "Camera", StringComparison.Ordinal))
                    return false;

                fpsCamera = Memory.ReadPtr(fpsCameraRef + Offsets.EFTCameraManager.CameraDerefOffset, false);
                if (!fpsCamera.IsValidVirtualAddress() || !ValidateCameraMatrix(fpsCamera))
                    return false;

                // Optic camera
                var opticCameraManager = Memory.ReadPtr(_eftCameraManagerInstance + Offsets.EFTCameraManager.OpticCameraManager, false);
                if (!opticCameraManager.IsValidVirtualAddress())
                    return false;

                var opticCameraRef = Memory.ReadPtr(opticCameraManager + Offsets.OpticCameraManager.Camera, false);
                if (!opticCameraRef.IsValidVirtualAddress())
                    return false;

                name = ObjectClass.ReadName(opticCameraRef, 32, false);
                if (!string.Equals(name, "Camera", StringComparison.Ordinal))
                    return false;

                opticCamera = Memory.ReadPtr(opticCameraRef + Offsets.EFTCameraManager.CameraDerefOffset, false);
                if (!opticCamera.IsValidVirtualAddress())
                    return false;

                return true;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[CameraManager] TryResolveViaCameraManagerInstance FAILED: {ex}");
                fpsCamera = 0;
                opticCamera = 0;
                return false;
            }
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
                var unityBase = Memory.UnityBase;
                if (!unityBase.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine("[CameraManager] Unity base not loaded; cannot use AllCameras.");
                    return false;
                }

                // NOTE: adjust UnityOffsets.ModuleBase.AllCameras if your name differs
                var allCamerasStatic = unityBase + ModuleBase.AllCameras;
                if (!allCamerasStatic.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine("[CameraManager] AllCameras static address invalid.");
                    return false;
                }

                var allCamerasPtr = Memory.ReadPtr(allCamerasStatic, false);
                if (!allCamerasPtr.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine("[CameraManager] AllCameras pointer invalid.");
                    return false;
                }

                ulong itemsPtr;
                int count;

                try
                {
                    // Internal Unity list layout:
                    // [0x00] -> items array (camera*[])
                    // [0x08] -> int count
                    itemsPtr = Memory.ReadPtr(allCamerasPtr + 0x0, false);
                    count = Memory.ReadValue<int>(allCamerasPtr + 0x8, false);
                }
                catch (Exception ex)
                {
                    XMLogging.WriteLine($"[CameraManager] Failed reading AllCameras header: {ex}");
                    return false;
                }

                if (!itemsPtr.IsValidVirtualAddress() || count <= 0 || count > 1024)
                {
                    XMLogging.WriteLine($"[CameraManager] AllCameras list invalid: items=0x{itemsPtr:X}, count={count}");
                    return false;
                }

                XMLogging.WriteLine($"[CameraManager] AllCameras: items=0x{itemsPtr:X}, count={count}");

                FindCamerasByName(itemsPtr, count, out fpsCamera, out opticCamera);

                if (!fpsCamera.IsValidVirtualAddress() || !ValidateCameraMatrix(fpsCamera))
                {
                    XMLogging.WriteLine("[CameraManager] AllCameras fallback: FPS camera invalid/matrix failed.");
                    fpsCamera = 0;
                }

                if (!opticCamera.IsValidVirtualAddress())
                    opticCamera = 0;

                return fpsCamera != 0 && opticCamera != 0;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[CameraManager] TryResolveViaAllCamerasByName FAILED: {ex}");
                fpsCamera = 0;
                opticCamera = 0;
                return false;
            }
        }

        /// <summary>
        /// Scan AllCameras list for “FPS Camera” / “Optic Camera” style names.
        /// </summary>
        private static void FindCamerasByName(ulong itemsPtr, int count, out ulong fpsCamera, out ulong opticCamera)
        {
            fpsCamera = 0;
            opticCamera = 0;

            int max = Math.Min(count, 100);

            for (int i = 0; i < max; i++)
            {
                try
                {
                    ulong entryAddr = itemsPtr + (uint)(i * 0x8);
                    var cameraPtr = Memory.ReadPtr(entryAddr, false);
                    if (!cameraPtr.IsValidVirtualAddress())
                        continue;

                    // Component -> GameObject -> Name
                    var gameObject = Memory.ReadPtr(cameraPtr + UnityOffsets.GameObject.ObjectClassOffset, false);
                    if (!gameObject.IsValidVirtualAddress())
                        continue;

                    var namePtr = Memory.ReadPtr(gameObject + UnityOffsets.GameObject.NameOffset, false);
                    if (!namePtr.IsValidVirtualAddress())
                        continue;

                    var name = Memory.ReadUnityString(namePtr, 64, false);
                    if (string.IsNullOrEmpty(name))
                        continue;

                    bool isFps =
                        name.IndexOf("FPS", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        name.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) >= 0;

                    bool isOptic =
                        (name.IndexOf("Optic", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         name.IndexOf("BaseOptic", StringComparison.OrdinalIgnoreCase) >= 0) &&
                        name.IndexOf("Camera", StringComparison.OrdinalIgnoreCase) >= 0;

                    if (isFps && fpsCamera == 0)
                        fpsCamera = cameraPtr;

                    if (isOptic && opticCamera == 0)
                        opticCamera = cameraPtr;

                    if (fpsCamera != 0 && opticCamera != 0)
                        break;
                }
                catch
                {
                    // Ignore individual failures
                }
            }
        }

        /// <summary>
        /// Quick sanity check for a camera's view matrix.
        /// </summary>
        private static bool ValidateCameraMatrix(ulong cameraPtr)
        {
            try
            {
                var vmAddr = cameraPtr + UnityOffsets.Camera.ViewMatrix;
                var vm = Memory.ReadValue<Matrix4x4>(vmAddr, false);

                if (float.IsNaN(vm.M11) || float.IsInfinity(vm.M11) ||
                    float.IsNaN(vm.M22) || float.IsInfinity(vm.M22) ||
                    float.IsNaN(vm.M33) || float.IsInfinity(vm.M33) ||
                    float.IsNaN(vm.M44) || float.IsInfinity(vm.M44))
                    return false;

                if (vm.M11 == 0f && vm.M22 == 0f && vm.M33 == 0f && vm.M44 == 0f)
                    return false;

                // simple translation sanity
                if (Math.Abs(vm.M41) > 5000f || Math.Abs(vm.M42) > 5000f || Math.Abs(vm.M43) > 5000f)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Pattern scan to find EFT.CameraControl.CameraManager.Instance via GameAssembly.dll.
        /// </summary>
        private static ulong FindCameraManagerInstance()
        {
            try
            {
                // Get GameAssembly base (IL2CPP binary)
                var gameAssemblyBase = MemoryInterface.Memory.GameAssemblyBase;
                if (!gameAssemblyBase.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine("[CameraManager] GameAssembly.dll not loaded");
                    return 0;
                }

                // Calculate get_Instance method address
                ulong methodAddr = gameAssemblyBase + Offsets.EFTCameraManager.GetInstance_RVA;
                XMLogging.WriteLine($"[CameraManager] get_Instance at 0x{methodAddr:X} (GameAssembly+0x{Offsets.EFTCameraManager.GetInstance_RVA:X})");

                // Read method bytes
                byte[] methodBytes = Memory.ReadBuffer(methodAddr, 128, false);
                if (methodBytes == null || methodBytes.Length < 64)
                {
                    XMLogging.WriteLine("[CameraManager] Failed to read get_Instance method");
                    return 0;
                }

                // Pattern 1: lea rcx, [rip+offset] → class metadata
                for (int i = 0; i < methodBytes.Length - 7; i++)
                {
                    if (methodBytes[i] == 0x48 && methodBytes[i + 1] == 0x8D && methodBytes[i + 2] == 0x0D)
                    {
                        int disp32 = BitConverter.ToInt32(methodBytes, i + 3);
                        ulong classMetadataAddr = methodAddr + (ulong)i + 7 + (ulong)disp32;

                        ulong classPtr = Memory.ReadPtr(classMetadataAddr, false);
                        if (classPtr.IsValidVirtualAddress())
                        {
                            uint[] staticFieldsOffsets = { 0xB8, 0xC0, 0xC8, 0xD0, 0xA8, 0xB0 };
                            foreach (var offset in staticFieldsOffsets)
                            {
                                ulong staticFieldsPtr = Memory.ReadPtr(classPtr + offset, false);
                                if (staticFieldsPtr.IsValidVirtualAddress())
                                {
                                    ulong instancePtr = Memory.ReadPtr(staticFieldsPtr, false);
                                    if (instancePtr.IsValidVirtualAddress())
                                    {
                                        ulong testCamera = Memory.ReadPtr(instancePtr + Offsets.EFTCameraManager.Camera, false);
                                        if (testCamera.IsValidVirtualAddress())
                                        {
                                            XMLogging.WriteLine($"[CameraManager] OK Found Instance via pattern 1: 0x{instancePtr:X}");
                                            return instancePtr;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Pattern 2: mov rax, [rip+offset] → direct static field
                for (int i = 32; i < methodBytes.Length - 7; i++)
                {
                    if (methodBytes[i] == 0x48 && methodBytes[i + 1] == 0x8B && methodBytes[i + 2] == 0x05)
                    {
                        int disp32 = BitConverter.ToInt32(methodBytes, i + 3);
                        ulong staticFieldAddr = methodAddr + (ulong)i + 7 + (ulong)disp32;

                        ulong instancePtr = Memory.ReadPtr(staticFieldAddr, false);
                        if (instancePtr.IsValidVirtualAddress())
                        {
                            ulong testCamera = Memory.ReadPtr(instancePtr + Offsets.EFTCameraManager.Camera, false);
                            if (testCamera.IsValidVirtualAddress())
                            {
                                XMLogging.WriteLine($"[CameraManager] OK Found Instance via pattern 2 at +0x{i:X}");
                                XMLogging.WriteLine($"[CameraManager]   Instance: 0x{instancePtr:X}");
                                return instancePtr;
                            }
                        }
                    }
                }

                XMLogging.WriteLine("[CameraManager] FAILED No valid pattern found in get_Instance");
                XMLogging.WriteLine($"[CameraManager] Update GetInstance_RVA! Current: 0x{Offsets.EFTCameraManager.GetInstance_RVA:X}");
                return 0;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"[CameraManager] FAILED Error in FindCameraManagerInstance: {ex.Message}");
                return 0;
            }
        }

        private static void MemDMA_GameStopped(object sender, EventArgs e)
        {
            _eftCameraManagerInstance = default;
        }

        /// <summary>
        /// Checks if we are actually scoped using the optic's SightComponent zoom level.
        /// NOTE: no longer gates on a fragile OpticCameraActive flag – as long as the
        /// optic chain + zoom is valid, we consider ourselves scoped.
        /// </summary>
        private bool CheckIfScoped(LocalPlayer localPlayer)
        {
            try
            {
                if (localPlayer is null)
                    return false;

                // Require a valid optic camera pointer (from either path).
                if (!OpticCamera.IsValidVirtualAddress())
                    return false;

                var opticsPtr = Memory.ReadPtr(localPlayer.PWA + Offsets.ProceduralWeaponAnimation._optics);
                if (!opticsPtr.IsValidVirtualAddress())
                    return false;

                using var optics = MemList<MemPointer>.Get(opticsPtr);
                if (optics.Count <= 0)
                    return false;

                var pSightComponent = Memory.ReadPtr(optics[0] + Offsets.SightNBone.Mod);
                if (!pSightComponent.IsValidVirtualAddress())
                    return false;

                var sightComponent = Memory.ReadValue<SightComponent>(pSightComponent);

                // Prefer ScopeZoomValue if non-zero
                if (sightComponent.ScopeZoomValue != 0f)
                    return sightComponent.ScopeZoomValue > 1f;

                var zoom = sightComponent.GetZoomLevel();
                return zoom > 1f;
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"CheckIfScoped() ERROR: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Executed on each realtime loop; queues view matrix + FOV/Aspect scatter reads.
        /// </summary>
        public void OnRealtimeLoop(ScatterReadIndex index, /* Can Be Null */ LocalPlayer localPlayer)
        {
            IsADS = localPlayer?.CheckIfADS() ?? false;
            IsScoped = IsADS && CheckIfScoped(localPlayer);

            // Choose camera: scoped → optic, otherwise → FPS
            ulong camera = (IsADS && IsScoped && OpticCamera.IsValidVirtualAddress())
                ? OpticCamera
                : FPSCamera;

            if (!camera.IsValidVirtualAddress())
                return;

            ulong vmAddr = camera + UnityOffsets.Camera.ViewMatrix;

            // View matrix
            index.AddEntry<Matrix4x4>(0, vmAddr);

            index.Callbacks += x1 =>
            {
                ref Matrix4x4 vm = ref x1.GetRef<Matrix4x4>(0);
                if (!Unsafe.IsNullRef(ref vm))
                {
                    _viewMatrix.Update(ref vm);
                }
            };

            // Keep FOV / Aspect up to date from FPS camera regardless;
            // WorldToScreen only applies zoom when IsScoped.
            if (FPSCamera.IsValidVirtualAddress())
            {
                var fovAddr = FPSCamera + UnityOffsets.Camera.FOV;
                var aspectAddr = FPSCamera + UnityOffsets.Camera.AspectRatio;

                index.AddEntry<float>(1, fovAddr);
                index.AddEntry<float>(2, aspectAddr);

                index.Callbacks += x2 =>
                {
                    if (x2.TryGetResult<float>(1, out var fov))
                    {
                        if (fov > 1f && fov < 180f)
                            _fov = fov;
                    }

                    if (x2.TryGetResult<float>(2, out var aspect))
                    {
                        if (aspect > 0.1f && aspect < 5f)
                            _aspect = aspect;
                    }
                };
            }
        }

        #region SightComponent structures

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        private readonly ref struct SightComponent // EFT.InventoryLogic.SightComponent
        {
            [FieldOffset((int)Offsets.SightComponent._template)]
            private readonly ulong pSightInterface;

            [FieldOffset((int)Offsets.SightComponent.ScopesSelectedModes)]
            private readonly ulong pScopeSelectedModes;

            [FieldOffset((int)Offsets.SightComponent.SelectedScope)]
            private readonly int SelectedScope;

            [FieldOffset((int)Offsets.SightComponent.ScopeZoomValue)]
            public readonly float ScopeZoomValue;

            public readonly float GetZoomLevel()
            {
                using var zoomArray = SightInterface.Zooms;

                if (SelectedScope >= zoomArray.Count || SelectedScope is < 0 or > 10)
                    return -1.0f;

                using var selectedScopeModes = MemArray<int>.Get(pScopeSelectedModes, false);
                int selectedScopeMode = SelectedScope >= selectedScopeModes.Count ? 0 : selectedScopeModes[SelectedScope];
                ulong zoomAddr = zoomArray[SelectedScope] + MemArray<float>.ArrBaseOffset + (uint)selectedScopeMode * 0x4;

                float zoomLevel = Memory.ReadValue<float>(zoomAddr, false);

                if (zoomLevel.IsNormalOrZero() && zoomLevel is >= 0f and < 100f)
                    return zoomLevel;

                return -1.0f;
            }

            public readonly SightInterface SightInterface =>
                Memory.ReadValue<SightInterface>(pSightInterface);
        }

        [StructLayout(LayoutKind.Explicit, Pack = 1)]
        private readonly ref struct SightInterface // -.GInterfaceBB26
        {
            [FieldOffset((int)Offsets.SightInterface.Zooms)]
            private readonly ulong pZooms;

            public readonly MemArray<ulong> Zooms =>
                MemArray<ulong>.Get(pZooms);
        }

        #endregion
    }
}
