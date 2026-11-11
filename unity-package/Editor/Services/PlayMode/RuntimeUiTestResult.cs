namespace UnityMcp.Editor
{
    internal sealed class RuntimeUiTestResult
    {
        public bool ok;
        public string error;
        public string session_id = "";
        public string test_instance_id = "";
        public bool passed = false;
        public int failed_step_index = -1;
        public UiTestStepResult[] steps = new UiTestStepResult[] {  };
        public string interaction_mode = "callback";
        public bool saved_scene = false;
    }
}
