using System.Net;
using System.Threading.Tasks;

namespace UnityMcp.Editor
{
    /// <summary>Authenticates requests and dispatches each endpoint to its own handler.</summary>
    internal sealed class EditorHttpRequestHandler
    {
        readonly HealthOperation health = new HealthOperation();
        readonly ProjectInfoOperation projectInfo = new ProjectInfoOperation();
        readonly CreateUiPrefabRequestHandler createUiPrefab;
        readonly PreviewUiPrefabRequestHandler previewUiPrefab;
        readonly ImportUiImageRequestHandler importUiImage;
        readonly BatchEditUiPrefabRequestHandler batchEditUiPrefab;
        readonly FindUiAssetsRequestHandler findUiAssets;
        readonly InspectUiAssetsRequestHandler inspectUiAssets;
        readonly ListUiBackupsRequestHandler listUiBackups;
        readonly RestoreUiPrefabRequestHandler restoreUiPrefab;

        // Construct on the main thread so endpoint constructors can capture Unity API values.
        internal EditorHttpRequestHandler(EditorTaskQueue tasks)
        {
            createUiPrefab = new CreateUiPrefabRequestHandler(tasks);
            previewUiPrefab = new PreviewUiPrefabRequestHandler(tasks);
            importUiImage = new ImportUiImageRequestHandler(tasks);
            batchEditUiPrefab = new BatchEditUiPrefabRequestHandler(tasks);
            findUiAssets = new FindUiAssetsRequestHandler(tasks);
            inspectUiAssets = new InspectUiAssetsRequestHandler(tasks);
            listUiBackups = new ListUiBackupsRequestHandler(tasks);
            restoreUiPrefab = new RestoreUiPrefabRequestHandler(tasks);
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
            string path = context.Request.Url.AbsolutePath;
            string method = context.Request.HttpMethod;
            if (path == "/ui/backups/list" && method == "POST") return listUiBackups.Handle(context);
            if (path == "/ui/backups/restore" && method == "POST") return restoreUiPrefab.Handle(context);
            if (path == "/ui/assets/find" && method == "POST") return findUiAssets.Handle(context);
            if (path == "/ui/assets/inspect" && method == "POST") return inspectUiAssets.Handle(context);
            if (path == "/ui/prefabs/batch" && method == "POST") return batchEditUiPrefab.Handle(context);
            if (path == "/ui/images/import" && method == "POST") return importUiImage.Handle(context);
            if (path == "/ui/prefabs/preview" && method == "POST") return previewUiPrefab.Handle(context);
            if (path == "/health" && method == "GET") return EditorHttpResponse.Send(context, 200, health.Execute());
            if (path == "/project/info" && method == "GET") return EditorHttpResponse.Send(context, 200, projectInfo.Execute());
            if (path == "/ui/prefabs" && method == "POST") return createUiPrefab.Handle(context);
            return EditorHttpResponse.Send(context, 404, "{\"error\":\"Unknown endpoint\"}");
        }
    }
}
