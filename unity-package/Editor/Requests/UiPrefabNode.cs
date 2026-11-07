using System;

namespace UnityMcp.Editor
{
    [Serializable] public sealed class UiPrefabNode
    {
        public string name, kind, anchor, color, text, text_color, font_path, sprite_path, layout;
        public float width, height, x, y, font_size, spacing;
        public int padding;
        public UiOffset offset_min, offset_max;
        public bool has_offset_min, has_offset_max;
        public UiPrefabNode[] children;
    }
}
