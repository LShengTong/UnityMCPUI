namespace UnityMcp.Editor
{
    internal sealed class RuntimeUiInspectionResult
    {
        public bool ok;
        public string error;
        public string session_id = "";
        public RuntimeUiNode[] nodes = new RuntimeUiNode[] {  };
        public bool truncated = false;
    }
}
