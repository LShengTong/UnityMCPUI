using System;
using System.Net;
using System.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class FindUiAssetsRequestHandler : IEditorRequestHandler
    {
        readonly EditorTaskQueue tasks;

        public FindUiAssetsRequestHandler(EditorTaskQueue tasks)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        public Task Handle(HttpListenerContext context) =>
            HttpRequestUtility.Handle<FindUiAssetsRequest>(context, tasks, Execute, Error, Validate);

        static void Validate(FindUiAssetsRequest request) => RequestProtocolValidation.Validate(request);

        static string Error(string id, string message) => JsonUtility.ToJson(new ReplyHeader {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message
        });
        static string Execute(FindUiAssetsRequest request)
        {
            var results = new List<UiAssetSearchResult>();
            for (int i = 0; i < request.queries.Length; i++)
            {
                var query = request.queries[i];
                try
                {
                    UiAssetQueryUtility.CheckDeadline(request.deadline);
                    if (query == null) throw new Exception("Missing query");
                    results.Add(UiAssetQueryRegistry.Get(query.asset_type).Find(query, i, request.deadline));
                }
                catch (Exception e) { results.Add(new UiAssetSearchResult { index = i, asset_type = query?.asset_type, error = e.Message }); }
            }
            string header = JsonUtility.ToJson(new ReplyHeader {
                id = request.id, instance_id = EditorInstanceInfo.InstanceId, ok = true
            });
            return header.Substring(0, header.Length - 1) + ",\"results\":" + JsonReplySerializer.Array(results) + "}";
        }
    }
}
