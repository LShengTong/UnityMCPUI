using System;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class ModifyUiNodePropertiesOperation
    {
        static T Need<T>(GameObject node) where T : Component
        {
            var value = node.GetComponent<T>();
            if (value == null) throw new Exception(node.name + " requires " + typeof(T).Name);
            return value;
        }
        static float Number(string text, float min, float max)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                || float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
                throw new Exception("Invalid number: " + text);
            return value;
        }
        static bool Boolean(string text)
        {
            if (text != "true" && text != "false") throw new Exception("Expected true or false");
            return text == "true";
        }
        static Color ColorValue(string text)
        {
            if (!ColorUtility.TryParseHtmlString(text, out var value)) throw new Exception("Invalid color");
            return value;
        }
        [Serializable] sealed class VectorInput { public float x = float.NaN, y = float.NaN; }
        static Vector2 Vector(string text, bool normalized = false)
        {
            var value = JsonUtility.FromJson<VectorInput>(text);
            if (value == null || float.IsNaN(value.x) || float.IsNaN(value.y)
                || float.IsInfinity(value.x) || float.IsInfinity(value.y)
                || Mathf.Abs(value.x) > 16384 || Mathf.Abs(value.y) > 16384
                || (normalized && (value.x < 0 || value.x > 1 || value.y < 0 || value.y > 1)))
                throw new Exception("Expected finite JSON Vector2 with x and y");
            return new Vector2(value.x, value.y);
        }
        static T Asset<T>(string path) where T : UnityEngine.Object
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)
                || path.Contains('\\') || path.Contains(':') || path.Split('/').Any(p => p == "" || p == "." || p == ".."))
                throw new Exception("Expected an Assets/... resource path");
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new Exception("Resource not found or wrong type: " + path);
            return asset;
        }
        static UnityEngine.UI.ContentSizeFitter.FitMode Fit(string value)
        {
            if (!new[] { "Unconstrained", "MinSize", "PreferredSize" }.Contains(value))
                throw new Exception("Fit mode must be Unconstrained, MinSize or PreferredSize");
            return (UnityEngine.UI.ContentSizeFitter.FitMode)Enum.Parse(typeof(UnityEngine.UI.ContentSizeFitter.FitMode), value);
        }
        public static void Execute(GameObject node, UiPropertyChange change)
        {
            if (change == null || change.value == null || change.value.Length > 4096)
                throw new Exception("Invalid change value");
            string value = change.value;
            switch (change.property)
            {
                case "preferred_width": Need<UnityEngine.UI.LayoutElement>(node).preferredWidth = Number(value, -1, 16384); break;
                case "preferred_height": Need<UnityEngine.UI.LayoutElement>(node).preferredHeight = Number(value, -1, 16384); break;
                case "min_width": Need<UnityEngine.UI.LayoutElement>(node).minWidth = Number(value, -1, 16384); break;
                case "min_height": Need<UnityEngine.UI.LayoutElement>(node).minHeight = Number(value, -1, 16384); break;
                case "flexible_width": Need<UnityEngine.UI.LayoutElement>(node).flexibleWidth = Number(value, -1, 16384); break;
                case "flexible_height": Need<UnityEngine.UI.LayoutElement>(node).flexibleHeight = Number(value, -1, 16384); break;
                case "horizontal_fit": Need<UnityEngine.UI.ContentSizeFitter>(node).horizontalFit = Fit(value); break;
                case "vertical_fit": Need<UnityEngine.UI.ContentSizeFitter>(node).verticalFit = Fit(value); break;
                case "grid_cell_size":
                    var size = Vector(value);
                    if (size.x <= 0 || size.y <= 0) throw new Exception("Grid cell size must be positive");
                    Need<UnityEngine.UI.GridLayoutGroup>(node).cellSize = size; break;
                case "grid_spacing": Need<UnityEngine.UI.GridLayoutGroup>(node).spacing = Vector(value); break;
                case "scroll_horizontal": Need<UnityEngine.UI.ScrollRect>(node).horizontal = Boolean(value); break;
                case "scroll_vertical": Need<UnityEngine.UI.ScrollRect>(node).vertical = Boolean(value); break;
                case "name":
                    if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || value.Any(c => char.IsControl(c) || c == '/' || c == '\\'))
                        throw new Exception("Invalid node name");
                    node.name = value; break;
                case "active": node.SetActive(Boolean(value)); break;
                case "text": Need<TMPro.TMP_Text>(node).text = value; break;
                case "font_size": Need<TMPro.TMP_Text>(node).fontSize = Number(value, 1, 512); break;
                case "text_color": Need<TMPro.TMP_Text>(node).color = ColorValue(value); break;
                case "font_path": Need<TMPro.TMP_Text>(node).font = Asset<TMPro.TMP_FontAsset>(value); break;
                case "sprite_path": Need<UnityEngine.UI.Image>(node).sprite = value == "" ? null : Asset<Sprite>(value); break;
                case "image_type":
                    if (!new[] { "Simple", "Sliced", "Tiled", "Filled" }.Contains(value))
                        throw new Exception("image_type must be Simple, Sliced, Tiled or Filled");
                    Need<UnityEngine.UI.Image>(node).type = (UnityEngine.UI.Image.Type)Enum.Parse(typeof(UnityEngine.UI.Image.Type), value);
                    break;
                case "color": Need<UnityEngine.UI.Graphic>(node).color = ColorValue(value); break;
                case "interactable": Need<UnityEngine.UI.Selectable>(node).interactable = Boolean(value); break;
                case "anchored_position": Need<RectTransform>(node).anchoredPosition = Vector(value); break;
                case "size_delta": Need<RectTransform>(node).sizeDelta = Vector(value); break;
                case "anchor": UiRectTransformUtility.SetPreset(Need<RectTransform>(node), value); break;
                case "offset_min": Need<RectTransform>(node).offsetMin = Vector(value); break;
                case "offset_max": Need<RectTransform>(node).offsetMax = Vector(value); break;
                case "anchor_min": Need<RectTransform>(node).anchorMin = Vector(value, true); break;
                case "anchor_max": Need<RectTransform>(node).anchorMax = Vector(value, true); break;
                case "pivot": Need<RectTransform>(node).pivot = Vector(value, true); break;
                case "spacing": Need<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>(node).spacing = Number(value, 0, 4096); break;
                case "padding":
                    int padding = (int)Number(value, 0, 4096);
                    if (padding.ToString(CultureInfo.InvariantCulture) != value) throw new Exception("padding must be an integer");
                    Need<UnityEngine.UI.LayoutGroup>(node).padding = new RectOffset(padding, padding, padding, padding); break;
                default: throw new Exception("Unsupported property: " + change.property);
            }
        }
    }
}
