using System;
using System.Net;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;
using System.IO;
using UnityEditor;

namespace UnityMcp.Editor
{
    internal sealed class RestoreUiPrefabRequestHandler : IEditorRequestHandler
    {
        readonly EditorTaskQueue tasks;

        public RestoreUiPrefabRequestHandler(EditorTaskQueue tasks)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        public Task Handle(HttpListenerContext context) =>
            HttpRequestUtility.Handle<RestoreUiPrefabRequest>(context, tasks, Execute, Error, Validate);

        static void Validate(RestoreUiPrefabRequest request) => RequestProtocolValidation.Validate(request);

        [Serializable] sealed class Reply
        {
            public string id, instance_id, asset_path, version, backup_id, restored_from, error, warning;
            public bool ok, saved, rolled_back;
        }
        static string Error(string id, string message) => JsonUtility.ToJson(new Reply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message
        });
        static string Execute(RestoreUiPrefabRequest request)
        {
            UiPrefabBackupStore.Entry safety = null;
            string path = null;
            bool written = false, rolledBack = false;
            try
            {
                UiAssetQueryUtility.CheckDeadline(request.deadline);
                var source = UiPrefabBackupStore.Read(request.backup_id);
                path = source.asset_path;
                byte[] contents = UiPrefabBackupStore.ReadContents(source);
                string version = UiPrefabVersion.Read(path);
                if (version != request.expected_version) throw new Exception("Prefab changed; inspect again before restoring");
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (guid != source.asset_guid || string.IsNullOrEmpty(guid))
                    throw new Exception("Prefab GUID changed; refusing to restore into a different asset");
                var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && stage.assetPath == path) throw new Exception("Close the prefab editing stage before restoring");
                if (!AssetDatabase.IsOpenForEdit(path)) throw new Exception("Prefab is not writable");
                safety = UiPrefabBackupStore.Create(path, version, "Before restoring backup " + request.backup_id);
                UiAssetQueryUtility.CheckDeadline(request.deadline);
                if (UiPrefabVersion.Read(path) != version) throw new Exception("Prefab changed after restore preparation");
                // Replace only the .prefab; never touch its .meta/GUID.
                UiPrefabBackupStore.WriteAtomic(UiPrefabBackupStore.AssetFile(path), contents);
                written = true;
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null || AssetDatabase.AssetPathToGUID(path) != source.asset_guid)
                    throw new Exception("Restored prefab could not be imported with its original GUID");
                if (UiPrefabBackupStore.Hash(File.ReadAllBytes(UiPrefabBackupStore.AssetFile(path))) != source.sha256)
                    throw new Exception("Restored contents changed during import");
                string afterVersion = UiPrefabVersion.Read(path);
                string warning = null;
                try { UiPrefabBackupStore.MarkSaved(safety, afterVersion); }
                catch (Exception e) { warning = "Restored successfully; backup completion metadata could not be updated: " + e.Message; }
                return JsonUtility.ToJson(new Reply { id = request.id, instance_id = EditorInstanceInfo.InstanceId,
                    ok = true, saved = true, asset_path = path, version = afterVersion,
                    backup_id = safety.backup_id, restored_from = source.backup_id, warning = warning });
            }
            catch (Exception e)
            {
                string message = e.Message;
                if (written && safety != null)
                {
                    try
                    {
                        UiPrefabBackupStore.WriteAtomic(UiPrefabBackupStore.AssetFile(path), UiPrefabBackupStore.ReadContents(safety));
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                        if (UiPrefabBackupStore.Hash(File.ReadAllBytes(UiPrefabBackupStore.AssetFile(path))) != safety.sha256)
                            throw new Exception("Rollback content verification failed");
                        rolledBack = true;
                        written = false;
                    }
                    catch (Exception rollback) { message += "; rollback failed: " + rollback.Message; }
                }
                if (safety != null) message += "; pre-restore backup_id=" + safety.backup_id;
                return JsonUtility.ToJson(new Reply { id = request.id, instance_id = EditorInstanceInfo.InstanceId,
                    error = message, asset_path = path, backup_id = safety?.backup_id, restored_from = request.backup_id,
                    saved = written, rolled_back = rolledBack });
            }
        }
    }
}
