using System;
using System.Net;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class ListUiBackupsRequestHandler : IEditorRequestHandler
    {
        readonly EditorTaskQueue tasks;

        public ListUiBackupsRequestHandler(EditorTaskQueue tasks)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        public Task Handle(HttpListenerContext context) =>
            HttpRequestUtility.Handle<ListUiBackupsRequest>(context, tasks, Execute, Error, Validate);

        static void Validate(ListUiBackupsRequest request) => RequestProtocolValidation.Validate(request);

        static string Error(string id, string message) => JsonUtility.ToJson(new ListUiBackupsReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message
        });
        static string Execute(ListUiBackupsRequest request)
        {
            try
            {
                UiAssetQueryUtility.CheckDeadline(request.deadline);
                UiAssetQueryUtility.ValidatePath(request.asset_path);
                if (!request.asset_path.EndsWith(".prefab", StringComparison.Ordinal)) throw new Exception("Expected prefab path");
                var entries = UiPrefabBackupStore.List(request.asset_path);
                var page = entries.Skip(request.offset).Take(request.limit).ToArray();
                int next = request.offset + page.Length;
                return JsonUtility.ToJson(new ListUiBackupsReply { id = request.id, instance_id = EditorInstanceInfo.InstanceId,
                    ok = true, total = entries.Length, next_offset = next < entries.Length ? next : -1, backups = page });
            }
            catch (Exception e) { return Error(request.id, e.Message); }
        }
    }
}
