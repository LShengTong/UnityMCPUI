using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    /// <summary>Project-local immutable prefab snapshots plus atomically written metadata.</summary>
    internal static class UiPrefabBackupStore
    {
        static string Root => Path.Combine(Path.GetDirectoryName(Application.dataPath), "Library", "UnityMcp", "Backups");
        public static string AssetFile(string path) => Path.Combine(Path.GetDirectoryName(Application.dataPath), path);
        static string FileFor(string id, string extension)
        {
            if (id == null || id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c))) throw new Exception("Invalid backup_id");
            return Path.Combine(Root, id + extension);
        }
        public static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
        static byte[] ReadBounded(string file)
        {
            using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length > 64 * 1024 * 1024) throw new Exception("Prefab backup exceeds 64 MiB");
                using (var memory = new MemoryStream()) { stream.CopyTo(memory); return memory.ToArray(); }
            }
        }
        public static BackupEntry Create(string assetPath, string expectedVersion, string summary)
        {
            if (UiPrefabVersion.Read(assetPath) != expectedVersion) throw new Exception("Prefab changed before backup");
            byte[] contents = ReadBounded(AssetFile(assetPath));
            string guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid)) throw new Exception("Prefab GUID unavailable");
            if (UiPrefabVersion.Read(assetPath) != expectedVersion) throw new Exception("Prefab changed while reading backup");
            var entry = new BackupEntry { backup_id = Guid.NewGuid().ToString("N"), asset_path = assetPath,
                asset_guid = guid, created_at = DateTimeOffset.UtcNow.ToString("O"), summary = summary,
                before_version = expectedVersion, sha256 = Hash(contents) };
            Directory.CreateDirectory(Root);
            string snapshot = FileFor(entry.backup_id, ".prefab");
            try
            {
                WriteAtomic(snapshot, contents);
                WriteEntry(entry);
            }
            catch
            {
                if (File.Exists(snapshot)) File.Delete(snapshot);
                throw;
            }
            return entry;
        }
        static void WriteEntry(BackupEntry entry) => WriteAtomic(FileFor(entry.backup_id, ".json"), System.Text.Encoding.UTF8.GetBytes(JsonUtility.ToJson(entry)));
        public static void MarkSaved(BackupEntry entry, string version)
        {
            entry.after_version = version;
            WriteEntry(entry);
        }
        public static BackupEntry Read(string id)
        {
            var entry = JsonUtility.FromJson<BackupEntry>(File.ReadAllText(FileFor(id, ".json")));
            if (entry == null || entry.backup_id != id || string.IsNullOrEmpty(entry.sha256)) throw new Exception("Invalid backup metadata");
            return entry;
        }
        public static byte[] ReadContents(BackupEntry entry)
        {
            byte[] bytes = ReadBounded(FileFor(entry.backup_id, ".prefab"));
            if (Hash(bytes) != entry.sha256) throw new Exception("Backup checksum mismatch");
            return bytes;
        }
        public static BackupEntry[] List(string assetPath)
        {
            if (!Directory.Exists(Root)) return Array.Empty<BackupEntry>();
            return Directory.EnumerateFiles(Root, "*.json").Select(file => Read(Path.GetFileNameWithoutExtension(file)))
                .Where(entry => entry.asset_path == assetPath)
                .OrderByDescending(entry => entry.created_at, StringComparer.Ordinal)
                .ThenBy(entry => entry.backup_id, StringComparer.Ordinal).ToArray();
        }
        public static void WriteAtomic(string target, byte[] bytes)
        {
            string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                if (File.Exists(target)) File.Replace(temporary, target, null);
                else File.Move(temporary, target);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
