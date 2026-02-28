using eft_dma_radar.Common.Misc;
using eft_dma_radar.Common.Unity.LowLevel.Types;
using eft_dma_shared.Common.Unity;
using System;
using System.Runtime.CompilerServices;

namespace eft_dma_shared.Common.Unity.LowLevel.Hooks
{
    public static class NativeMethods
    {
        public static ulong FindGameObject(ulong name)
        {
            ulong fn = NativeHook.UnityPlayerDll + NativeOffsets.GameObject_CUSTOM_Find;
            return NativeHook.Call(fn, name) ?? 0;
        }
        public static readonly object Lock = new();        
        public static ulong FindGameObjectS(string name)
        {
            lock (Lock)
            {
                var nameMonoStr = RemoteBytes.MonoString.Get(name);
                using RemoteBytes nameMonoStrMem = new((int)nameMonoStr.GetSizeU());
                nameMonoStrMem.WriteString(nameMonoStr);

                ulong result = FindGameObject((ulong)nameMonoStrMem);

                if (result == 0x0)
                    XMLogging.WriteLine($"Game object \"{name}\" could not be found!");
                
                return result;
            }
        }
        public static ulong GameObjectSetActive(ulong go, bool state)
        {
            ulong fn = NativeHook.UnityPlayerDll + NativeOffsets.GameObject_CUSTOM_SetActive;
            return NativeHook.Call(fn, go, Unsafe.As<bool, ulong>(ref state)) ?? 0;
        }

        public static ulong SetBehaviorState(ulong behavior, bool state)
        {
            ulong fn = NativeHook.UnityPlayerDll + NativeOffsets.Behaviour_SetEnabled;
            return NativeHook.Call(fn, behavior, Unsafe.As<bool, ulong>(ref state)) ?? 0;
        }
    }
}
