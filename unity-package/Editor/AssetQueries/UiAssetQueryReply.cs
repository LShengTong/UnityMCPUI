using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace UnityMcp.Editor
{
    [Serializable] internal class UiAssetInspectionResult
    {
        public int index;
        public bool ok;
        public string error, asset_type;
        public UiAssetQueryUtility.AssetReference asset;
    }
    [Serializable] internal sealed class UiAssetSearchResult
    {
        public int index, total, next_offset = -1;
        public bool ok;
        public string error, asset_type;
        public UiAssetQueryUtility.AssetReference[] assets;
    }
    internal static class UiAssetQueryReply
    {
        [Serializable] sealed class Header { public string id, instance_id, error; public bool ok; }
        public static string Error(string id, string message) => JsonUtility.ToJson(new Header {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message
        });
        public static string Batch<T>(string id, IEnumerable<T> results)
        {
            string header = JsonUtility.ToJson(new Header { id = id, instance_id = EditorInstanceInfo.InstanceId, ok = true });
            // Serialize each result at its concrete runtime type: JsonUtility cannot preserve
            // derived fields inside a base-typed array. Every fragment is serializer output.
            return header.Substring(0, header.Length - 1) + ",\"results\":[" +
                string.Join(",", results.Select(result => JsonUtility.ToJson(result))) + "]}";
        }
    }
}
