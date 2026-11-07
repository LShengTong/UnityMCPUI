using System;
using UnityEngine;
namespace UnityMcp.Editor
{
    /// <summary>Shared anchor presets for creation and existing-node edits.</summary>
    internal static class UiRectTransformUtility
    {
        public static void GetPreset(string name, out Vector2 min, out Vector2 max, out Vector2 pivot)
        {
            switch (name)
            {
                case "top_left": min = max = pivot = new Vector2(0, 1); break;
                case "top_center": min = max = pivot = new Vector2(.5f, 1); break;
                case "top_right": min = max = pivot = Vector2.one; break;
                case "center_left": min = max = pivot = new Vector2(0, .5f); break;
                case "center": min = max = pivot = new Vector2(.5f, .5f); break;
                case "center_right": min = max = pivot = new Vector2(1, .5f); break;
                case "bottom_left": min = max = pivot = Vector2.zero; break;
                case "bottom_center": min = max = pivot = new Vector2(.5f, 0); break;
                case "bottom_right": min = max = pivot = new Vector2(1, 0); break;
                case "stretch_horizontal_top": min = new Vector2(0, 1); max = Vector2.one; pivot = new Vector2(.5f, 1); break;
                case "stretch_horizontal_center": min = new Vector2(0, .5f); max = new Vector2(1, .5f); pivot = new Vector2(.5f, .5f); break;
                case "stretch_horizontal_bottom": min = Vector2.zero; max = new Vector2(1, 0); pivot = new Vector2(.5f, 0); break;
                case "stretch_vertical_left": min = Vector2.zero; max = new Vector2(0, 1); pivot = new Vector2(0, .5f); break;
                case "stretch_vertical_center": min = new Vector2(.5f, 0); max = new Vector2(.5f, 1); pivot = new Vector2(.5f, .5f); break;
                case "stretch_vertical_right": min = new Vector2(1, 0); max = Vector2.one; pivot = new Vector2(1, .5f); break;
                case "stretch": min = Vector2.zero; max = Vector2.one; pivot = new Vector2(.5f, .5f); break;
                default: throw new Exception("Unsupported anchor preset: " + name);
            }
        }
        public static void SetPreset(RectTransform rect, string name)
        {
            GetPreset(name, out var min, out var max, out var pivot);
            var size = rect.sizeDelta;
            var position = rect.anchoredPosition3D;
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition3D = position;
        }
        public static void ValidateOffset(UiOffset value)
        {
            if (value == null) throw new Exception("Explicit offset requires x/y values");
            if (float.IsNaN(value.x) || float.IsInfinity(value.x) || float.IsNaN(value.y) || float.IsInfinity(value.y)
                || Mathf.Abs(value.x) > 16384 || Mathf.Abs(value.y) > 16384)
                throw new Exception("Offset requires finite x/y values within +/-16384");
        }
        public static void Configure(RectTransform rect, UiPrefabNode node)
        {
            SetPreset(rect, node.anchor);
            rect.sizeDelta = new Vector2(node.width, node.height);
            rect.anchoredPosition = new Vector2(node.x, node.y);
            var min = rect.offsetMin;
            var max = rect.offsetMax;
            // Stretched axes use symmetric insets; fixed axes keep width/height and position.
            if (rect.anchorMin.x != rect.anchorMax.x) { min.x = node.x; max.x = -node.x; }
            if (rect.anchorMin.y != rect.anchorMax.y) { min.y = node.y; max.y = -node.y; }
            rect.offsetMin = min;
            rect.offsetMax = max;
            // Explicit edge offsets override the preset's calculated geometry.
            if (node.has_offset_min) rect.offsetMin = new Vector2(node.offset_min.x, node.offset_min.y);
            if (node.has_offset_max) rect.offsetMax = new Vector2(node.offset_max.x, node.offset_max.y);
        }
    }
}
