using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityMcp.Editor
{
    /// <summary>Reads, parses and validates UI input before passing it to the queue.</summary>
    internal sealed class CreateUiPrefabRequestHandler
    {
        readonly EditorTaskQueue tasks;
        public CreateUiPrefabRequestHandler(EditorTaskQueue tasks) { this.tasks = tasks; }
        public async Task Handle(HttpListenerContext context)
        {
            CreateUiPrefabRequest request = null;
            try
            {
                string json = await HttpRequestBodyReader.Read(context).ConfigureAwait(false);
                request = JsonUtility.FromJson<CreateUiPrefabRequest>(json);
                if (request == null) throw new Exception("Missing request");
                if (request.id == null || request.id.Length != 32 || !request.id.All(Uri.IsHexDigit))
                    throw new Exception("Invalid request ID");
                double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                if (double.IsNaN(request.deadline) || double.IsInfinity(request.deadline)
                    || request.deadline <= now || request.deadline > now + 60)
                    throw new Exception("Expired or invalid request deadline");
            }
            catch (HttpRequestBodyReader.ReadException e)
            {
                await EditorHttpResponse.Send(context, e.StatusCode, e.ResponseJson);
                return;
            }
            catch (Exception e)
            {
                var reply = new UiPrefabBridge.Reply {
                    id = request?.id ?? "", instance_id = EditorInstanceInfo.InstanceId,
                    ok = false, error = e.Message
                };
                await EditorHttpResponse.Send(context, 200, JsonUtility.ToJson(reply));
                return;
            }
            await tasks.EnqueueAndRespond(context, () => CreateUiPrefabOperation.Execute(request)).ConfigureAwait(false);
        }
    }
}
