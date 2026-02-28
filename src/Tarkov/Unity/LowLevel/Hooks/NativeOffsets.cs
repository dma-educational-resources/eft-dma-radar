namespace eft_dma_shared.Common.Unity.LowLevel.Hooks
{
    internal static class NativeOffsets
    {
        // ─────────────────────────────────────────────
        // GameAssembly.dll (IL2CPP)
        // ─────────────────────────────────────────────

        // .data pointer → points to il2cpp_runtime_invoke
        // You already identified this
        public const ulong il2cpp_runtime_invoke_ptr = 0x59386B8;
        public const ulong il2cpp_delegate_invoke    = 0x0024AF0;   // sub_180024AF0
        public const ulong il2cpp_gc_alloc_fixed     = 0x02A1B60;  // ✔ from dump
        public const ulong Unity_PlayerLoop_Run = 0x005A1230; // UPDATE PER BUILD
        public const ulong UnityPlayer_PerFrameFn = 0x0E488B0;
        public const ulong GameObject_CUSTOM_Find      = 0x001006C0;
        public const ulong GameObject_CUSTOM_SetActive = 0x000FD8E0;
        public const ulong PlayerLoop_Execute = 0x0E488B0;
        // ─────────────────────────────────────────────
        // UnityPlayer.dll
        // ─────────────────────────────────────────────
public const ulong Time_get_time_rva = 0x432A560;
        public const ulong Behaviour_SetEnabled = 0x4499E0;
        public const ulong Material_CUSTOM_SetColorImpl_Injected = 0xB7CC0;
        public const ulong Shader_CUSTOM_PropertyToID = 0xAB260;

        public const ulong AssetBundle_CUSTOM_LoadFromMemory_Internal = 0x1CC810;
        public const ulong AssetBundle_CUSTOM_LoadAsset_Internal = 0x1CD440;
        public const ulong AssetBundle_CUSTOM_Unload = 0x1CE410;
    }
}
