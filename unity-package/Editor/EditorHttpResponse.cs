using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace UnityMcp.Editor
{
    internal static class EditorHttpResponse
    {
        public static async Task Send(HttpListenerContext context, int status, string json)
        {
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                context.Response.Headers["X-Unity-Instance"] = EditorInstanceInfo.InstanceId;
                context.Response.StatusCode = status;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
            }
            catch (IOException) { }
            catch (HttpListenerException) { }
            catch (ObjectDisposedException) { }
            finally { context.Response.Close(); }
        }
    }
}
