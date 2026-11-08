using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp.Editor
{
    public static class UiPrefabBridge
    {
        [Serializable] public sealed class Reply
        {
            public string id, instance_id, asset_path, error, warning;
            public bool ok;
            public int node_count;
        }

        static readonly string Project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        static string ValidateAssetPath(string value, string suffix = null)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith("Assets/", StringComparison.Ordinal) ||
                value.Contains('\\') || value.Contains(':') || value.Split('/').Any(p => p == ".." || p == "." || p == "") ||
                (suffix != null && !value.EndsWith(suffix, StringComparison.Ordinal)))
                throw new Exception("Invalid Assets/ asset path: " + value);
            string current = Project;
            foreach (string part in value.Split('/'))
            {
                current = Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new Exception("Asset paths through symlinks/junctions are unsupported");
            }
            return value;
        }
        static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(c => char.IsControl(c) || c == '/' || c == '\\'))
                throw new Exception("Invalid object name");
        }
        static Color ParseColor(string value)
        {
            if (!ColorUtility.TryParseHtmlString(value, out var result)) throw new Exception("Invalid color: " + value);
            return result;
        }
        static bool Finite(float n) => !float.IsNaN(n) && !float.IsInfinity(n);
        static void Validate(UiPrefabNode[] nodes, int depth, ref int count)
        {
            if (nodes == null) return;
            if (depth > 12 || nodes.Select(n => n.name).Distinct().Count() != nodes.Length) throw new Exception("Invalid tree depth or duplicate siblings");
            foreach (var n in nodes)
            {
                if (++count > 128) throw new Exception("Too many UI nodes");
                ValidateName(n.name);
                if (!new[] { "panel", "image", "text", "button" }.Contains(n.kind)) throw new Exception("Unsupported node kind");
                UiRectTransformUtility.GetPreset(n.anchor, out _, out _, out _);
                if (n.has_offset_min) UiRectTransformUtility.ValidateOffset(n.offset_min);
                if (n.has_offset_max) UiRectTransformUtility.ValidateOffset(n.offset_max);
                if (!new[] { "none", "vertical", "horizontal" }.Contains(n.layout)) throw new Exception("Unsupported layout");
                if (!new[] { n.width, n.height, n.x, n.y, n.font_size, n.spacing }.All(Finite) || n.width <= 0 || n.height <= 0 || n.font_size <= 0 || n.spacing < 0 || n.padding < 0)
                    throw new Exception("Invalid dimensions");
                ParseColor(n.color); ParseColor(n.text_color);
                if ((n.kind == "button" || n.kind == "text") && ((n.children?.Length ?? 0) > 0 || n.layout != "none")) throw new Exception("Text/button cannot contain children/layout");
                if (n.kind == "text" || n.kind == "button") ResolveFont(n.font_path);
                if (!string.IsNullOrEmpty(n.sprite_path) && AssetDatabase.LoadAssetAtPath<Sprite>(ValidateAssetPath(n.sprite_path)) == null)
                    throw new Exception("Sprite not found: " + n.sprite_path);
                Validate(n.children, depth + 1, ref count);
            }
        }
        static TMPro.TMP_FontAsset ResolveFont(string path)
        {
            var font = string.IsNullOrEmpty(path) ? TMPro.TMP_Settings.defaultFontAsset : AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(ValidateAssetPath(path));
            if (font == null) throw new Exception("No TMP font. Configure TMP default font or supply an existing font_path; no resources were imported.");
            return font;
        }
        static void EnsureFolder(string folder)
        {
            string parent = "Assets";
            foreach (string part in folder.Substring("Assets".Length).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string child = parent + "/" + part;
                if (!AssetDatabase.IsValidFolder(child) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, part))) throw new Exception("Cannot create folder: " + child);
                parent = child;
            }
        }
        public static Reply Create(string outputPath, UiPrefabSpec spec, double deadline)
        {
            string output = ValidateAssetPath(outputPath, ".prefab");
            if (File.Exists(output) || File.Exists(output + ".meta") || AssetDatabase.LoadMainAssetAtPath(output) != null)
                throw new Exception("Output exists; overwrite is not supported");
            if (spec == null) throw new Exception("Missing UI spec");
            ValidateName(spec.name);
            if (!Finite(spec.reference_width) || !Finite(spec.reference_height) || spec.reference_width <= 0 || spec.reference_height <= 0 || spec.children == null || spec.children.Length == 0)
                throw new Exception("Invalid canvas size or children");
            int count = 0; Validate(spec.children, 1, ref count);
            var scene = EditorSceneManager.NewPreviewScene();
            bool saved = false;
            try
            {
                var root = new GameObject(spec.name, typeof(RectTransform));
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                var canvas = root.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = root.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(spec.reference_width, spec.reference_height);
                scaler.matchWidthOrHeight = .5f;
                root.AddComponent<UnityEngine.UI.GraphicRaycaster>();
                foreach (var node in spec.children) Build(node, root.transform, false);
                if (deadline < Now) throw new Exception("CreateUiPrefabRequest expired before saving");
                EnsureFolder(Path.GetDirectoryName(output).Replace('\\', '/'));
                if (File.Exists(output) || File.Exists(output + ".meta")) throw new Exception("Output appeared during creation; refusing overwrite");
                var prefab = UiPrefabSaveUtility.Save(root, output, Vector3.one, true, () => saved = true);
                if (prefab == null || prefab.GetComponent<Canvas>() == null || prefab.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
                    throw new Exception("Saved prefab verification failed");
                if (prefab.transform.localScale != Vector3.one)
                    throw new Exception("Saved root scale differs from intended unit scale");
                return new Reply { ok = true, asset_path = output, node_count = count,
                    warning = "Visual UI only: button callbacks are empty. Destination scene needs exactly one EventSystem with its input module. Verify font glyph coverage and layout in Prefab Mode." };
            }
            catch
            {
                if (saved) AssetDatabase.DeleteAsset(output);
                throw;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        internal static void AddNode(UiPrefabNode node, Transform parent)
        {
            if (node == null) throw new Exception("Missing node specification");
            int count = 0;
            Validate(new[] { node }, 1, ref count);
            if (node.kind == "text" && string.IsNullOrEmpty(node.text)) throw new Exception("Text nodes require text");
            Build(node, parent, parent.GetComponent<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>() != null);
        }
        static void Build(UiPrefabNode n, Transform parent, bool parentLayout)
        {
            var go = new GameObject(n.name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            UiRectTransformUtility.Configure(rect, n);
            if (parentLayout)
            {
                var le = go.AddComponent<UnityEngine.UI.LayoutElement>();
                le.preferredWidth = n.width; le.preferredHeight = n.height;
                le.minWidth = n.width; le.minHeight = n.height;
            }
            if (n.kind != "text")
            {
                var image = go.AddComponent<UnityEngine.UI.Image>(); image.color = ParseColor(n.color);
                image.raycastTarget = n.kind == "button";
                if (!string.IsNullOrEmpty(n.sprite_path)) image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(n.sprite_path);
                if (n.kind == "button")
                {
                    var button = go.AddComponent<UnityEngine.UI.Button>(); button.targetGraphic = image;
                    var label = new GameObject("Label", typeof(RectTransform)); label.layer = go.layer; label.transform.SetParent(go.transform, false);
                    var lr = (RectTransform)label.transform; lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                    lr.offsetMin = new Vector2(8, 4); lr.offsetMax = new Vector2(-8, -4);
                    AddText(label, n);
                }
            }
            else AddText(go, n);
            if (n.layout != "none")
            {
                UnityEngine.UI.HorizontalOrVerticalLayoutGroup group = n.layout == "vertical"
                    ? (UnityEngine.UI.HorizontalOrVerticalLayoutGroup)go.AddComponent<UnityEngine.UI.VerticalLayoutGroup>()
                    : go.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                group.spacing = n.spacing; group.padding = new RectOffset(n.padding, n.padding, n.padding, n.padding);
                group.childAlignment = TextAnchor.MiddleCenter;
                group.childControlWidth = group.childControlHeight = true;
                group.childForceExpandWidth = group.childForceExpandHeight = false;
            }
            if (n.children != null) foreach (var child in n.children) Build(child, go.transform, n.layout != "none");
        }
        static void AddText(GameObject go, UiPrefabNode n)
        {
            var label = go.AddComponent<TMPro.TextMeshProUGUI>();
            label.font = ResolveFont(n.font_path); label.text = n.text;
            label.fontSize = n.font_size; label.color = ParseColor(n.text_color);
            label.alignment = TMPro.TextAlignmentOptions.Center; label.raycastTarget = false;
        }
    }
}
