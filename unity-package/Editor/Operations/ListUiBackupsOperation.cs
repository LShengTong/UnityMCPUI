using System;
using System.Linq;
using UnityEngine;
namespace UnityMcp.Editor
{
    internal static class ListUiBackupsOperation
    {
        [Serializable] sealed class Reply
        {
            public string id, instance_id, error;
            public bool ok;
            public int total, next_offset = -1;
            public UiPrefabBackupStore.Entry[] backups;
        }
        public static string Error(string id, string message) => JsonUtility.ToJson(new Reply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message
        });
        public static string Execute(ListUiBackupsRequest request)
        {
            try
            {
                UiAssetQueryUtility.CheckDeadline(request.deadline);
                UiAssetQueryUtility.ValidatePath(request.asset_path);
                if (!request.asset_path.EndsWith(".prefab", StringComparison.Ordinal)) throw new Exception("Expected prefab path");
                var entries = UiPrefabBackupStore.List(request.asset_path);
                var page = entries.Skip(request.offset).Take(request.limit).ToArray();
                int next = request.offset + page.Length;
                return JsonUtility.ToJson(new Reply { id = request.id, instance_id = EditorInstanceInfo.InstanceId,
                    ok = true, total = entries.Length, next_offset = next < entries.Length ? next : -1, backups = page });
            }
            catch (Exception e) { return Error(request.id, e.Message); }
        }
    }
}
