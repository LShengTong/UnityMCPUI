using System;
using System.IO;
using UnityEngine;

namespace UnityMcp.Editor
{
    /// <summary>Publishes the local HTTP endpoint; identity is obtained during the handshake.</summary>
    internal static class EditorDiscoveryFile
    {
        [Serializable] class Descriptor
        {
            public int port;
            public string token;
        }
        // Resolve on the main thread at the point of use, without storing project metadata.
        static string FilePath => Path.Combine(
            Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
            "Library", "UnityMcp", "bridge.json");

        public static void Write(int port)
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".tmp";
            var descriptor = new Descriptor { port = port, token = EditorInstanceInfo.Token };
            try
            {
                File.WriteAllText(temporary, JsonUtility.ToJson(descriptor));
                if (File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        public static void DeleteOwned()
        {
            string path = FilePath;
            if (!File.Exists(path)) return;
            var descriptor = JsonUtility.FromJson<Descriptor>(File.ReadAllText(path));
            if (descriptor != null && descriptor.token == EditorInstanceInfo.Token)
                File.Delete(path);
        }
    }
}
