using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class AddUiScriptOperation
    {
        public static void Execute(Transform root, Transform node, UiBatchOperation operation)
        {
            UiNodeUtility.EnsureEditable(node, root);
            UiPrefabBridge.ValidateAssetPath(operation.script_path, ".cs");
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(operation.script_path);
            var type = script == null ? null : script.GetClass();
            if (type == null || !typeof(MonoBehaviour).IsAssignableFrom(type) || type.IsAbstract || type.ContainsGenericParameters)
                throw new Exception("Script must compile to a concrete MonoBehaviour: " + operation.script_path);
            if (!CompilationPipeline.GetAssemblies(AssembliesType.Player).Any(a => a.name == type.Assembly.GetName().Name))
                throw new Exception("Editor-only scripts cannot be attached to runtime prefabs");
            if (node.GetComponent(type) != null) throw new Exception("Script component already exists: " + type.FullName);
            if (node.gameObject.AddComponent(type) == null) throw new Exception("Unity could not add script component");
        }
    }
}
