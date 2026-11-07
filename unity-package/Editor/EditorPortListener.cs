using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace UnityMcp.Editor
{
    /// <summary>Owns the loopback HTTP listener and its port binding.</summary>
    internal sealed class EditorPortListener
    {
        public HttpListener Listener { get; private set; }
        public int Port { get; private set; }
        // Raised on a background thread. One subscriber should own and close each response.
        public event Action<HttpListenerContext> ContextReceived;

        public void Open()
        {
            if (Listener != null) throw new InvalidOperationException("Listener is already open");
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                int port;
                try
                {
                    probe.Start();
                    port = ((IPEndPoint)probe.LocalEndpoint).Port;
                }
                finally { probe.Stop(); }

                // The probe must release the port before HTTP can bind it.
                var candidate = new HttpListener();
                try
                {
                    candidate.Prefixes.Add("http://127.0.0.1:" + port + "/");
                    candidate.Start();
                    Listener = candidate;
                    Port = port;
                    _ = Task.Run(() => AcceptLoop(candidate));
                    return;
                }
                catch (HttpListenerException)
                {
                    candidate.Close();
                    Listener = null;
                    Port = 0;
                    if (attempt == 4) throw;
                }
                catch
                {
                    candidate.Close();
                    Listener = null;
                    Port = 0;
                    throw;
                }
            }
        }

        async Task AcceptLoop(HttpListener listener)
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await listener.GetContextAsync().ConfigureAwait(false); }
                catch (HttpListenerException) { break; }
                catch (ObjectDisposedException) { break; }
                try
                {
                    var subscriber = ContextReceived;
                    if (subscriber == null)
                    {
                        context.Response.StatusCode = 503;
                        context.Response.Close();
                    }
                    else subscriber(context);
                }
                catch
                {
                    // A failed subscriber must not terminate the receive loop.
                    try { context.Response.StatusCode = 500; context.Response.Close(); } catch { }
                }
            }
        }

        public void Close()
        {
            var listener = Listener;
            Listener = null;
            Port = 0;
            listener?.Close();
        }
    }
}
