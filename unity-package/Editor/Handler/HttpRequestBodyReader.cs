using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace UnityMcp.Editor
{
    /// <summary>Reads a request body once with common size and read-timeout limits.</summary>
    internal static class HttpRequestBodyReader
    {
        internal sealed class ReadException : Exception
        {
            public readonly int StatusCode;
            public readonly string ResponseJson;
            public ReadException(int statusCode, string responseJson)
            {
                StatusCode = statusCode;
                ResponseJson = responseJson;
            }
        }
        const int Limit = 256 * 1024;
        public static async Task<string> Read(HttpListenerContext context, int limit = Limit)
        {
            if (context.Request.ContentLength64 <= 0 || context.Request.ContentLength64 > limit)
                throw new ReadException(413, "{\"error\":\"Invalid request size\"}");
            using (var memory = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                while (true)
                {
                    var readTask = context.Request.InputStream.ReadAsync(buffer, 0, buffer.Length);
                    if (await Task.WhenAny(readTask, Task.Delay(5000)).ConfigureAwait(false) != readTask)
                        throw new ReadException(408, "{\"error\":\"Body read timeout\"}");
                    int read = await readTask.ConfigureAwait(false);
                    if (read == 0) break;
                    if (memory.Length + read > limit)
                        throw new ReadException(413, "{\"error\":\"Request too large\"}");
                    memory.Write(buffer, 0, read);
                }
                return Encoding.UTF8.GetString(memory.ToArray());
            }
        }
    }
}
