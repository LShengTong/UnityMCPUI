namespace UnityMcp.Editor
{
    internal sealed class BatchEditUiPrefabResult
    {
        public bool ok;
        public string error;
        public string asset_path;
        public string version;
        public string phase;
        public string failed_action;
        public string backup_id;
        public string backup_warning;
        public bool saved = false;
        public int executed_operations = 0;
        public int changed_properties = 0;
        public int node_operations = 0;
        public int added_components = 0;
        public int failed_operation_index = -1;
        public UiCreatedNode[] created_nodes = new UiCreatedNode[] {  };
        public int added_scripts = 0;
        public int assigned_references = 0;
    }
}
