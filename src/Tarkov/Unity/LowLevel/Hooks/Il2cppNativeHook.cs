using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;

namespace eft_dma_shared.Common.Unity.LowLevel.Hooks
{
    public static class NativeHook
    {
        private const int STOLEN_BYTES = 16;

        private static readonly object SyncRoot = new();

        private static ulong _unityBase;
        private static ulong _hookTarget;
        private static ulong _codeCave;
        private static byte[] _originalBytes = Array.Empty<byte>();
        public static ulong UnityPlayerDll => _unityBase;
        public static bool Initialized => _codeCave != 0;

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct CallData
        {
            public ulong Function;
            public ulong RcX;
            public ulong RdX;
            public ulong R8;
            public ulong R9;
            public ulong Result;
            public byte Executed;
        }

        private static ulong CallDataAddr => _codeCave;

        private static ulong TrampolineAddr =>
            MemDMABase.AlignAddress(_codeCave + (uint)Marshal.SizeOf<CallData>()) + 0x10;

        // ============================================================
        // INIT
        // ============================================================

        public static bool Initialize()
        {
            lock (SyncRoot)
            {
                if (Initialized)
                    return true;

                ulong gameAsm = Memory.GameAssemblyBase;
                gameAsm.ThrowIfInvalidVirtualAddress();

                // ? CORRECT per-frame executor
                _hookTarget = Memory.GameAssemblyBase + NativeOffsets.PlayerLoop_Execute;
                _hookTarget.ThrowIfInvalidVirtualAddress();

                _codeCave = GetCodeCave();
                _codeCave.ThrowIfInvalidVirtualAddress();

                _originalBytes = new byte[STOLEN_BYTES];
                Memory.ReadBufferEnsure(_hookTarget, _originalBytes);

                WriteTrampoline();
                PatchAbsoluteJump(_hookTarget, TrampolineAddr);

                XMLogging.WriteLine(
                    $"[NativeHook] Hooked PlayerLoop.Run @ 0x{_hookTarget:X}, cave=0x{_codeCave:X}");

                return true;
            }
        }

        // ============================================================
        // PUBLIC CALL
        // ============================================================

        public static ulong? Call(
            ulong function,
            ulong rcx = 0,
            ulong rdx = 0,
            ulong r8  = 0,
            ulong r9  = 0)
        {
            lock (SyncRoot)
            {
                if (!Initialized)
                    return null;

                CallData data = new()
                {
                    Function = function,
                    RcX = rcx,
                    RdX = rdx,
                    R8  = r8,
                    R9  = r9,
                    Executed = 0
                };

                Memory.WriteValueEnsure(CallDataAddr, ref data);

                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 2000)
                {
                    Thread.Sleep(1);
                    Memory.ReadValueEnsure(CallDataAddr, out data);
                    if (data.Executed != 0)
                        return data.Result;
                }

                XMLogging.WriteLine("[NativeHook] Call timeout");
                return null;
            }
        }

        // ============================================================
        // TRAMPOLINE (CORRECT ABI)
        // ============================================================

        private static void WriteTrampoline()
        {
            ulong cursor = TrampolineAddr;

            // Save non-volatiles
            byte[] prologue =
            {
                0x53,                   // push rbx
                0x55,                   // push rbp
                0x57,                   // push rdi
                0x56,                   // push rsi
                0x41,0x54,              // push r12
                0x41,0x55,              // push r13
                0x41,0x56,              // push r14
                0x41,0x57,              // push r15
                0x48,0x83,0xEC,0x28     // sub rsp,28h  ?
            };
            Memory.WriteBufferEnsure(cursor, prologue);
            cursor += (ulong)prologue.Length;

            // RBX = &CallData
            byte[] exec =
            {
                0x48,0xBB, 0,0,0,0,0,0,0,0,   // mov rbx, CallDataAddr
                0x48,0x8B,0x03,              // mov rax,[rbx] (Function)
                0x48,0x85,0xC0,              // test rax,rax
                0x74,0x22,                   // jz skip

                0x48,0x8B,0x4B,0x08,         // rcx
                0x48,0x8B,0x53,0x10,         // rdx
                0x4C,0x8B,0x43,0x18,         // r8
                0x4C,0x8B,0x4B,0x20,         // r9
                0xFF,0xD0,                   // call rax

                0xF3,0x0F,0x11,0x43,0x28,    // movss [rbx+28h], xmm0 ?
                0xC6,0x43,0x30,0x01,         // Executed = 1
                0x48,0xC7,0x03,0,0,0,0       // Function = 0
            };

            BinaryPrimitives.WriteUInt64LittleEndian(exec.AsSpan(2), CallDataAddr);
            Memory.WriteBufferEnsure(cursor, exec);
            cursor += (ulong)exec.Length;

            // Restore stolen bytes
            Memory.WriteBufferEnsure(cursor, _originalBytes);
            cursor += (ulong)_originalBytes.Length;

            PatchAbsoluteJump(cursor, _hookTarget + STOLEN_BYTES);
        }

        // ============================================================
        // HELPERS
        // ============================================================

        private static void PatchAbsoluteJump(ulong at, ulong to)
        {
            Span<byte> jmp = stackalloc byte[14];
            jmp[0] = 0x49;
            jmp[1] = 0xBB;
            BinaryPrimitives.WriteUInt64LittleEndian(jmp[2..10], to);
            jmp[10] = 0x41;
            jmp[11] = 0xFF;
            jmp[12] = 0xE3;
            jmp[13] = 0x90;
            Memory.WriteBufferEnsure(at, jmp);
        }

        private static ulong GetCodeCave()
        {
            const ulong ScanSize = 0x2000000;
            ulong baseAddr = Memory.UnityBase;

            for (ulong addr = baseAddr; addr < baseAddr + ScanSize; addr += 0x10)
            {
                try
                {
                    if (Memory.ReadValue<byte>(addr) == 0xCC &&
                        Memory.ReadValue<byte>(addr + 1) == 0xCC &&
                        Memory.ReadValue<byte>(addr + 2) == 0xCC &&
                        Memory.ReadValue<byte>(addr + 3) == 0xCC)
                        return addr;
                }
                catch { }
            }
            return 0;
        }
    }
}
