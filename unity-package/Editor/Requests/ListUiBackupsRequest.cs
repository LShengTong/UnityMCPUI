using System;
namespace UnityMcp.Editor
{
    [Serializable] public sealed class ListUiBackupsRequest
    {
        public string id, asset_path;
        public double deadline;
        public int limit = 20, offset;
    }
}
