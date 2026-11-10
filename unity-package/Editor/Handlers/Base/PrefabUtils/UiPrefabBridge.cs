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
        static readonly string Project = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        static double Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;

        internal static string ValidateAssetPath(string value, string suffix = null)
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
        internal static void Validate(UiPrefabNode[] nodes, int depth, ref int count)
        {
            if (nodes == null || nodes.Length == 0) return;
            if (nodes.Any(n => n == null)) throw new Exception("UI nodes cannot be null");
            if (depth > 12 || nodes.Select(n => n.name).Distinct().Count() != nodes.Length) throw new Exception("Invalid tree depth or duplicate siblings");
            foreach (var n in nodes)
            {
                if (++count > 128) throw new Exception("Too many UI nodes");
                ValidateName(n.name);
                if (!new[] { "panel", "image", "text", "button", "prefab" }.Contains(n.kind)) throw new Exception("Unsupported node kind");
                if (n.kind == "prefab" && (string.IsNullOrEmpty(n.prefab_path) || (n.children?.Length ?? 0) > 0 || n.layout != "none"))
                    throw new Exception("Prefab nodes require prefab_path and cannot define children/layout");
                if (n.kind != "prefab" && !string.IsNullOrEmpty(n.prefab_path)) throw new Exception("prefab_path requires kind=prefab");
                UiRectTransformUtility.GetPreset(n.anchor, out _, out _, out _);
                if (n.has_offset_min) UiRectTransformUtility.ValidateOffset(n.offset_min);
                if (n.has_offset_max) UiRectTransformUtility.ValidateOffset(n.offset_max);
                if (!new[] { "none", "vertical", "horizontal", "grid" }.Contains(n.layout)) throw new Exception("Unsupported layout");
                if (!new[] { n.width, n.height, n.x, n.y, n.font_size, n.spacing }.All(Finite) || n.width <= 0 || n.height <= 0 || n.font_size <= 0 || n.spacing < 0 || n.padding < 0)
                    throw new Exception("Invalid dimensions");
                ParseColor(n.color); ParseColor(n.text_color);
                if ((n.kind == "button" || n.kind == "text") && ((n.children?.Length ?? 0) > 0 || n.layout != "none")) throw new Exception("Text/button cannot contain children/layout");
                if (n.kind == "text" && string.IsNullOrEmpty(n.text)) throw new Exception("Text nodes require nonempty text");
                if (n.font_size_min > n.font_size_max) throw new Exception("font_size_min cannot exceed font_size_max");
                if ((n.fill_method == "Horizontal" || n.fill_method == "Vertical") && n.fill_origin > 1)
                    throw new Exception("Horizontal/Vertical fill_origin must be 0 or 1");
                if (n.layout == "grid" && (n.grid_cell_size == null || n.grid_cell_size.x <= 0 || n.grid_cell_size.y <= 0))
                    throw new Exception("Grid cell size must be positive");
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
        public static CreateUiPrefabReply Create(string outputPath, UiPrefabSpec spec, double deadline)
        {
            string output = ValidateAssetPath(outputPath, ".prefab");
            if (File.Exists(output) || File.Exists(output + ".meta") || AssetDatabase.LoadMainAssetAtPath(output) != null)
                throw new Exception("Output exists; overwrite is not supported");
            if (spec == null) throw new Exception("Missing UI spec");
            ValidateName(spec.name);
            if (spec.children == null || spec.children.Length == 0)
                throw new Exception("Missing UI children");
            int count = 0; Validate(spec.children, 1, ref count);
            var scene = EditorSceneManager.NewPreviewScene();
            bool saved = false;
            try
            {
                var root = new GameObject(spec.name, typeof(RectTransform));
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, scene);
                var rootRect = (RectTransform)root.transform;
                rootRect.anchorMin = Vector2.zero;
                rootRect.anchorMax = Vector2.one;
                rootRect.pivot = new Vector2(.5f, .5f);
                rootRect.sizeDelta = Vector2.zero;
                rootRect.anchoredPosition3D = Vector3.zero;
                foreach (var node in spec.children)
                {
                    Build(node, root.transform, false, output);
                    int builtCount = 0;
                    CheckTree(root.transform, 0, ref builtCount);
                }
                int actualCount = 0;
                CheckTree(root.transform, 0, ref actualCount);
                if (deadline < Now) throw new Exception("CreateUiPrefabRequest expired before saving");
                EnsureFolder(Path.GetDirectoryName(output).Replace('\\', '/'));
                if (File.Exists(output) || File.Exists(output + ".meta")) throw new Exception("Output appeared during creation; refusing overwrite");
                var prefab = UiPrefabSaveUtility.Save(root, output, Vector3.one, true, () => saved = true);
                if (prefab == null || !(prefab.transform is RectTransform) || prefab.GetComponent<Canvas>() != null)
                    throw new Exception("Saved prefab verification failed");
                if (prefab.transform.localScale != Vector3.one)
                    throw new Exception("Saved root scale differs from intended unit scale");
                return new CreateUiPrefabReply { ok = true, asset_path = output, node_count = count,
                    warning = "Mount this RectTransform prefab under the scene Canvas; CanvasScaler, GraphicRaycaster and EventSystem belong to the scene. Generated button callbacks are empty. Verify font glyph coverage and layout." };
            }
            catch
            {
                if (saved) AssetDatabase.DeleteAsset(output);
                throw;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }
        internal static void AddNode(UiPrefabNode node, Transform parent, string destination = null)
        {
            if (node == null) throw new Exception("Missing node specification");
            int count = 0;
            Validate(new[] { node }, 1, ref count);
            if (node.kind == "text" && string.IsNullOrEmpty(node.text)) throw new Exception("Text nodes require text");
            Build(node, parent, parent.GetComponent<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>() != null, destination);
        }
        internal static void CheckTree(Transform node, int depth, ref int count)
        {
            if (depth > 64 || ++count > 2000) throw new Exception("Prefab exceeds 64 levels or 2000 nodes");
            for (int i = 0; i < node.childCount; i++) CheckTree(node.GetChild(i), depth + 1, ref count);
        }
        static void Build(UiPrefabNode n, Transform parent, bool parentLayout, string destination)
        {
            if (n.kind == "prefab")
            {
                var instance = UiPrefabInstantiationUtility.Instantiate(n.prefab_path, parent, destination);
                instance.name = n.name;
                UiPrefabInstantiationUtility.ApplyLayout((RectTransform)instance.transform, n);
                var root = parent;
                while (root.parent != null) root = root.parent;
                int count = 0;
                CheckTree(root, 0, ref count);
                return;
            }
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
                UiVisualPropertyUtility.ConfigureImage(image, n);
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
                UnityEngine.UI.LayoutGroup group = n.layout == "grid"
                    ? (UnityEngine.UI.LayoutGroup)go.AddComponent<UnityEngine.UI.GridLayoutGroup>()
                    : n.layout == "vertical" ? (UnityEngine.UI.LayoutGroup)go.AddComponent<UnityEngine.UI.VerticalLayoutGroup>()
                    : go.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                UiVisualPropertyUtility.ConfigureLayout(group, n);
            }
            if (n.children != null) foreach (var child in n.children) Build(child, go.transform, n.layout == "vertical" || n.layout == "horizontal", destination);
        }
        static void AddText(GameObject go, UiPrefabNode n)
        {
            var label = go.AddComponent<TMPro.TextMeshProUGUI>();
            label.font = ResolveFont(n.font_path); label.text = n.text;
            label.fontSize = n.font_size; label.color = ParseColor(n.text_color);
            UiVisualPropertyUtility.ConfigureText(label, n);
        }
    }
}
