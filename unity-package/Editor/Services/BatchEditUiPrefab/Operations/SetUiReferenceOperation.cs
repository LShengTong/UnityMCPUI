using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class SetUiReferenceOperation
    {
        static Component ComponentAt(Transform node, string type, int index)
        {
            var components = node.GetComponents<Component>().Where(c => c != null && c.GetType().FullName == type).ToArray();
            if (components.Length == 0) throw new Exception("Component not found: " + type);
            if (index < 0 && components.Length > 1) throw new Exception("Multiple components match; specify component index: " + type);
            if (index < 0) index = 0;
            if (index >= components.Length) throw new Exception("Component index out of range: " + type);
            return components[index];
        }
        static Type FieldType(Type type, string path)
        {
            foreach (string name in path.Split('.'))
            {
                FieldInfo field = null;
                for (var current = type; current != null && field == null; current = current.BaseType)
                    field = current.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field == null) throw new Exception("Serialized field not found: " + path);
                type = field.FieldType;
            }
            return type;
        }
        public static void Execute(Transform root, Transform node, Transform target, UiBatchOperation operation)
        {
            if (node == null || (node != root && !node.IsChildOf(root))) throw new Exception("Reference owner was removed");
            var component = ComponentAt(node, operation.component_type, operation.component_index);
            using (var serialized = new SerializedObject(component))
            {
                var property = serialized.FindProperty(operation.field_path);
                if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                    throw new Exception("Field must be a serialized object reference: " + operation.field_path);
                UnityEngine.Object value = null;
                if (!operation.clear)
                {
                    if (target == null || (target != root && !target.IsChildOf(root))) throw new Exception("Reference target was removed");
                    value = string.IsNullOrEmpty(operation.target_component) ? (UnityEngine.Object)target.gameObject
                        : ComponentAt(target, operation.target_component, operation.target_component_index);
                }
                var fieldType = FieldType(component.GetType(), operation.field_path);
                if (!typeof(UnityEngine.Object).IsAssignableFrom(fieldType) || (value != null && !fieldType.IsInstanceOfType(value)))
                    throw new Exception("Reference type mismatch for " + operation.field_path + "; expected " + fieldType.FullName);
                property.objectReferenceValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
            }
        }
    }
}
