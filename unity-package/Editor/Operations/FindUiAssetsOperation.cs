using System;
using System.Collections.Generic;
namespace UnityMcp.Editor
{
    internal static class FindUiAssetsOperation
    {
        public static string Error(string id, string message) => UiAssetQueryReply.Error(id, message);
        public static string Execute(FindUiAssetsRequest request)
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
            return UiAssetQueryReply.Batch(request.id, results);
        }
    }
}
