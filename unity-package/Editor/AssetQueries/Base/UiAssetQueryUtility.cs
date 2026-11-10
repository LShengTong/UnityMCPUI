using System;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace UnityMcp.Editor
{
    internal static class UiAssetQueryUtility
    {
        public static void CheckDeadline(double deadline)
        {
            if (deadline <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0)
                throw new Exception("Asset query expired");
        }
        public static void ValidatePath(string path, bool folder = false)
        {
            if (folder && path == "Assets") return;
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)
                || path.Contains('\\') || path.Contains(':') || path.Split('/').Any(p => p == "" || p == "." || p == ".."))
                throw new Exception("Expected an Assets/ path with forward slashes");
        }
        public static AssetReference Describe(UnityEngine.Object asset, UiAssetType type)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId))
                throw new Exception("Asset has no persistent identity");
            return new AssetReference { name = asset.name, asset_path = AssetDatabase.GetAssetPath(asset),
                guid = guid, local_id = localId.ToString(CultureInfo.InvariantCulture), asset_type = UiAssetQueryRegistry.Name(type) };
        }
    }
}
