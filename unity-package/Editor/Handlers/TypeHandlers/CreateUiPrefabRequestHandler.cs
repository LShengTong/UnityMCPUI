using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class CreateUiPrefabRequestHandler : IEditorRequestHandler
    {
        readonly EditorTaskQueue tasks;

        public CreateUiPrefabRequestHandler(EditorTaskQueue tasks)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        public Task Handle(HttpListenerContext context) =>
            HttpRequestUtility.Handle<CreateUiPrefabRequest>(context, tasks, Execute, Error,
                request => RequestProtocolValidation.Validate(request));

        static string Error(string id, string message) => JsonUtility.ToJson(new UiPrefabBridge.Reply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, ok = false, error = message
        });

        static string Execute(CreateUiPrefabRequest request)
        {
            try
            {
                // Recheck at execution time: a valid request may have expired in the queue.
                double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                if (request.deadline <= now) throw new Exception("Request expired before execution");
                var result = UiPrefabBridge.Create(request.output_path, request.spec, request.deadline);
                result.id = request.id;
                result.instance_id = EditorInstanceInfo.InstanceId;
                return JsonUtility.ToJson(result);
            }
            catch (Exception e)
            {
                return Error(request.id, e.Message);
            }
        }
    }
}
