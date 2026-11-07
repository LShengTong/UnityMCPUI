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
        [Serializable] internal sealed class Details
        {
            public string asset_path, version;
            public bool truncated;
            public int total;
            public Node[] nodes;
        }
        [Serializable] sealed class Reply : UiAssetInspectionResult { public Details prefab; }
        [Serializable] internal sealed class Node
        {
            public string name, index_path, parent_index_path;
            public bool active_self;
            public string[] components;
            public RectInfo rect;
            public ComponentInfo[] ui;
        }
        [Serializable] internal sealed class RectInfo
        {
            public Vector2 anchor_min, anchor_max, pivot, size_delta, offset_min, offset_max;
            public Vector3 anchored_position, local_scale, local_euler_angles;
        }
        [Serializable] internal sealed class ComponentInfo
        {
            public string type;
            public bool enabled, properties_truncated;
            public Property[] properties;
        }
        [Serializable] internal sealed class Property
        {
            public string name, value, asset_path, object_type;
        }

        public override UiAssetInspectionResult Inspect(UiAssetInspection query, int index, double deadline)
        {
            UiAssetQueryUtility.CheckDeadline(deadline);
            var root = (GameObject)Resolve(query);
            var details = new Details { asset_path = query.asset_path, version = UiPrefabVersion.Read(query.asset_path) };
            var nodes = new List<Node>();
            ReadNode(root.transform, "0", "", 0, query, nodes, details, deadline);
            details.nodes = nodes.ToArray();
            details.total = nodes.Count;
            return new Reply { index = index, ok = true, asset_type = "ui_prefab",
                asset = UiAssetQueryUtility.Describe(root, Type), prefab = details };
        }
        static void ReadNode(Transform transform, string indexPath, string parentPath, int depth,
            UiAssetInspection request, List<Node> nodes, Details reply, double deadline)
        {
            UiAssetQueryUtility.CheckDeadline(deadline);
            if (nodes.Count >= request.max_nodes || depth > 64) { reply.truncated = true; return; }
            var components = transform.GetComponents<Component>();
            var node = new Node {
                name = transform.name, index_path = indexPath, parent_index_path = parentPath,
                active_self = transform.gameObject.activeSelf,
                components = components.Select(c => c == null ? "MissingScript" : c.GetType().FullName).ToArray(),
                ui = components.Where(IsUiComponent).Select(ReadComponent).ToArray()
            };
            if (transform is RectTransform rect)
                node.rect = new RectInfo {
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

        static ComponentInfo ReadComponent(Component component)
        {
            // Serialized properties capture built-in UI configuration, including persistent events.
            // Keep values typed by object_type and asset_path where Unity references are involved.
            var values = new List<Property>();
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
                            values.Add(new Property { name = property.propertyPath, value = target == null ? "" : target.name,
                                asset_path = target == null ? "" : AssetDatabase.GetAssetPath(target),
                                object_type = target == null ? "" : target.GetType().FullName });
                            continue;
                        default: continue;
                    }
                    values.Add(new Property { name = property.propertyPath, value = value });
                }
            }
            return new ComponentInfo { type = component.GetType().FullName,
                enabled = !(component is Behaviour behaviour) || behaviour.enabled,
                properties_truncated = truncated, properties = values.ToArray() };
        }
    }
}
