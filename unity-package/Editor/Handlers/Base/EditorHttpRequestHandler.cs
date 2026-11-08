using System.Net;
using System.Threading.Tasks;

namespace UnityMcp.Editor
{
    /// <summary>Authenticates requests and dispatches each endpoint to its own handler.</summary>
    internal sealed class EditorHttpRequestHandler
    {
        readonly EditorRequestRegistry requests;

        // Construct on the main thread so endpoint constructors can capture Unity API values.
        internal EditorHttpRequestHandler(EditorTaskQueue tasks)
        {
            requests = new EditorRequestRegistry(tasks);
        }
        internal void OnContextReceived(HttpListenerContext context)
        {
            _ = HandleSafely(context);
        }
        async Task HandleSafely(HttpListenerContext context)
        {
            try { await Handle(context).ConfigureAwait(false); }
            catch { try { context.Response.Close(); } catch { } }
        }
        Task Handle(HttpListenerContext context)
        {
            if (context.Request.Headers["Origin"] != null || context.Request.Headers["Authorization"] != "Bearer " + EditorInstanceInfo.Token)
                return EditorHttpResponse.Send(context, 401, "{\"error\":\"Unauthorized\"}");
            if (requests.TryResolve(context.Request.HttpMethod, context.Request.Url.AbsolutePath, out var type))
                return requests.Get(type).Handle(context);
            return EditorHttpResponse.Send(context, 404, "{\"error\":\"Unknown endpoint\"}");
        }
    }
}
