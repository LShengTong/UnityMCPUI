using System;
using System.Net;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class RefreshAssetsRequestHandler : IEditorRequestHandler
    {
        readonly EditorTaskQueue tasks;
        public RefreshAssetsRequestHandler(EditorTaskQueue tasks) { this.tasks = tasks; }
        public Task Handle(HttpListenerContext context) => HttpRequestUtility.Handle<RefreshAssetsRequest>(
            context, tasks, Execute, Error, request => RequestProtocolValidation.Validate(request));
        static string Error(string id, string message) => JsonUtility.ToJson(new RefreshAssetsReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message });
        static string Execute(RefreshAssetsRequest request)
        {
            if (request.deadline <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0)
                return Error(request.id, "Request expired before execution");
            // Execute already runs on the main thread. Compilation/reload may outlive Refresh.
            string refreshId = EditorCompilationState.RequestRefresh();
            try
            {
                AssetDatabase.Refresh();
            }
            catch (Exception e)
            {
                return Error(request.id, e.Message);
            }
            finally
            {
                EditorCompilationState.RefreshReturned();
            }
            return JsonUtility.ToJson(new RefreshAssetsReply { id = request.id,
                instance_id = EditorInstanceInfo.InstanceId, ok = true, refresh_requested = true, refresh_id = refreshId });
        }
    }
}
