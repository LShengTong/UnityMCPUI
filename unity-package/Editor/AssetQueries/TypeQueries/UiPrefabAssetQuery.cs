using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace UnityMcp.Editor
{
    internal sealed class UiPrefabAssetQuery : UiAssetQuery
    {
        public override UiAssetType Type => UiAssetType.UiPrefab;
        protected override string Filter => "t:Prefab";
        public override bool Matches(UnityEngine.Object asset) => asset is GameObject go
            && AssetDatabase.GetAssetPath(go).EndsWith(".prefab", StringComparison.Ordinal)
            && go.GetComponentInChildren<RectTransform>(true) != null;
        protected override void ValidateInspection(UiAssetInspection query)
        {
            if (!query.asset_path.EndsWith(".prefab", StringComparison.Ordinal)) throw new Exception("Expected .prefab asset");
            if (query.max_nodes < 1 || query.max_nodes > 1000) throw new Exception("max_nodes must be 1..1000");
        }
        public override UiAssetInspectionResult Inspect(UiAssetInspection query, int index, double deadline)
        {
            UiAssetQueryUtility.CheckDeadline(deadline);
            var root = (GameObject)Resolve(query);
            var details = new PrefabDetails { asset_path = query.asset_path, version = UiPrefabVersion.Read(query.asset_path) };
            var nodes = new List<PrefabNodeInfo>();
            ReadNode(root.transform, "0", "", 0, query, nodes, details, deadline);
            details.nodes = nodes.ToArray();
            details.total = nodes.Count;
            return new PrefabInspectionReply { index = index, ok = true, asset_type = "ui_prefab",
                asset = UiAssetQueryUtility.Describe(root, Type), prefab = details };
        }
        static void ReadNode(Transform transform, string indexPath, string parentPath, int depth,
            UiAssetInspection request, List<PrefabNodeInfo> nodes, PrefabDetails reply, double deadline)
        {
            UiAssetQueryUtility.CheckDeadline(deadline);
            if (nodes.Count >= request.max_nodes || depth > 64) { reply.truncated = true; return; }
            var components = transform.GetComponents<Component>();
            var node = new PrefabNodeInfo {
                name = transform.name, index_path = indexPath, parent_index_path = parentPath,
                active_self = transform.gameObject.activeSelf,
                components = components.Select(c => c == null ? "MissingScript" : c.GetType().FullName).ToArray(),
                ui = components.Where(IsUiComponent).Select(ReadComponent).ToArray()
            };
            if (transform is RectTransform rect)
                node.rect = new PrefabRectInfo {
                    anchor_min = rect.anchorMin, anchor_max = rect.anchorMax, pivot = rect.pivot,
                    size_delta = rect.sizeDelta, offset_min = rect.offsetMin, offset_max = rect.offsetMax,
                    anchored_position = rect.anchoredPosition3D, local_scale = rect.localScale,
                    local_euler_angles = rect.localEulerAngles
                };
            nodes.Add(node);
            for (int i = 0; i < transform.childCount; i++)
            {
                if (nodes.Count >= request.max_nodes) { reply.truncated = true; break; }
                ReadNode(transform.GetChild(i), indexPath + "/" + i, indexPath, depth + 1, request, nodes, reply, deadline);
            }
        }

        static bool IsUiComponent(Component component)
        {
            return component is UnityEngine.EventSystems.UIBehaviour || component is Canvas || component is CanvasGroup;
        }

        internal static PrefabComponentInfo ReadComponent(Component component)
        {
            // Serialized properties capture built-in UI configuration, including persistent events.
            // Keep values typed by object_type and asset_path where Unity references are involved.
            var values = new List<PrefabProperty>();
            bool truncated = false;
            using (var serialized = new SerializedObject(component))
            {
                var property = serialized.GetIterator();
                bool children = true;
                while (property.NextVisible(children))
                {
                    children = true;
                    if (values.Count >= 128) { truncated = true; break; }
                    if (property.propertyPath == "m_Script") continue;
                    string value;
                    switch (property.propertyType)
                    {
                        case SerializedPropertyType.String:
                            value = property.stringValue;
                            if (value.Length > 2048) { value = value.Substring(0, 2048); truncated = true; }
                            break;
                        case SerializedPropertyType.Boolean: value = property.boolValue ? "true" : "false"; break;
                        case SerializedPropertyType.Integer: value = property.longValue.ToString(System.Globalization.CultureInfo.InvariantCulture); break;
                        case SerializedPropertyType.Float: value = property.doubleValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture); break;
                        case SerializedPropertyType.Enum:
                            value = property.enumValueIndex >= 0 && property.enumValueIndex < property.enumNames.Length
                                ? property.enumNames[property.enumValueIndex] : property.intValue.ToString(); break;
                        case SerializedPropertyType.Color: value = "#" + ColorUtility.ToHtmlStringRGBA(property.colorValue); break;
                        case SerializedPropertyType.Vector2: value = JsonUtility.ToJson(property.vector2Value); break;
                        case SerializedPropertyType.Vector3: value = JsonUtility.ToJson(property.vector3Value); break;
                        case SerializedPropertyType.Vector4: value = JsonUtility.ToJson(property.vector4Value); break;
                        case SerializedPropertyType.ObjectReference:
                            var target = property.objectReferenceValue;
                            values.Add(new PrefabProperty { name = property.propertyPath, value = target == null ? "" : target.name,
                                asset_path = target == null ? "" : AssetDatabase.GetAssetPath(target),
                                object_type = target == null ? "" : target.GetType().FullName });
                            continue;
                        default: continue;
                    }
                    values.Add(new PrefabProperty { name = property.propertyPath, value = value });
                }
            }
            return new PrefabComponentInfo { type = component.GetType().FullName,
                enabled = !(component is Behaviour behaviour) || behaviour.enabled,
                properties_truncated = truncated, properties = values.ToArray() };
        }
    }
}
