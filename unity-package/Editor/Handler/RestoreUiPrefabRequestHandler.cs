using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class RestoreUiPrefabRequestHandler
    {
        readonly EditorTaskQueue tasks;
        public RestoreUiPrefabRequestHandler(EditorTaskQueue tasks) { this.tasks = tasks; }

        public async Task Handle(HttpListenerContext context)
        {
            RestoreUiPrefabRequest request = null;
            try
            {
                string json = await HttpRequestBodyReader.Read(context).ConfigureAwait(false);
                request = JsonUtility.FromJson<RestoreUiPrefabRequest>(json);
                if (request == null) throw new Exception("Missing request");
                if (request.id == null || request.id.Length != 32 || !request.id.All(Uri.IsHexDigit))
                    throw new Exception("Invalid request ID");
                double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                if (double.IsNaN(request.deadline) || double.IsInfinity(request.deadline)
                    || request.deadline <= now || request.deadline > now + 60)
                    throw new Exception("Expired or invalid request deadline");
                if (request.backup_id == null || request.backup_id.Length != 32 || !request.backup_id.All(Uri.IsHexDigit))
                    throw new Exception("Invalid backup_id");
                if (request.expected_version == null || request.expected_version.Length != 64 || !request.expected_version.All(Uri.IsHexDigit))
                    throw new Exception("Invalid expected_version");
            }
            catch (HttpRequestBodyReader.ReadException e)
            {
                await EditorHttpResponse.Send(context, e.StatusCode, e.ResponseJson);
                return;
            }
            catch (Exception e)
            {
                await EditorHttpResponse.Send(context, 200, RestoreUiPrefabOperation.Error(request?.id, e.Message));
                return;
            }
            await tasks.EnqueueAndRespond(context, () => RestoreUiPrefabOperation.Execute(request)).ConfigureAwait(false);
        }
    }
}
