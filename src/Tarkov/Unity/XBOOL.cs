namespace eft_dma_radar.Common.Unity
{
    public readonly struct XBOOL
    {
        public static bool Get(byte x) => new XBOOL(x);
        public static byte Get(bool x) => new XBOOL(x).Value;

        public static implicit operator bool(XBOOL x) => x.Value != 0;

        public readonly byte Value;

        public XBOOL(byte value)
        {
            Value = value;
        }

        public XBOOL(bool value)
        {
            if (value)
                Value = 1;
            else
                Value = 0;
        }
    }
}
