using System.ComponentModel;

namespace eft_dma_radar.Common.Unity
{
    /// <summary>
    /// Bones Index for Player Transforms.
    /// </summary>
    public enum Bones : uint
    {
        [Description("HumanBase")]
        HumanBase = 0,
        [Description("Stomach")]
        HumanPelvis = 14,
        [Description("HumanLThigh1")]
        HumanLThigh1 = 15,
        [Description("HumanLThigh2")]
        HumanLThigh2 = 16,
        [Description("HumanLCalf")]
        HumanLCalf = 17,
        [Description("HumanLFoot")]
        HumanLFoot = 18,
        [Description("HumanLToe")]
        HumanLToe = 19,
        [Description("HumanRThigh1")]
        HumanRThigh1 = 20,
        [Description("HumanRThigh2")]
        HumanRThigh2 = 21,
        [Description("HumanRCalf")]
        HumanRCalf = 22,
        [Description("HumanRFoot")]
        HumanRFoot = 23,
        [Description("HumanRToe")]
        HumanRToe = 24,
        [Description("HumanSpine1")]
        HumanSpine1 = 29,
        [Description("HumanSpine2")]
        HumanSpine2 = 36,
        [Description("Thorax")]
        HumanSpine3 = 37,
        [Description("HumanLCollarbone")]
        HumanLCollarbone = 89,
        [Description("HumanLUpperarm")]
        HumanLUpperarm = 90,
        [Description("HumanLForearm1")]
        HumanLForearm1 = 91,
        [Description("HumanLForearm2")]
        HumanLForearm2 = 92,
        [Description("HumanLForearm3")]
        HumanLForearm3 = 93,
        [Description("HumanLPalm")]
        HumanLPalm = 94,
        [Description("HumanRCollarbone")]
        HumanRCollarbone = 110,
        [Description("HumanRUpperarm")]
        HumanRUpperarm = 111,
        [Description("HumanRForearm1")]
        HumanRForearm1 = 112,
        [Description("HumanRForearm2")]
        HumanRForearm2 = 113,
        [Description("HumanRForearm3")]
        HumanRForearm3 = 114,
        [Description("HumanRPalm")]
        HumanRPalm = 115,
        [Description("Neck")]
        HumanNeck = 132,
        [Description("Head")]
        HumanHead = 133,
        /// <summary>
        /// Custom Define - Do not read at this index.
        /// </summary>
        [Description("Legs")]
        Legs = 9999
    };
}
