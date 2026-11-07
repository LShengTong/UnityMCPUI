using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class PreviewUiPrefabRequestHandler
    {
        readonly EditorTaskQueue tasks;
        public PreviewUiPrefabRequestHandler(EditorTaskQueue tasks) { this.tasks = tasks; }

        public async Task Handle(HttpListenerContext context)
        {
            PreviewUiPrefabRequest request = null;
            try
            {
                string json = await HttpRequestBodyReader.Read(context).ConfigureAwait(false);
                request = JsonUtility.FromJson<PreviewUiPrefabRequest>(json);
                if (request == null) throw new Exception("Missing request");
                if (request.id == null || request.id.Length != 32 || !request.id.All(Uri.IsHexDigit))
                    throw new Exception("Invalid request ID");
                double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                if (double.IsNaN(request.deadline) || double.IsInfinity(request.deadline)
                    || request.deadline <= now || request.deadline > now + 60)
                    throw new Exception("Expired or invalid request deadline");
                if (request.width < 64 || request.width > 2048 || request.height < 64 || request.height > 2048)
                    throw new Exception("Image dimensions must be 64..2048");
            }
            catch (HttpRequestBodyReader.ReadException e)
            {
                await EditorHttpResponse.Send(context, e.StatusCode, e.ResponseJson);
                return;
            }
            catch (Exception e)
            {
                await EditorHttpResponse.Send(context, 200, PreviewUiPrefabOperation.Error(request?.id, e.Message));
                return;
            }
            await tasks.EnqueueAndRespond(context, () => PreviewUiPrefabOperation.Execute(request)).ConfigureAwait(false);
        }
    }
}
