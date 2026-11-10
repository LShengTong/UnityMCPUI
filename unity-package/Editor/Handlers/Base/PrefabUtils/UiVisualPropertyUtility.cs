using System;
using System.Globalization;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class UiVisualPropertyUtility
    {
        static T Need<T>(GameObject node) where T : Component => node.GetComponent<T>()
            ?? throw new Exception(node.name + " requires " + typeof(T).Name);

        static T Choice<T>(string text) where T : struct
        {
            // Reject numeric enum values and combinations; protocol names are case sensitive.
            if (Array.IndexOf(Enum.GetNames(typeof(T)), text) < 0)
                throw new Exception("Invalid " + typeof(T).Name + ": " + text);
            return (T)Enum.Parse(typeof(T), text);
        }
        static float Number(string text, float min, float max)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                || float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                throw new Exception("Expected a finite number in " + min + ".." + max);
            return value;
        }
        static int Integer(string text, int min, int max)
        {
            if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
                throw new Exception("Expected an integer in " + min + ".." + max);
            return value;
        }
        static bool Boolean(string text) => text == "true" ? true : text == "false" ? false
            : throw new Exception("Expected true or false");
        static void TextAlignment(TMPro.TMP_Text text, string value)
        {
            if (Array.IndexOf(new[] { "TopLeft", "Top", "TopRight", "Left", "Center", "Right",
                "BottomLeft", "Bottom", "BottomRight", "Justified", "Flush" }, value) < 0)
                throw new Exception("Unsupported text_alignment");
            text.alignment = Choice<TMPro.TextAlignmentOptions>(value);
        }
        static void Overflow(TMPro.TMP_Text text, string value)
        {
            if (Array.IndexOf(new[] { "Overflow", "Ellipsis", "Masking", "Truncate", "Page" }, value) < 0)
                throw new Exception("Unsupported text_overflow");
            text.overflowMode = Choice<TMPro.TextOverflowModes>(value);
        }
        static int MaxOrigin(UnityEngine.UI.Image.FillMethod method) =>
            method == UnityEngine.UI.Image.FillMethod.Horizontal || method == UnityEngine.UI.Image.FillMethod.Vertical ? 1 : 3;

        public static bool TryApply(GameObject node, string property, string value)
        {
            switch (property)
            {
                case "text_alignment": TextAlignment(Need<TMPro.TMP_Text>(node), value); break;
                case "word_wrap": Need<TMPro.TMP_Text>(node).textWrappingMode = Boolean(value)
                    ? TMPro.TextWrappingModes.Normal : TMPro.TextWrappingModes.NoWrap; break;
                case "auto_size": Need<TMPro.TMP_Text>(node).enableAutoSizing = Boolean(value); break;
                case "font_size_min":
                    var minText = Need<TMPro.TMP_Text>(node);
                    minText.fontSizeMin = Number(value, 1, Mathf.Min(512, minText.fontSizeMax)); break;
                case "font_size_max":
                    var maxText = Need<TMPro.TMP_Text>(node);
                    maxText.fontSizeMax = Number(value, Mathf.Max(1, maxText.fontSizeMin), 512); break;
                case "text_overflow": Overflow(Need<TMPro.TMP_Text>(node), value); break;
                case "line_spacing": Need<TMPro.TMP_Text>(node).lineSpacing = Number(value, -512, 512); break;
                case "preserve_aspect": Need<UnityEngine.UI.Image>(node).preserveAspect = Boolean(value); break;
                case "fill_method":
                    var image = Need<UnityEngine.UI.Image>(node);
                    image.fillMethod = Choice<UnityEngine.UI.Image.FillMethod>(value);
                    if (image.fillOrigin > MaxOrigin(image.fillMethod)) image.fillOrigin = 0;
                    break;
                case "fill_origin":
                    var originImage = Need<UnityEngine.UI.Image>(node);
                    originImage.fillOrigin = Integer(value, 0, MaxOrigin(originImage.fillMethod)); break;
                case "fill_amount": Need<UnityEngine.UI.Image>(node).fillAmount = Number(value, 0, 1); break;
                case "fill_clockwise": Need<UnityEngine.UI.Image>(node).fillClockwise = Boolean(value); break;
                case "raycast_target": Need<UnityEngine.UI.Graphic>(node).raycastTarget = Boolean(value); break;
                case "child_alignment": Need<UnityEngine.UI.LayoutGroup>(node).childAlignment = Choice<TextAnchor>(value); break;
                case "child_control_width": Need<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>(node).childControlWidth = Boolean(value); break;
                case "child_control_height": Need<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>(node).childControlHeight = Boolean(value); break;
                case "child_force_expand_width": Need<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>(node).childForceExpandWidth = Boolean(value); break;
                case "child_force_expand_height": Need<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>(node).childForceExpandHeight = Boolean(value); break;
                case "child_scale_width": Need<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>(node).childScaleWidth = Boolean(value); break;
                case "child_scale_height": Need<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>(node).childScaleHeight = Boolean(value); break;
                case "grid_constraint": Need<UnityEngine.UI.GridLayoutGroup>(node).constraint = Choice<UnityEngine.UI.GridLayoutGroup.Constraint>(value); break;
                case "grid_constraint_count": Need<UnityEngine.UI.GridLayoutGroup>(node).constraintCount = Integer(value, 1, 2000); break;
                case "grid_start_corner": Need<UnityEngine.UI.GridLayoutGroup>(node).startCorner = Choice<UnityEngine.UI.GridLayoutGroup.Corner>(value); break;
                case "grid_start_axis": Need<UnityEngine.UI.GridLayoutGroup>(node).startAxis = Choice<UnityEngine.UI.GridLayoutGroup.Axis>(value); break;
                default: return false;
            }
            return true;
        }

        public static void ConfigureText(TMPro.TMP_Text text, UiPrefabNode node)
        {
            if (node.font_size_min > node.font_size_max) throw new Exception("font_size_min cannot exceed font_size_max");
            TextAlignment(text, node.text_alignment);
            Overflow(text, node.text_overflow);
            text.textWrappingMode = node.word_wrap ? TMPro.TextWrappingModes.Normal : TMPro.TextWrappingModes.NoWrap;
            text.enableAutoSizing = node.auto_size;
            text.fontSizeMin = node.font_size_min;
            text.fontSizeMax = node.font_size_max;
            text.lineSpacing = node.line_spacing;
            // For buttons raycast_target belongs to the background, not its label.
            text.raycastTarget = node.kind == "text" && node.has_raycast_target && node.raycast_target;
        }
        public static void ConfigureImage(UnityEngine.UI.Image image, UiPrefabNode node)
        {
            image.type = Choice<UnityEngine.UI.Image.Type>(node.image_type);
            image.preserveAspect = node.preserve_aspect;
            image.fillMethod = Choice<UnityEngine.UI.Image.FillMethod>(node.fill_method);
            if (node.fill_origin < 0 || node.fill_origin > MaxOrigin(image.fillMethod)) throw new Exception("Invalid fill_origin for fill_method");
            image.fillOrigin = node.fill_origin;
            image.fillAmount = node.fill_amount;
            image.fillClockwise = node.fill_clockwise;
            image.raycastTarget = node.has_raycast_target ? node.raycast_target : node.kind == "button";
        }
        public static void ConfigureLayout(UnityEngine.UI.LayoutGroup group, UiPrefabNode node)
        {
            group.childAlignment = Choice<TextAnchor>(node.child_alignment);
            group.padding = new RectOffset(node.padding, node.padding, node.padding, node.padding);
            if (group is UnityEngine.UI.HorizontalOrVerticalLayoutGroup linear)
            {
                linear.spacing = node.spacing;
                linear.childControlWidth = node.child_control_width;
                linear.childControlHeight = node.child_control_height;
                linear.childForceExpandWidth = node.child_force_expand_width;
                linear.childForceExpandHeight = node.child_force_expand_height;
                linear.childScaleWidth = node.child_scale_width;
                linear.childScaleHeight = node.child_scale_height;
            }
            if (group is UnityEngine.UI.GridLayoutGroup grid)
            {
                if (node.grid_cell_size == null || node.grid_cell_size.x <= 0 || node.grid_cell_size.y <= 0)
                    throw new Exception("Grid cell size must be positive");
                grid.cellSize = new Vector2(node.grid_cell_size.x, node.grid_cell_size.y);
                grid.spacing = new Vector2(node.grid_spacing.x, node.grid_spacing.y);
                grid.constraint = Choice<UnityEngine.UI.GridLayoutGroup.Constraint>(node.grid_constraint);
                grid.constraintCount = node.grid_constraint_count;
                grid.startCorner = Choice<UnityEngine.UI.GridLayoutGroup.Corner>(node.grid_start_corner);
                grid.startAxis = Choice<UnityEngine.UI.GridLayoutGroup.Axis>(node.grid_start_axis);
            }
        }
    }
}
