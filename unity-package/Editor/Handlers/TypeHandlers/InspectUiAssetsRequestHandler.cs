using System;
using System.Net;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class InspectUiAssetsRequestHandler : IEditorRequestHandler
    {
        readonly EditorTaskQueue tasks;

        public InspectUiAssetsRequestHandler(EditorTaskQueue tasks)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        public Task Handle(HttpListenerContext context) =>
            HttpRequestUtility.Handle<InspectUiAssetsRequest>(context, tasks, Execute, Error, Validate);

        static void Validate(InspectUiAssetsRequest request) => RequestProtocolValidation.Validate(request);

        static string Error(string id, string message) => JsonUtility.ToJson(new ReplyHeader {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message
        });
        static string Execute(InspectUiAssetsRequest request)
        {
            var results = new List<UiAssetInspectionResult>();
            for (int i = 0; i < request.assets.Length; i++)
            {
                var query = request.assets[i];
                try
                {
                    UiAssetQueryUtility.CheckDeadline(request.deadline);
                    if (query == null) throw new Exception("Missing query");
                    results.Add(UiAssetQueryRegistry.Get(query.asset_type).Inspect(query, i, request.deadline));
                }
                catch (Exception e) { results.Add(new UiAssetInspectionResult { index = i, asset_type = query?.asset_type, error = e.Message }); }
            }
            string header = JsonUtility.ToJson(new ReplyHeader {
                id = request.id, instance_id = EditorInstanceInfo.InstanceId, ok = true
            });
            return header.Substring(0, header.Length - 1) + ",\"results\":" + JsonReplySerializer.Array(results) + "}";
        }
    }
}
