/*
 * XM EFT DMA Radar
 * Brought to you by XM (XM DMA)
 * 
 * MIT License
 */

using System;
using System.Diagnostics;
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
        private static ulong _eftCameraManagerClassPtr;
        private static ulong _allCamerasAddr;
        private static int _resolveAttemptCount;
        private static bool _staticInitDone;
        private static bool _debugDumpDone;

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
        /// Signature scans and offset resolution run once per game session.
        /// CameraManager.Instance is re-resolved each time (per-raid).
        /// </summary>
        public static void Initialize()
        {
            try
            {
                // One-time: sig scans + offset resolution (stable per game session)
                if (!_staticInitDone)
                {
                    _allCamerasAddr = ResolveAllCamerasAddr();
                    ResolveCameraOffsets();
                    _staticInitDone = true;
                }

                // Per-raid: re-resolve CameraManager.Instance
                var prev = _eftCameraManagerInstance;
                _eftCameraManagerInstance = FindCameraManagerInstance();

                if (!_eftCameraManagerInstance.IsValidVirtualAddress())
                {
                    if (_resolveAttemptCount == 0)
                    {
                        XMLogging.WriteLine("[CameraManager] WARNING CameraManager.Instance not found - will fall back to AllCameras.");
                        XMLogging.WriteLine("[CameraManager] Radar will still work (cameras are optional).");
                    }
                    return;
                }

                DebugDumpStateOnce();

                if (prev != _eftCameraManagerInstance)
                    XMLogging.WriteLine($"[CameraManager] OK Initialized CameraManager.Instance @ 0x{_eftCameraManagerInstance:X}");
            }
            catch (Exception ex)
            {
                if (_resolveAttemptCount == 0)
                {
                    XMLogging.WriteLine($"[CameraManager] FAILED Init: {ex.Message}");
                    XMLogging.WriteLine("[CameraManager] Radar will still work (cameras are optional).");
                }
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
            bool verbose = _resolveAttemptCount == 0;

            // 1) Primary: IL2CPP CameraManager singleton
            if (TryResolveViaCameraManagerInstance(out fpsCamera, out opticCamera))
            {
                XMLogging.WriteLine("[CameraManager] Using CameraManager.Instance cameras.");
                _resolveAttemptCount = 0; // Reset for next raid
                return true;
            }

            // 2) Backup: Unity AllCameras + name-based search
            if (TryResolveViaAllCamerasByName(out fpsCamera, out opticCamera))
            {
                XMLogging.WriteLine("[CameraManager] Using Unity AllCameras + name search fallback.");
                _resolveAttemptCount = 0; // Reset for next raid
                return true;
            }

            fpsCamera = 0;
            opticCamera = 0;
            if (verbose)
                XMLogging.WriteLine("[CameraManager] ERROR: Could not resolve cameras via any path.");
            _resolveAttemptCount++;
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
                    return false; // Already tried in Initialize()

                // FPS camera
                var fpsCameraRef = Memory.ReadPtr(_eftCameraManagerInstance + Offsets.EFTCameraManager.Camera, false);
                if (!fpsCameraRef.IsValidVirtualAddress())
                    return false;

                var name = ObjectClass.ReadName(fpsCameraRef, 32, false);
                if (!string.Equals(name, "Camera", StringComparison.Ordinal))
                    return false;

                fpsCamera = Memory.ReadPtr(fpsCameraRef + UnityOffsets.ObjectClass.MonoBehaviourOffset, false);
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

                opticCamera = Memory.ReadPtr(opticCameraRef + UnityOffsets.ObjectClass.MonoBehaviourOffset, false);
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
                if (!_allCamerasAddr.IsValidVirtualAddress())
                {
                    XMLogging.WriteLine("[CameraManager] AllCameras address not resolved.");
                    return false;
                }

                var allCamerasPtr = Memory.ReadPtr(_allCamerasAddr, false);
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

                bool verbose = _resolveAttemptCount == 0;
                if (verbose)
                    XMLogging.WriteLine($"[CameraManager] AllCameras: items=0x{itemsPtr:X}, count={count}");

                FindCamerasByName(itemsPtr, count, out fpsCamera, out opticCamera);

                if (!fpsCamera.IsValidVirtualAddress() || !ValidateCameraMatrix(fpsCamera))
                {
                    if (verbose)
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
        /// Candidate signatures for locating the AllCameras global in UnityPlayer.dll.
        /// All reference the global via mov/lea reg,[rip+rel32] — rel32 at offset 3, instruction ends at 7.
        /// Ordered by uniqueness (most distinctive first).
        /// </summary>
        private static readonly (string Sig, int RelOffset, int InstrLen, string Desc)[] AllCamerasSigs =
        [
            // IDA 0x6DE0A9: mov rax,[rip+rel32]; mov r14,imm; mov ecx,[rax+?]; test ecx,ecx; jz; mov [rsp+?],rbx
            ("48 8B 05 ? ? ? ? 49 C7 C6 ? ? ? ? 8B 48 ? 85 C9 0F 84 ? ? ? ? 48 89 9C 24", 3, 7, "AllCameras@6DE0A9: mov rax,[rip]; mov r14,imm; test ecx; jz; mov [rsp],rbx"),
            // IDA 0x790214: mov r8,[rip+rel32]; xor edx,edx; mov rcx,[r8+?]
            ("4C 8B 05 ? ? ? ? 33 D2 49 8B 48", 3, 7, "AllCameras@790214: mov r8,[rip]; xor edx; mov rcx,[r8]"),
            // IDA 0xC30645: mov rax,[rip+rel32]; mov r14,imm; mov ecx,[rax+?]; test ecx,ecx; jz; mov [rsp+?],rsi
            ("48 8B 05 ? ? ? ? 49 C7 C6 ? ? ? ? 8B 48 ? 85 C9 0F 84 ? ? ? ? 48 89 B4 24", 3, 7, "AllCameras@C30645: mov rax,[rip]; mov r14,imm; test ecx; jz; mov [rsp],rsi"),
            // IDA 0xD3A139: mov rbx,[rip+rel32]; mov rsi,[rbx+?]; mov rax,[rbx+?]; inc rsi; ...
            ("48 8B 1D ? ? ? ? 48 8B 73 ? 48 8B 43 ? 48 FF C6", 3, 7, "AllCameras@D3A139: mov rbx,[rip]; mov rsi,[rbx]; mov rax,[rbx]; inc rsi"),
        ];

        /// <summary>
        /// Resolves the AllCameras global address via signature scan on UnityPlayer.dll,
        /// falling back to the hardcoded <see cref="ModuleBase.AllCameras"/> offset.
        /// </summary>
        private static ulong ResolveAllCamerasAddr()
        {
            var unityBase = Memory.UnityBase;
            if (!unityBase.IsValidVirtualAddress())
            {
                XMLogging.WriteLine("[CameraManager] Unity base not loaded; AllCameras unavailable.");
                return 0;
            }

            DebugTestAllSignatures();

            // Try signature scan first
            foreach (var (sig, relOff, instrLen, desc) in AllCamerasSigs)
            {
                try
                {
                    var sigAddr = Memory.FindSignature(sig, "UnityPlayer.dll");
                    if (sigAddr == 0)
                        continue;

                    int disp32 = Memory.ReadValue<int>(sigAddr + (ulong)relOff, false);
                    ulong resolved = sigAddr + (ulong)instrLen + (ulong)(long)disp32;

                    if (!resolved.IsValidVirtualAddress())
                        continue;

                    var listPtr = Memory.ReadPtr(resolved, false);
                    if (listPtr.IsValidVirtualAddress())
                    {
                        var items = Memory.ReadPtr(listPtr, false);
                        int count = Memory.ReadValue<int>(listPtr + 0x8, false);
                        if (items.IsValidVirtualAddress() && count >= 0 && count < 1024)
                            return resolved;
                    }
                }
                catch
                {
                    // Try next signature
                }
            }

            // Fallback: hardcoded offset
            var fallbackAddr = unityBase + ModuleBase.AllCameras;
            if (fallbackAddr.IsValidVirtualAddress())
            {
                XMLogging.WriteLine("[CameraManager] AllCameras using hardcoded fallback");
                return fallbackAddr;
            }

            XMLogging.WriteLine("[CameraManager] AllCameras resolution FAILED");
            return 0;
        }

        /// <summary>
        /// DEBUG: Tests all signatures (AllCameras + Camera struct offsets) and stores results for
        /// deferred output inside <see cref="DebugDumpState"/>.
        /// Automatically runs in Debug builds; fully stripped in Release.
        /// </summary>
        [Conditional("DEBUG")]
        private static void DebugTestAllSignatures()
        {
            var unityBase = Memory.UnityBase;
            if (!unityBase.IsValidVirtualAddress())
            {
                XMLogging.WriteLine("[CameraManager] DEBUG: Unity base not loaded — skipping sig audit.");
                return;
            }

            const string tag = "[CameraManager]";
            var bodyLines = new List<string>
            {
                "AllCameras Signatures",
            };

            for (int idx = 0; idx < AllCamerasSigs.Length; idx++)
            {
                var (sig, relOff, instrLen, desc) = AllCamerasSigs[idx];
                try
                {
                    var sigAddr = Memory.FindSignature(sig, "UnityPlayer.dll");
                    if (sigAddr == 0)
                    {
                        bodyLines.Add($"[{idx}] MISS — {desc}");
                        continue;
                    }

                    int disp32 = Memory.ReadValue<int>(sigAddr + (ulong)relOff, false);
                    ulong resolved = sigAddr + (ulong)instrLen + (ulong)(long)disp32;
                    string status;

                    if (!resolved.IsValidVirtualAddress())
                    {
                        status = $"BAD ADDR 0x{resolved:X}";
                    }
                    else
                    {
                        var listPtr = Memory.ReadPtr(resolved, false);
                        if (!listPtr.IsValidVirtualAddress())
                        {
                            status = $"BAD LIST PTR 0x{listPtr:X}";
                        }
                        else
                        {
                            var items = Memory.ReadPtr(listPtr, false);
                            int count = Memory.ReadValue<int>(listPtr + 0x8, false);
                            bool valid = items.IsValidVirtualAddress() && count >= 0 && count < 1024;
                            status = valid
                                ? $"OK 0x{resolved:X} RVA=0x{resolved - unityBase:X} count={count}"
                                : $"INVALID items=0x{items:X} count={count}";
                        }
                    }

                    bodyLines.Add($"[{idx}] {status} — {desc} (UP+0x{sigAddr - unityBase:X})");
                }
                catch (Exception ex)
                {
                    bodyLines.Add($"[{idx}] ERROR {ex.Message} — {desc}");
                }
            }

            // Camera struct offset signatures
            DebugTestCameraOffsetSigs(bodyLines, "ViewMatrix", ViewMatrixSigs, UnityOffsets.Camera.ViewMatrix, unityBase);
            DebugTestCameraOffsetSigs(bodyLines, "FOV", FovSigs, UnityOffsets.Camera.FOV, unityBase);
            DebugTestCameraOffsetSigs(bodyLines, "AspectRatio", AspectRatioSigs, UnityOffsets.Camera.AspectRatio, unityBase);

            // Auto-size: W = inner width between │ and │ (or ┌ and ┐)
            const string title = "Signature Health Audit";
            int W = title.Length + 4; // "─ title ─" minimum
            foreach (var line in bodyLines)
                if (line.Length + 2 > W) // " content " padding
                    W = line.Length + 2;

            var lines = new List<string>(bodyLines.Count + 2);
            {
                int dashTotal = W - title.Length - 2; // 2 spaces around title
                int dashLeft = dashTotal / 2;
                int dashRight = dashTotal - dashLeft;
                lines.Add($"{tag} ┌{new string('─', dashLeft)} {title} {new string('─', dashRight)}┐");
            }
            foreach (var body in bodyLines)
            {
                if (body.StartsWith('['))
                {
                    lines.Add($"{tag} │ {body.PadRight(W - 1)}│");
                }
                else
                {
                    int pad = W - body.Length;
                    int left = pad / 2;
                    int right = pad - left;
                    lines.Add($"{tag} │{new string(' ', left)}{body}{new string(' ', right)}│");
                }
            }
            lines.Add($"{tag} └{new string('─', W)}┘");

#if DEBUG
            _sigAuditLines = lines;

            // Cache valid counts so DebugDumpState doesn't re-scan.
            _cachedAllCamerasValidSigs = CountValidFromAudit(bodyLines, AllCamerasSigs.Length);
            _cachedViewMatrixValidSigs = CountValidFromAudit(bodyLines, ViewMatrixSigs.Length, "ViewMatrix");
            _cachedFovValidSigs = CountValidFromAudit(bodyLines, FovSigs.Length, "FOV");
            _cachedAspectRatioValidSigs = CountValidFromAudit(bodyLines, AspectRatioSigs.Length, "AspectRatio");
#endif
        }

        /// <summary>
        /// Counts "OK" results from the already-collected audit lines, avoiding re-scanning.
        /// </summary>
        private static int CountValidFromAudit(List<string> bodyLines, int sigCount, string sectionHeader = null)
        {
            int count = 0;
            bool inSection = false;
            foreach (var line in bodyLines)
            {
                if (!inSection)
                {
                    // Find the target section header.
                    // null means AllCameras (first section, header is "AllCameras Signatures").
                    if (sectionHeader is null
                        ? line.StartsWith("AllCameras", StringComparison.Ordinal)
                        : line.StartsWith(sectionHeader, StringComparison.Ordinal))
                    {
                        inSection = true;
                    }
                    continue;
                }
                if (line.StartsWith('[') && line.Contains("] OK "))
                    count++;
                else if (!line.StartsWith('['))
                    break; // Hit next section header
            }
            return count;
        }

        /// <summary>
        /// DEBUG helper: tests a set of Camera struct offset signatures and appends results to the buffer.
        /// </summary>
        private static void DebugTestCameraOffsetSigs(List<string> bodyLines, string fieldName, CameraOffsetSig[] sigs, uint currentValue, ulong unityBase)
        {
            bodyLines.Add($"{fieldName} Signatures (current=0x{currentValue:X})");
            for (int idx = 0; idx < sigs.Length; idx++)
            {
                var entry = sigs[idx];
                try
                {
                    var sigAddr = Memory.FindSignature(entry.Sig, "UnityPlayer.dll");
                    if (sigAddr == 0)
                    {
                        bodyLines.Add($"[{idx}] MISS — {entry.Desc}");
                        continue;
                    }

                    string matchInfo = $"UP+0x{sigAddr - unityBase:X}";
                    uint offset;

                    if (entry.IsCallSite)
                    {
                        int callRel32 = Memory.ReadValue<int>(sigAddr + (ulong)entry.OffsetPos + 1, false);
                        ulong callTarget = sigAddr + 5 + (ulong)(long)callRel32;
                        matchInfo += $" → UP+0x{callTarget - unityBase:X}";

                        if (!callTarget.IsValidVirtualAddress())
                        {
                            bodyLines.Add($"[{idx}] BAD CALL TARGET — {entry.Desc} ({matchInfo})");
                            continue;
                        }

                        offset = entry.TargetBodyDispSize switch
                        {
                            1 => Memory.ReadValue<byte>(callTarget + (ulong)entry.TargetBodyDispOffset, false),
                            4 => Memory.ReadValue<uint>(callTarget + (ulong)entry.TargetBodyDispOffset, false),
                            _ => 0,
                        };
                    }
                    else
                    {
                        offset = entry.DispSize switch
                        {
                            1 => Memory.ReadValue<byte>(sigAddr + (ulong)entry.OffsetPos, false),
                            4 => Memory.ReadValue<uint>(sigAddr + (ulong)entry.OffsetPos, false),
                            _ => 0,
                        };
                    }

                    bool sane = offset > 0 && offset < 0x1000;
                    bool matches = offset == currentValue;
                    string status = (sane, matches) switch
                    {
                        (true, true) => $"OK 0x{offset:X} (matches current)",
                        (true, false) => $"CHANGED 0x{offset:X} (current=0x{currentValue:X})",
                        _ => $"BAD offset=0x{offset:X}",
                    };

                    bodyLines.Add($"[{idx}] {status} — {entry.Desc} ({matchInfo})");
                }
                catch (Exception ex)
                {
                    bodyLines.Add($"[{idx}] ERROR {ex.Message} — {entry.Desc}");
                }
            }
        }

        #if DEBUG
        /// <summary>
        /// Sig audit lines collected by <see cref="DebugTestAllSignatures"/> for
        /// deferred output inside <see cref="DebugDumpState"/>.
        /// </summary>
        private static List<string>? _sigAuditLines;

        /// <summary>
        /// Cached signature valid counts from <see cref="DebugTestAllSignatures"/>,
        /// used by <see cref="DebugDumpState"/> to avoid redundant sig scans.
        /// </summary>
        private static int _cachedAllCamerasValidSigs;
        private static int _cachedViewMatrixValidSigs;
        private static int _cachedFovValidSigs;
        private static int _cachedAspectRatioValidSigs;
#endif

        /// <summary>
        /// DEBUG: Dumps state once per game session.
        /// </summary>
        [Conditional("DEBUG")]
        private static void DebugDumpStateOnce()
        {
            if (_debugDumpDone)
                return;
            _debugDumpDone = true;
            DebugDumpState();
        }

        /// <summary>
        /// DEBUG: Dumps a comprehensive summary of all resolved addresses and offsets.
        /// Includes the deferred sig audit box from <see cref="DebugTestAllSignatures"/>.
        /// Automatically runs in Debug builds after successful initialization.
        /// </summary>
        [Conditional("DEBUG")]
        private static void DebugDumpState()
        {
            const int W = 54;
            const string tag = "[CameraManager]";
            var gaBase = Memory.GameAssemblyBase;
            var unityBase = Memory.UnityBase;

            string Row(string text) => $"║  {text.PadRight(W - 2)}║";
            string Header(string text)
            {
                int pad = W - text.Length;
                int left = pad / 2;
                int right = pad - left;
                return $"║{new string(' ', left)}{text}{new string(' ', right)}║";
            }
            string Sep(string label) => $"╠── {label} {"".PadRight(W - 4 - label.Length, '─')}╣";

            var lines = new List<string>();

#if DEBUG
            // Prepend the deferred sig audit box so both appear together.
            if (_sigAuditLines is not null)
            {
                lines.AddRange(_sigAuditLines);
                _sigAuditLines = null;
            }
#endif

            lines.Add($"{tag} ╔{new string('═', W)}╗");
            lines.Add($"{tag} {Header("Camera Manager — Debug State")}");
            lines.Add($"{tag} ╠{new string('═', W)}╣");
            lines.Add($"{tag} {Row($"GameAssembly Base:  {FormatPtr(gaBase)}")}");
            lines.Add($"{tag} {Row($"UnityPlayer Base:   {FormatPtr(unityBase)}")}");
            lines.Add($"{tag} {Sep("Resolved Pointers")}");
            lines.Add($"{tag} {Row($"ClassPtr:           {FormatPtr(_eftCameraManagerClassPtr)}")}");
            lines.Add($"{tag} {Row($"Instance:           {FormatPtr(_eftCameraManagerInstance)}")}");
            lines.Add($"{tag} {Row($"AllCameras Addr:    {FormatPtr(_allCamerasAddr)}")}");
            lines.Add($"{tag} {Sep("Camera Struct Offsets")}");
            lines.Add($"{tag} {Row($"ViewMatrix:         0x{UnityOffsets.Camera.ViewMatrix:X}")}");
            lines.Add($"{tag} {Row($"FOV:                0x{UnityOffsets.Camera.FOV:X}")}");
            lines.Add($"{tag} {Row($"AspectRatio:        0x{UnityOffsets.Camera.AspectRatio:X}")}");
            lines.Add($"{tag} {Sep("SDK Offsets (EFTCameraManager)")}");
            lines.Add($"{tag} {Row($"GetInstance_RVA:    0x{Offsets.EFTCameraManager.GetInstance_RVA:X}")}");
            lines.Add($"{tag} {Row($"Camera:             0x{Offsets.EFTCameraManager.Camera:X}")}");
            lines.Add($"{tag} {Row($"OpticCameraManager: 0x{Offsets.EFTCameraManager.OpticCameraManager:X}")}");
            lines.Add($"{tag} {Sep("Signature Health")}");
#if DEBUG
            lines.Add($"{tag} {Row($"AllCameras:   {_cachedAllCamerasValidSigs}/{AllCamerasSigs.Length} sigs valid")}");
            lines.Add($"{tag} {Row($"ViewMatrix:   {_cachedViewMatrixValidSigs}/{ViewMatrixSigs.Length} sigs valid")}");
            lines.Add($"{tag} {Row($"FOV:          {_cachedFovValidSigs}/{FovSigs.Length} sigs valid")}");
            lines.Add($"{tag} {Row($"AspectRatio:  {_cachedAspectRatioValidSigs}/{AspectRatioSigs.Length} sigs valid")}");
#endif
            lines.Add($"{tag} ╚{new string('═', W)}╝");

            XMLogging.WriteBlock(lines);
        }

        private static string FormatPtr(ulong ptr) =>
            ptr.IsValidVirtualAddress() ? $"0x{ptr:X}" : "(not resolved)";

        #region Camera Struct Offset Resolution

        /// <summary>
        /// Camera getter signature entry.
        /// Two resolution strategies:
        ///   Direct  — the sig matches the getter itself; read displacement at OffsetPos.
        ///   Indirect — the sig matches a call-site (E8 rel32); resolve the call target first,
        ///              then read the displacement from the target function body.
        /// </summary>
        private readonly record struct CameraOffsetSig(
            string Sig,
            int OffsetPos,
            int DispSize,
            bool IsCallSite,
            int TargetBodyDispOffset,
            int TargetBodyDispSize,
            string Desc);

        /// <summary>
        /// Signatures for Camera::GetWorldToCameraMatrix in UnityPlayer.dll.
        /// Returns a pointer to the ViewMatrix (lea rax,[rcx+disp32]).
        /// </summary>
        private static readonly CameraOffsetSig[] ViewMatrixSigs =
        [
            // IDA call-site at 0xC37468: call sub_1800A4690 → target is lea rax,[rcx+128h]; ret
            // Sig matches the call + post-call context; E8 rel32 at offset 0, target body: 48 8D 81 XX XX XX XX C3
            new("E8 ? ? ? ? 48 3B 58 ? 0F 83 ? ? ? ? ? ? ? 48 8D 0C 5D ? ? ? ? 48 03 CB ? ? ? ? E8 ? ? ? ? 4C 8B C7 49 FF C0 ? ? ? ? ? 75",
                0, 4, IsCallSite: true, TargetBodyDispOffset: 3, TargetBodyDispSize: 4,
                "ViewMatrix call-site@C37468: call GetWorldToCameraMatrix; cmp rbx,[rax+10h]"),
        ];

        private static readonly CameraOffsetSig[] FovSigs =
        [
            // IDA sub_1807D3670: cmp dword ptr [rcx+53Ch],2; jnz → movss xmm0,[rcx+928h]; ret / movss xmm0,[rcx+1A8h]; ret
            // Full function: 83 B9 [3C050000] 02 75 ? F3 0F 10 81 [28090000] C3 F3 0F 10 81 [A8010000] C3
            // We wildcard the cmp displacement, jnz offset, and alternate-path displacement; extract FOV disp at the final movss.
            new("83 B9 ? ? ? ? 02 75 ? F3 0F 10 81 ? ? ? ? C3 F3 0F 10 81 ? ? ? ? C3", 22, 4, IsCallSite: false, 0, 0,
                "GetFieldOfView@7D3670: cmp [rcx+?],2; jnz; movss ret; movss xmm0,[rcx+FOV]; ret"),
        ];

        private static readonly CameraOffsetSig[] AspectRatioSigs =
        [
            // IDA call-site at 0x2EBF21: call sub_18013EE70 → target is movss xmm0,[rcx+518h]; ret
            // Post-call context: mulss xmm8,[rip+?]; mulss xmm0,xmm6
            new("E8 ? ? ? ? F3 44 0F 59 05 ? ? ? ? F3 0F 59 C6",
                0, 4, IsCallSite: true, TargetBodyDispOffset: 4, TargetBodyDispSize: 4,
                "AspectRatio call-site@2EBF21: call get_aspect; mulss xmm8; mulss xmm0,xmm6"),
        ];

        /// <summary>
        /// Resolves Camera struct field offsets (ViewMatrix, FOV, AspectRatio) via signature scan.
        /// Falls back to hardcoded defaults in <see cref="UnityOffsets.Camera"/> if any scan fails.
        /// </summary>
        private static void ResolveCameraOffsets()
        {
            var unityBase = Memory.UnityBase;
            if (!unityBase.IsValidVirtualAddress())
                return;

            ApplyCameraOffset(ViewMatrixSigs, "ViewMatrix", unityBase,
                ref UnityOffsets.Camera.ViewMatrix);
            ApplyCameraOffset(FovSigs, "FOV", unityBase,
                ref UnityOffsets.Camera.FOV);
            ApplyCameraOffset(AspectRatioSigs, "AspectRatio", unityBase,
                ref UnityOffsets.Camera.AspectRatio);
        }

        /// <summary>
        /// Resolves a Camera struct field offset via sig scan and applies it.
        /// Logs only on change or failure — confirmed matches are silent.
        /// </summary>
        private static void ApplyCameraOffset(CameraOffsetSig[] sigs, string fieldName, ulong unityBase, ref uint target)
        {
            var resolved = TryResolveCameraOffset(sigs, fieldName, unityBase);
            if (resolved.HasValue && resolved.Value != target)
            {
                XMLogging.WriteLine($"[CameraManager] Camera.{fieldName} UPDATED: 0x{target:X} → 0x{resolved.Value:X}");
                target = resolved.Value;
            }
            else if (!resolved.HasValue)
            {
                XMLogging.WriteLine($"[CameraManager] Camera.{fieldName} sig scan FAILED — using hardcoded 0x{target:X}");
            }
        }

        /// <summary>
        /// Tries each signature to extract a Camera struct field offset from UnityPlayer.dll.
        /// Supports two strategies:
        ///   Direct  — displacement is read directly from the sig match.
        ///   Indirect (call-site) — resolves E8 rel32 call target, then reads displacement from the target function body.
        /// Returns the displacement value if found, or null if all signatures failed.
        /// </summary>
        private static uint? TryResolveCameraOffset(CameraOffsetSig[] sigs, string fieldName, ulong unityBase)
        {
            foreach (var entry in sigs)
            {
                try
                {
                    var sigAddr = Memory.FindSignature(entry.Sig, "UnityPlayer.dll");
                    if (sigAddr == 0)
                    {
                        continue;
                    }

                    uint offset;

                    if (entry.IsCallSite)
                    {
                        int callRel32 = Memory.ReadValue<int>(sigAddr + (ulong)entry.OffsetPos + 1, false);
                        ulong callTarget = sigAddr + 5 + (ulong)(long)callRel32;

                        if (!callTarget.IsValidVirtualAddress())
                            continue;

                        offset = entry.TargetBodyDispSize switch
                        {
                            1 => Memory.ReadValue<byte>(callTarget + (ulong)entry.TargetBodyDispOffset, false),
                            4 => Memory.ReadValue<uint>(callTarget + (ulong)entry.TargetBodyDispOffset, false),
                            _ => 0,
                        };
                    }
                    else
                    {
                        offset = entry.DispSize switch
                        {
                            1 => Memory.ReadValue<byte>(sigAddr + (ulong)entry.OffsetPos, false),
                            4 => Memory.ReadValue<uint>(sigAddr + (ulong)entry.OffsetPos, false),
                            _ => 0,
                        };
                    }

                    // Sanity: Camera struct offsets should be reasonable (< 0x1000)
                    if (offset > 0 && offset < 0x1000)
                    {
                        return offset;
                    }

                    // Offset out of range, try next sig
                }
                catch
                {
                    // Sig failed, try next
                }
            }

            return null;
        }

        #endregion

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

                ulong methodAddr = gameAssemblyBase + Offsets.EFTCameraManager.GetInstance_RVA;

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
                            // Use the known Il2CppClass::static_fields offset first, then probe nearby offsets as fallback
                            var knownOffset = Offsets.Il2CppClass.StaticFields;
                            ReadOnlySpan<uint> fallbackOffsets = [knownOffset - 0x10, knownOffset - 0x08, knownOffset + 0x08, knownOffset + 0x10, knownOffset + 0x18];

                            if (TryReadStaticInstance(classPtr, knownOffset, out var instance))
                            {
                                _eftCameraManagerClassPtr = classPtr;
                                return instance;
                            }

                            foreach (var offset in fallbackOffsets)
                            {
                                if (offset == knownOffset)
                                    continue;
                                if (TryReadStaticInstance(classPtr, offset, out instance))
                                {
                                    _eftCameraManagerClassPtr = classPtr;
                                    return instance;
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
                                return instancePtr;
                        }
                    }
                }

                if (_resolveAttemptCount == 0)
                {
                    XMLogging.WriteLine("[CameraManager] FAILED No valid pattern found in get_Instance");
                    XMLogging.WriteLine($"[CameraManager] Update GetInstance_RVA! Current: 0x{Offsets.EFTCameraManager.GetInstance_RVA:X}");
                }
                return 0;
            }
            catch (Exception ex)
            {
                if (_resolveAttemptCount == 0)
                    XMLogging.WriteLine($"[CameraManager] FAILED Error in FindCameraManagerInstance: {ex.Message}");
                return 0;
            }
        }

        /// <summary>
        /// Attempts to read the CameraManager singleton instance from an Il2CppClass pointer
        /// using the given static_fields offset. Validates the result by checking the Camera field.
        /// </summary>
        private static bool TryReadStaticInstance(ulong classPtr, uint staticFieldsOffset, out ulong instance)
        {
            instance = 0;
            try
            {
                var staticFieldsPtr = Memory.ReadPtr(classPtr + staticFieldsOffset, false);
                if (!staticFieldsPtr.IsValidVirtualAddress())
                    return false;

                var instancePtr = Memory.ReadPtr(staticFieldsPtr, false);
                if (!instancePtr.IsValidVirtualAddress())
                    return false;

                var testCamera = Memory.ReadPtr(instancePtr + Offsets.EFTCameraManager.Camera, false);
                if (!testCamera.IsValidVirtualAddress())
                    return false;

                instance = instancePtr;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void MemDMA_GameStopped(object sender, EventArgs e)
        {
            _eftCameraManagerInstance = default;
            _eftCameraManagerClassPtr = default;
            _allCamerasAddr = default;
            _resolveAttemptCount = 0;
            _staticInitDone = false;
            _debugDumpDone = false;
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
