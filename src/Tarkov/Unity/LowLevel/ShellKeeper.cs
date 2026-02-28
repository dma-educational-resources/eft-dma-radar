using Reloaded.Assembler;

namespace eft_dma_radar.Common.Unity.LowLevel
{
    public static class ShellKeeper
    {
        private static readonly Assembler _assembler = new();

        static ShellKeeper()
        {
            string[] returnTrue = new[]
            {
                "use64",
                "mov rax, 1",
                "ret"
            };
            PatchTrue = _assembler.Assemble(returnTrue);

            string[] returnFalse = new[]
            {
                "use64",
                "mov rax, 0",
                "ret"
            };
            PatchFalse = _assembler.Assemble(returnFalse);

            string[] returnZeroFloat = new[]
            {
                "use64",
                "xorps xmm0, xmm0",
                "ret"
            };
            PatchReturnZeroFloat = _assembler.Assemble(returnZeroFloat);
        }

        /// <summary>
        /// The same as "return false;"
        /// </summary>
        public static readonly byte[] PatchFalse;

        /// <summary>
        /// The same as "return true;"
        /// </summary>
        public static readonly byte[] PatchTrue;

        /// <summary>
        /// The same as "return 0f;"
        /// </summary>
        public static readonly byte[] PatchReturnZeroFloat;

        /// <summary>
        /// The same as "return;"
        /// </summary>
        public static readonly byte[] PatchReturn = new byte[] { 0xC3 };

        public static byte[] ReturnInt(int returnVal)
        {
            string[] returnInt = new[]
            {
                "use64",
                $"mov rax, {returnVal}",
                "ret"
            };

            return _assembler.Assemble(returnInt);
        }
    }
}
