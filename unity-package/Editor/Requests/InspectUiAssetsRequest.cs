using System;
namespace UnityMcp.Editor
{
    [Serializable] public sealed class UiAssetInspection
    {
        public string asset_path, asset_type, local_id = "", text = "";
        public bool search_fallbacks = true;
        public int max_nodes = 200;
    }
    [Serializable] public sealed class InspectUiAssetsRequest
    {
        public string id;
        public double deadline;
        public UiAssetInspection[] assets;
    }
}
