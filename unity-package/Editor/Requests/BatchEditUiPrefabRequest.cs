using System;
namespace UnityMcp.Editor
{
    [Serializable] public sealed class BatchEditUiPrefabRequest
    {
        public string id, asset_path, expected_version;
        public double deadline;
        public UiBatchOperation[] operations;
    }
}
