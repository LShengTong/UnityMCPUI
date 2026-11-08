using System;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class UiPrefabEditScope : IDisposable
    {
        readonly string assetPath;
        readonly double deadline;
        readonly string version;
        readonly Vector3 originalRootScale;
        readonly bool originalRootActive;
        public GameObject Root { get; private set; }
        public bool Saved { get; private set; }
        public string BackupId { get; private set; }
        public string BackupWarning { get; private set; }

        public UiPrefabEditScope(string assetPath, string expectedVersion, double deadline)
        {
            this.assetPath = assetPath;
            this.deadline = deadline;
            CheckDeadline();
            version = UiPrefabVersion.Read(assetPath);
            if (version != expectedVersion) throw new Exception("Prefab changed; inspect again before modifying");
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == assetPath)
                throw new Exception("Close this prefab's editing stage before modifying");
            if (!AssetDatabase.IsOpenForEdit(assetPath)) throw new Exception("Prefab is not writable");
            // Read the persistent asset before loading an active Canvas into a preview scene.
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (source == null) throw new Exception("Prefab not found");
            originalRootScale = source.transform.localScale;
            originalRootActive = source.activeSelf;
            Root = PrefabUtility.LoadPrefabContents(assetPath);
            Root.transform.localScale = originalRootScale;
            if (Root.GetComponentInChildren<RectTransform>(true) == null)
            {
                Dispose();
                throw new Exception("Prefab contains no uGUI RectTransform");
            }
        }
        public void CheckDeadline()
        {
            if (deadline <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0)
                throw new Exception("Modification expired before saving");
        }
        public string Save(string summary)
        {
            int count = 0;
            CheckTree(Root.transform, 0, ref count);
            CheckDeadline();
            if (UiPrefabVersion.Read(assetPath) != version)
                throw new Exception("Prefab changed while preparing modifications; inspect again");
            var backup = UiPrefabBackupStore.Create(assetPath, version, summary);
            BackupId = backup.backup_id;
            CheckDeadline();
            if (UiPrefabVersion.Read(assetPath) != version) throw new Exception("Prefab changed after backup; refusing save");
            // Preserve an intentional root active-state edit; source scale remains invariant.
            bool intendedActive = Root.activeSelf;
            try
            {
                UiPrefabSaveUtility.Save(Root, assetPath, originalRootScale, intendedActive, () => Saved = true);
            }
            catch (Exception saveError)
            {
                if (!Saved) throw;
                try
                {
                    UiPrefabBackupStore.WriteAtomic(UiPrefabBackupStore.AssetFile(assetPath), UiPrefabBackupStore.ReadContents(backup));
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                    var restored = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                    if (restored == null || restored.transform.localScale != originalRootScale || restored.activeSelf != originalRootActive)
                        throw new Exception("Restored root state verification failed");
                    Saved = false;
                }
                catch (Exception rollback) { throw new Exception(saveError.Message + "; rollback failed, backup_id=" + BackupId + ": " + rollback.Message); }
                throw new Exception(saveError.Message + "; original prefab restored, backup_id=" + BackupId);
            }
            string afterVersion = UiPrefabVersion.Read(assetPath);
            try { UiPrefabBackupStore.MarkSaved(backup, afterVersion); }
            catch (Exception e) { BackupWarning = "Prefab saved; backup completion metadata could not be updated: " + e.Message; }
            return afterVersion;
        }
        static void CheckTree(Transform node, int depth, ref int count)
        {
            if (depth > 64 || ++count > 2000) throw new Exception("Edited prefab exceeds 64 levels or 2000 nodes");
            for (int i = 0; i < node.childCount; i++) CheckTree(node.GetChild(i), depth + 1, ref count);
        }
        public void Dispose()
        {
            if (Root == null) return;
            PrefabUtility.UnloadPrefabContents(Root);
            Root = null;
        }
    }
}
