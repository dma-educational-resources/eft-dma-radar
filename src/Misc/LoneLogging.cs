namespace eft_dma_radar.Common.Misc
{
    /// <summary>Forwarding shim — use <see cref="Log"/> directly.</summary>
    [Obsolete("Use Log.WriteLine / Log.Write instead.")]
    public static class XMLogging
    {
        [Obsolete("Use Log.WriteLine instead.")]
        public static void WriteLine(object data) => Log.WriteLine(data);

        [Obsolete("Use Log.WriteBlock instead.")]
        public static void WriteBlock(List<string> lines) => Log.WriteBlock(lines);
    }
}
