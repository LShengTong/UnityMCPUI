using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class RuntimeUiInspection
    {
        internal static Component ExactComponent(Transform node, string type)
        {
            var found = node.GetComponents<Component>().Where(c => c != null && c.GetType().FullName == type).ToArray();
            if (found.Length != 1) throw new Exception("Missing or ambiguous component: " + type);
            return found[0];
        }
        internal static string ReferencePath(UnityEngine.Object target)
        {
            if (target != null && EditorUtility.IsPersistent(target)) return AssetDatabase.GetAssetPath(target);
            if (target is Component component) return PlayModeSession.ScenePath(component.transform);
            if (target is GameObject go) return PlayModeSession.ScenePath(go.transform);
            return target == null ? "" : AssetDatabase.GetAssetPath(target);
        }
        internal static string Value(Transform node, UiTestStep step)
        {
            switch (step.property)
            {
                case "active_self": return node.gameObject.activeSelf ? "true" : "false";
                case "active_in_hierarchy": return node.gameObject.activeInHierarchy ? "true" : "false";
                case "text":
                    var input = node.GetComponent<TMPro.TMP_InputField>();
                    if (input != null) return input.text ?? "";
                    var text = node.GetComponent<TMPro.TMP_Text>();
                    if (text == null) throw new Exception("Node requires TMP_Text");
                    return text.text ?? "";
                case "interactable":
                    var selectable = node.GetComponent<UnityEngine.UI.Selectable>();
                    if (selectable == null) throw new Exception("Node requires Selectable");
                    return selectable.IsActive() && selectable.IsInteractable() ? "true" : "false";
                case "reference_is_set":
                case "reference_path":
                    using (var serialized = new SerializedObject(ExactComponent(node, step.component_type)))
                    {
                        var property = serialized.FindProperty(step.field_path);
                        if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                            throw new Exception("Field must be a serialized object reference");
                        return step.property == "reference_is_set" ? (property.objectReferenceValue != null ? "true" : "false")
                            : ReferencePath(property.objectReferenceValue);
                    }
                default: throw new Exception("Unsupported runtime property");
            }
        }
        internal static RuntimeUiInspectionResult Inspect(string test_instance_id, string scene_name, string path, int max_nodes)
        {
            PlayModeSession.NeedPlay();
            Transform[] roots;
            if (!string.IsNullOrEmpty(test_instance_id)) roots = new[] {
                PlayModeSession.Relative(PlayModeSession.Root(test_instance_id).transform, path) };
            else if (!string.IsNullOrEmpty(path)) roots = new[] { PlayModeSession.SceneNode(path, scene_name) };
            else roots = PlayModeSession.SceneRoots(scene_name).Select(go => go.transform).ToArray();
            var nodes = new List<RuntimeUiNode>();
            bool truncated = false;
            foreach (var root in roots) Visit(root, "0", nodes, max_nodes, ref truncated, 0);
            return new RuntimeUiInspectionResult { 
                ok = true, session_id = PlayModeSession.Id, nodes = nodes.ToArray(), truncated = truncated };
        }
        static void Visit(Transform node, string path, List<RuntimeUiNode> nodes, int max, ref bool truncated, int depth)
        {
            if (depth > 64 || nodes.Count >= max) { truncated = true; return; }
            var components = node.GetComponents<Component>().Where(c => c != null).ToArray();
            var references = new List<RuntimeReferenceInfo>();
            foreach (var component in components)
            {
                using (var serialized = new SerializedObject(component))
                {
                    var property = serialized.GetIterator();
                    int count = 0;
                    while (property.NextVisible(true) && count++ < 128)
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.name != "m_Script")
                            references.Add(new RuntimeReferenceInfo { component_type = component.GetType().FullName,
                                field_path = property.propertyPath, target_path = ReferencePath(property.objectReferenceValue),
                                target_type = property.objectReferenceValue == null ? "" : property.objectReferenceValue.GetType().FullName });
                }
            }
            var text = node.GetComponent<TMPro.TMP_Text>();
            var input = node.GetComponent<TMPro.TMP_InputField>();
            string content = (input != null ? input.text : text != null ? text.text : "") ?? "";
            var selectable = node.GetComponent<UnityEngine.UI.Selectable>();
            nodes.Add(new RuntimeUiNode { name = node.name, path = path, scene_path = PlayModeSession.ScenePath(node),
                active_self = node.gameObject.activeSelf, active_in_hierarchy = node.gameObject.activeInHierarchy,
                components = components.Select(c => c.GetType().FullName).ToArray(),
                has_text = text != null || input != null, text = content.Substring(0, Math.Min(content.Length, 4096)),
                has_selectable = selectable != null, interactable = selectable != null && selectable.IsActive() && selectable.IsInteractable(),
                is_canvas = node.GetComponent<Canvas>() != null, is_event_system = node.GetComponent<UnityEngine.EventSystems.EventSystem>() != null,
                ui = components.Where(c => c is UnityEngine.EventSystems.UIBehaviour || c is Canvas || c is CanvasGroup)
                    .Select(UiPrefabAssetQuery.ReadComponent).ToArray(), references = references.ToArray() });
            for (int i = 0; i < node.childCount; i++) Visit(node.GetChild(i), path + "/" + i, nodes, max, ref truncated, depth + 1);
        }
    }
}
