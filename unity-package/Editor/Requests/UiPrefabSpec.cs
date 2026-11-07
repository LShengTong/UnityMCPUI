using System;

namespace UnityMcp.Editor
{
    [Serializable] public sealed class UiPrefabSpec
    {
        public string name;
        public float reference_width, reference_height;
        public UiPrefabNode[] children;
    }
}
