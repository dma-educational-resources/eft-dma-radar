using eft_dma_radar.Common.DMA;
using eft_dma_radar.Common.Misc;
using eft_dma_radar.Misc;
using eft_dma_shared.Common.Unity.LowLevel.Hooks;
using System.Diagnostics;
using static eft_dma_radar.Common.Unity.MonoLib;

namespace eft_dma_radar.Common.DMA.Features
{
    public abstract class MemPatchFeature<T> : IFeature, IMemPatchFeature
        where T : IMemPatchFeature
    {
        /// <summary>
        /// Singleton Instance.
        /// </summary>
        public static T Instance { get; }

        private readonly Stopwatch _sw = Stopwatch.StartNew();

        static MemPatchFeature()
        {
            Instance = Activator.CreateInstance<T>();
            IFeature.Register(Instance);
        }

        public virtual bool Enabled { get; set; }

        public virtual bool CanRun
        {
            get
            {
                if (!Memory.Ready || !Enabled)
                    return false;
                if (!DelayElapsed)
                    return false;
                return true;
            }
        }

        public bool IsApplied { get; protected set; }

        protected virtual TimeSpan Delay => TimeSpan.FromSeconds(3);

        protected bool DelayElapsed => Delay == TimeSpan.Zero || _sw.Elapsed >= Delay;

        protected virtual Func<ulong> GetPFunc => throw new NotImplementedException();

        protected virtual int PFuncSize => throw new NotImplementedException();

        protected virtual byte[] Signature => throw new NotImplementedException();

        protected virtual byte[] Patch => throw new NotImplementedException();

        protected virtual string Mask { get; }

        /// <summary>
        /// Try apply this patch (does not throw).
        /// No-op if already applied.
        /// </summary>
        /// <returns>True if applied OK (or already applied), otherwise False.</returns>
        public virtual bool TryApply()
        {
            if (IsApplied)
                return true;
            try
            {
                var pFuncBase = GetPFunc();
                var method = new byte[PFuncSize];
                Memory.ReadBuffer(pFuncBase, method.AsSpan(), false);
                int sigIndex = method.FindSignatureOffset(Signature, Mask);
                if (sigIndex != -1)
                {
                    Memory.WriteBufferEnsure(pFuncBase + (uint)sigIndex, Patch.AsSpan());
                    XMLogging.WriteLine($"MemPatch {GetType().ToString()} Applied!");
                    NotificationsShared.Info($"MemPatch {GetType().ToString()} Applied!");
                    return IsApplied = true;
                }
                else if (method.FindSignatureOffset(Patch) != -1)
                {
                    XMLogging.WriteLine($"MemPatch {GetType().ToString()} Already Set!");
                    return IsApplied = true;
                }
            }
            catch (Exception ex)
            {
                XMLogging.WriteLine($"ERROR Applying Patch {GetType().ToString()}: {ex}");
            }
            return false;
        }

        public void PatchMethod(string className, string methodName, byte[] signature, byte[] patch, int offset = 0, string assemblyName = "Assembly-CSharp", bool compileClass = false)
        {
            static string GetName(string className)
            {
                return $"({className})";
            }            
            ulong methodAddr = 0x0;
            if (methodAddr == 0x0)
            {
                XMLogging.WriteLine($"Failed to find method {methodName} in {className}");
                return;
            }
            ulong mClass1;
            var mClass = MonoClass.Find(assemblyName, className, out mClass1);
            byte[] methodBytes = new byte[signature.Length + 0x10];
            Memory.ReadBufferEnsure(methodAddr, methodBytes.AsSpan());
            int sigIndex = methodBytes.FindSignatureOffset(signature);
            if (sigIndex != -1)
            {
                Memory.WriteBufferEnsure(methodAddr + (uint)sigIndex + (uint)offset, patch.AsSpan());
                XMLogging.WriteLine($"Patched {className}.{methodName}!");
            }
            if (compileClass)
            {
                ulong compiledClass = 0x0;
                if (compiledClass == 0x0)
                    throw new Exception($"Unable to compile class {GetName(className)}!");
            }          
        }

        public static void PatchMethodE(in string className, in string methodName, in SignatureInfo sigInfo, in int subclass = -1, in string assemblyName = "Assembly-CSharp", in bool compileClass = false, in bool compileMethod = true)
        {
            static string GetName(string className) => $"({className})";
            static string FormatEx(string exception) => $"[PATCH METHOD]: {exception}";

            // Get the class and its memory address
            var mClass = MonoClass.Find(assemblyName, className, out ulong classAddress, subclass);

            if (classAddress == 0x0)
                throw new Exception(FormatEx($"Unable to find class {GetName(className)}!"));

            if (compileClass)
            {
                ulong compiledClass = 0x0;
                if (compiledClass == 0x0)
                    throw new Exception(FormatEx($"Unable to compile class {GetName(className)}!"));
            }

            var mMethod = mClass.FindMethod(methodName);
            if (mMethod == 0x0)
                throw new Exception(FormatEx($"Method '{methodName}' not found in {GetName(className)}!"));

            PatchMethodEv(mMethod, className, methodName, sigInfo, compileMethod);
        }

        public static void PatchMethodEv(ulong mMethod, string className, string methodName, SignatureInfo sigInfo, bool compile = true)
        {
            static string GetName(string className, string methodName) => $"({className} -> {methodName})";
            static string FormatEx(string exception) => $"[PATCH METHOD]: {exception}";

            if (mMethod == 0x0)
                throw new Exception(FormatEx($"Unable to find method {GetName(className, methodName)}!"));

            ulong methodAddr = compile ? 0x0 : mMethod;

            if (methodAddr == 0x0)
                throw new Exception(FormatEx($"Unable to compile method {GetName(className, methodName)}!"));

            if (sigInfo.Signature == null)
            {
                try
                {
                    Memory.WriteBufferEnsure<byte>(methodAddr, sigInfo.Patch);
                    XMLogging.WriteLine(FormatEx($"Successfully patched {GetName(className, methodName)}!"));
                }
                catch
                {
                    throw new Exception(FormatEx($"Unable to patch method bytes for {GetName(className, methodName)}!"));
                }
                return;
            }

            // This is a complex patch
            byte[] methodBytes = MemDMABase.ReadBufferEnsureE(methodAddr, sigInfo.ReadSize);
            if (methodBytes == null)
                throw new Exception(FormatEx($"Unable to read method bytes for {GetName(className, methodName)}!"));

            // Determine the correct signature order
            byte[] sig1, sig2;
            string mask1, mask2;
            bool sig1AsPatch;

            if (sigInfo.PatchOffset < 0x0)
            {
                sig1 = sigInfo.Patch;
                mask1 = sigInfo.PatchMask;
                sig2 = sigInfo.Signature;
                mask2 = sigInfo.SignatureMask;
                sig1AsPatch = true;
            }
            else
            {
                sig1 = sigInfo.Signature;
                mask1 = sigInfo.SignatureMask;
                sig2 = sigInfo.Patch;
                mask2 = sigInfo.PatchMask;
                sig1AsPatch = false;
            }

            uint offset = methodBytes.FindSignature(sig1, mask1);
            if (offset == uint.MaxValue)
            {
                if (methodBytes.FindSignature(sig2, mask2) == uint.MaxValue)
                    throw new Exception(FormatEx($"Unable to find patch signature for {GetName(className, methodName)}!"));
                else
                {
                    XMLogging.WriteLine(FormatEx($"{GetName(className, methodName)} has already been patched!"));
                    return;
                }
            }
            else if (sig1AsPatch)
            {
                XMLogging.WriteLine(FormatEx($"{GetName(className, methodName)} has already been patched!"));
                return;
            }

            ulong finalAddr = (ulong)((long)(methodAddr + offset) + sigInfo.PatchOffset);
            byte[] usedPatch = sigInfo.RealPatch ?? sigInfo.Patch;

            try
            {
                Memory.WriteBufferEnsure<byte>(finalAddr, usedPatch);
                XMLogging.WriteLine(FormatEx($"Successfully patched {GetName(className, methodName)}!"));
            }
            catch
            {
                throw new Exception(FormatEx($"Unable to patch method bytes for {GetName(className, methodName)}!"));
            }
        }

        public readonly struct SignatureInfo(byte[] signature, byte[] patch, int readSize = 0, string signatureMask = null, string patchMask = null, int patchOffset = 0x0, byte[] realPatch = null)
        {
            public readonly byte[] Signature = signature;
            public readonly string SignatureMask = signatureMask;

            public readonly byte[] Patch = patch;
            public readonly byte[] RealPatch = realPatch;
            public readonly string PatchMask = patchMask;

            public readonly int ReadSize = readSize;
            public readonly int PatchOffset = patchOffset;
        }

        public void OnApply()
        {
            if (Delay != TimeSpan.Zero)
            {
                _sw.Restart();
            }
        }

        public virtual void OnGameStart()
        {
        }

        public virtual void OnRaidStart()
        {
        }

        public virtual void OnRaidEnd()
        {
        }

        public virtual void OnGameStop()
        {
        }
    }
}