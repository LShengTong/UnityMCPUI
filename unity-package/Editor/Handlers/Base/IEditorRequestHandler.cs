using System.Net;
using System.Threading.Tasks;

namespace UnityMcp.Editor
{
    internal interface IEditorRequestHandler
    {
        Task Handle(HttpListenerContext context);
    }
}
