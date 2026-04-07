namespace eft_dma_radar.Silk.Tarkov.Unity
{
    /// <summary>
    /// Pure Unity engine layout constants. These are internal C++ struct layouts
    /// that do not change between game patches — they are part of the Unity runtime itself.
    /// </summary>
    internal static class UnitySDK
    {
        /// <summary>
        /// Il2Cpp generic List&lt;T&gt; / System.Collections.Generic.List layout.
        /// _items (array pointer) at +0x10, first element at array + 0x20.
        /// </summary>
        public static class UnityList
        {
            /// <summary>Offset from List base to _items (the backing array pointer).</summary>
            public const uint ArrOffset = 0x10;

            /// <summary>Offset from the array base to the first element (element[0]).</summary>
            public const uint ArrStartOffset = 0x20;
        }

        /// <summary>
        /// TransformAccess embedded in TransformInternal (native Unity object).
        /// These offsets are read directly from the TransformInternal pointer.
        /// </summary>
        public static class TransformAccess
        {
            /// <summary>TransformInternal + 0x70 → pointer to TransformHierarchy.</summary>
            public const uint HierarchyOffset = 0x70;

            /// <summary>TransformInternal + 0x78 → int index into the hierarchy's vertices/indices arrays.</summary>
            public const uint IndexOffset = 0x78;
        }

        /// <summary>
        /// TransformHierarchy — native Unity struct pointed to by TransformAccess.
        /// </summary>
        public static class TransformHierarchy
        {
            /// <summary>TransformHierarchy + 0x40 → pointer to indices array (int[]).</summary>
            public const uint IndicesOffset = 0x40;

            /// <summary>TransformHierarchy + 0x68 → pointer to vertices array (TrsX[]).</summary>
            public const uint VerticesOffset = 0x68;
        }
    }
}
