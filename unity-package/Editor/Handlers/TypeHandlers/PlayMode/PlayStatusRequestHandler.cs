using System.Net;
using System.Threading.Tasks;
namespace UnityMcp.Editor
{
    internal sealed class PlayStatusRequestHandler : IEditorRequestHandler
    {
        public PlayStatusRequestHandler() { PlayModeSession.Initialize(); }
        public Task Handle(HttpListenerContext context) => EditorHttpResponse.Send(context, 200, PlayModeSession.Json);
    }
}
