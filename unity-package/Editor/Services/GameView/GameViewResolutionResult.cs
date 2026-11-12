namespace UnityMcp.Editor
{
    internal sealed class GameViewResolutionResult
    {
        public bool ok;
        public string error;
        public string group = "";
        public int selected_index = -1;
        public int actual_width;
        public int actual_height;
        public bool is_playing;
        public bool can_restore;
        public GameViewPreset[] presets = new GameViewPreset[0];
    }
}
