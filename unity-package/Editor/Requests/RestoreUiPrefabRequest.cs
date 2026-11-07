using System;
namespace UnityMcp.Editor
{
    [Serializable] public sealed class RestoreUiPrefabRequest
    {
        public string id, backup_id, expected_version;
        public double deadline;
    }
}
