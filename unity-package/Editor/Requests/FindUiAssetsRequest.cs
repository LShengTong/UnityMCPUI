using System;
namespace UnityMcp.Editor
{
    [Serializable] public sealed class UiAssetSearch
    {
        public string asset_type, search = "", folder = "Assets";
        public int limit = 50, offset;
    }
    [Serializable] public sealed class FindUiAssetsRequest
    {
        public string id;
        public double deadline;
        public UiAssetSearch[] queries;
    }
}
