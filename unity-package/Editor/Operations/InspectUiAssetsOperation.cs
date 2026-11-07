using System;
using System.Collections.Generic;
namespace UnityMcp.Editor
{
    internal static class InspectUiAssetsOperation
    {
        public static string Error(string id, string message) => UiAssetQueryReply.Error(id, message);
        public static string Execute(InspectUiAssetsRequest request)
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
            return UiAssetQueryReply.Batch(request.id, results);
        }
    }
}
