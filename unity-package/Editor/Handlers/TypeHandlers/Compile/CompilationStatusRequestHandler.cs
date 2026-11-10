using System.Net;
using System.Threading.Tasks;

namespace UnityMcp.Editor
{
    internal sealed class CompilationStatusRequestHandler : IEditorRequestHandler
    {
        // Force initialization on the main thread; HTTP threads only read the snapshot.
        public CompilationStatusRequestHandler() { _ = EditorCompilationState.Json; }
        public Task Handle(HttpListenerContext context) => EditorHttpResponse.Send(context, 200, EditorCompilationState.Json);
    }
}
