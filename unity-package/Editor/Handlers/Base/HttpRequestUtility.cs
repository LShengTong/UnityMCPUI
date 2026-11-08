using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal interface IEditorRequest
    {
        string Id { get; }
        double Deadline { get; }
    }

    [Serializable] internal sealed class ReplyHeader
    {
        public string id, instance_id, error;
        public bool ok;
    }

    internal static class HttpRequestUtility
    {
        public static async Task<T> Read<T>(HttpListenerContext context, int bodyLimit = 256 * 1024) where T : class
        {
            string json = await HttpRequestBodyReader.Read(context, bodyLimit).ConfigureAwait(false);
            return JsonUtility.FromJson<T>(json) ?? throw new Exception("Missing request");
        }

        // Shared orchestration; handlers own their request type, validation and execution.
        public static async Task Handle<T>(HttpListenerContext context, EditorTaskQueue tasks,
            Func<T, string> execute, Func<string, string, string> error,
            Action<T> validate = null, int bodyLimit = 256 * 1024) where T : class, IEditorRequest
        {
            T request = null;
            try
            {
                request = await Read<T>(context, bodyLimit).ConfigureAwait(false);
                if (request.Id == null || request.Id.Length != 32 || !request.Id.All(Uri.IsHexDigit))
                    throw new Exception("Invalid request ID");
                double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                if (double.IsNaN(request.Deadline) || double.IsInfinity(request.Deadline)
                    || request.Deadline <= now || request.Deadline > now + 60)
                    throw new Exception("Expired or invalid request deadline");
                validate?.Invoke(request);
            }
            catch (HttpRequestBodyReader.ReadException e)
            {
                await EditorHttpResponse.Send(context, e.StatusCode, e.ResponseJson).ConfigureAwait(false);
                return;
            }
            catch (Exception e)
            {
                await EditorHttpResponse.Send(context, 200, error(request?.Id, e.Message)).ConfigureAwait(false);
                return;
            }
            await tasks.EnqueueAndRespond(context, () => execute(request)).ConfigureAwait(false);
        }
    }
}
