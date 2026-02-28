using System;
using eft_dma_shared.Common.Unity;
using eft_dma_radar.Tarkov.Features;
using eft_dma_radar.Common.DMA.Features;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Tarkov.Unity.IL2CPP;

namespace eft_dma_radar.Tarkov.Features.MemoryWrites.Patches
{
    public sealed class HideRaidCode : MemWriteFeature<HideRaidCode>
    {
        //private bool _set;
//
        //private ulong _cachedPreloaderUi;
//
        //// Text components (NOT GameObjects)
        //private ulong _alphaVersionText;
        //private ulong _sessionIdText;
//
        //// Optional restore support
        //private string? _originalAlphaText;
        //private string? _originalSessionText;
//
        //public override bool Enabled
        //{
        //    get => MemWrites.Config.HideRaidCode;
        //    set => MemWrites.Config.HideRaidCode = value;
        //}
//
        //public void Set()
        //{
        //    try
        //    {
        //        // Resolve PreloaderUI once
        //        if (!_cachedPreloaderUi.IsValidVirtualAddress())
        //        {
        //            _cachedPreloaderUi = ResolvePreloaderUI();
        //            if (!_cachedPreloaderUi.IsValidVirtualAddress())
        //                return;
        //        }
//
        //        // Resolve Alpha Version TEXT component
        //        if (!_alphaVersionText.IsValidVirtualAddress())
        //        {
        //            _alphaVersionText = Memory.ReadValue<ulong>(
        //                _cachedPreloaderUi + Offsets.PreloaderUI._alphaVersionLabel);
//
        //            if (_alphaVersionText.IsValidVirtualAddress())
        //                _originalAlphaText = Memory.ReadUnityString(_alphaVersionText);
        //        }
//
        //        // Resolve Session ID TEXT component
        //        if (!_sessionIdText.IsValidVirtualAddress())
        //        {
        //            _sessionIdText = Memory.ReadValue<ulong>(
        //                _cachedPreloaderUi + Offsets.PreloaderUI._sessionIdText);
//
        //            if (_sessionIdText.IsValidVirtualAddress())
        //                _originalSessionText = Memory.ReadUnityString(_sessionIdText);
        //        }
//
        //        if (Enabled)
        //        {
        //            if (!_set)
        //            {
        //                if (_alphaVersionText.IsValidVirtualAddress())
        //                    Memory.WriteUnityString(_alphaVersionText, string.Empty);
//
        //                if (_sessionIdText.IsValidVirtualAddress())
        //                    Memory.WriteUnityString(_sessionIdText, string.Empty);
//
        //                XMLogging.WriteLine("HideRaidCode [ON]");
        //                _set = true;
        //            }
        //        }
        //        else if (_set)
        //        {
        //            if (_alphaVersionText.IsValidVirtualAddress() && _originalAlphaText != null)
        //                Memory.WriteUnityString(_alphaVersionText, _originalAlphaText);
//
        //            if (_sessionIdText.IsValidVirtualAddress() && _originalSessionText != null)
        //                Memory.WriteUnityString(_sessionIdText, _originalSessionText);
//
        //            XMLogging.WriteLine("HideRaidCode [OFF]");
        //            _set = false;
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        XMLogging.WriteLine($"ERROR configuring HideRaidCode: {ex}");
        //        ResetCache();
        //    }
        //}
//
        //// ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
        //// Helpers
        //// ©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤©¤
//
        //private static ulong ResolvePreloaderUI()
        //{
        //    ulong unityBase = Memory.UnityBase;
        //    ulong gomAddr = GameObjectManager.GetAddr(unityBase);
        //    var gom = GameObjectManager.Get(gomAddr);
//
        //    ulong app = gom.FindBehaviourByClassName("TarkovApplication");
        //    if (!app.IsValidVirtualAddress())
        //        return 0;
//
        //    ulong menuOp = Memory.ReadPtr(app + Offsets.TarkovApplication._menuOperation);
        //    if (!menuOp.IsValidVirtualAddress())
        //        return 0;
//
        //    return Memory.ReadPtr(menuOp + Offsets.MainMenuShowOperation._preloaderUI);
        //}
//
        //private void ResetCache()
        //{
        //    _set = false;
//
        //    _cachedPreloaderUi = 0;
        //    _alphaVersionText  = 0;
        //    _sessionIdText     = 0;
//
        //    _originalAlphaText   = null;
        //    _originalSessionText = null;
        //}
//
        //public override void OnRaidStart() => ResetCache();
        //public override void OnGameStop()  => ResetCache();
    }
}
