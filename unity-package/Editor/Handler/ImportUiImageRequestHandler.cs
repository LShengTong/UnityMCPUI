using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class ImportUiImageRequestHandler
    {
        readonly EditorTaskQueue tasks;
        public ImportUiImageRequestHandler(EditorTaskQueue tasks) { this.tasks = tasks; }

        public async Task Handle(HttpListenerContext context)
        {
            ImportUiImageRequest request = null;
            try
            {
                string json = await HttpRequestBodyReader.Read(context, 12 * 1024 * 1024).ConfigureAwait(false);
                request = JsonUtility.FromJson<ImportUiImageRequest>(json);
                if (request == null) throw new Exception("Missing request");
                if (request.id == null || request.id.Length != 32 || !request.id.All(Uri.IsHexDigit))
                    throw new Exception("Invalid request ID");
                double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                if (double.IsNaN(request.deadline) || double.IsInfinity(request.deadline)
                    || request.deadline <= now || request.deadline > now + 60)
                    throw new Exception("Expired or invalid request deadline");
                if (string.IsNullOrEmpty(request.image_base64) || request.image_base64.Length > 11184812)
                    throw new Exception("Image must be at most 8 MiB");
            }
            catch (HttpRequestBodyReader.ReadException e)
            {
                await EditorHttpResponse.Send(context, e.StatusCode, e.ResponseJson);
                return;
            }
            catch (Exception e)
            {
                await EditorHttpResponse.Send(context, 200, ImportUiImageOperation.Error(request?.id, e.Message));
                return;
            }
            await tasks.EnqueueAndRespond(context, () => ImportUiImageOperation.Execute(request)).ConfigureAwait(false);
        }
    }
}
