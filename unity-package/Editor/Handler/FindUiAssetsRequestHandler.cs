using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class FindUiAssetsRequestHandler
    {
        readonly EditorTaskQueue tasks;
        public FindUiAssetsRequestHandler(EditorTaskQueue tasks) { this.tasks = tasks; }

        public async Task Handle(HttpListenerContext context)
        {
            FindUiAssetsRequest request = null;
            try
            {
                string json = await HttpRequestBodyReader.Read(context).ConfigureAwait(false);
                request = JsonUtility.FromJson<FindUiAssetsRequest>(json);
                if (request == null) throw new Exception("Missing request");
                if (request.id == null || request.id.Length != 32 || !request.id.All(Uri.IsHexDigit))
                    throw new Exception("Invalid request ID");
                double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                if (double.IsNaN(request.deadline) || double.IsInfinity(request.deadline)
                    || request.deadline <= now || request.deadline > now + 60)
                    throw new Exception("Expired or invalid request deadline");
                if (request.queries == null || request.queries.Length < 1 || request.queries.Length > 16)
                    throw new Exception("Provide 1..16 queries");
            }
            catch (HttpRequestBodyReader.ReadException e)
            {
                await EditorHttpResponse.Send(context, e.StatusCode, e.ResponseJson);
                return;
            }
            catch (Exception e)
            {
                await EditorHttpResponse.Send(context, 200, FindUiAssetsOperation.Error(request?.id, e.Message));
                return;
            }
            await tasks.EnqueueAndRespond(context, () => FindUiAssetsOperation.Execute(request)).ConfigureAwait(false);
        }
    }
}
