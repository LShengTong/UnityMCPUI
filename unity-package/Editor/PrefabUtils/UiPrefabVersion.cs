using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;

namespace UnityMcp.Editor
{
    internal static class UiPrefabVersion
    {
        public static string Read(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)
                || !path.EndsWith(".prefab", StringComparison.Ordinal) || path.Contains('\\')
                || path.Contains(':') || path.Split('/').Any(p => p == "" || p == "." || p == ".."))
                throw new Exception("Expected an Assets/.../*.prefab path");
            string current = Path.GetFullPath(".");
            foreach (string part in path.Split('/'))
            {
                current = Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new Exception("Symlink asset paths are unsupported");
            }
            using (var hash = SHA256.Create())
            {
                // Include dependencies so changes to nested prefabs also invalidate node addresses.
                string content = Convert.ToBase64String(File.ReadAllBytes(path)) + AssetDatabase.GetAssetDependencyHash(path);
                return BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(content))).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
