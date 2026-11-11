namespace UnityMcp.Editor
{
    internal sealed class PlayModeStatus
    {
        public bool is_playing;
        public bool is_changing;
        public bool is_paused;
        public string session_id;
        public int frame_count = 0;
        public bool test_running = false;
        public PlayTestInstanceInfo[] test_instances = new PlayTestInstanceInfo[] {  };
        public string test_mode = "current";
        public string test_scene_path = "";
        public string test_parent_path = "";
        public bool scene_restore_pending = false;
        public string scene_restore_error = "";
        public string test_scene_error = "";
    }
}
