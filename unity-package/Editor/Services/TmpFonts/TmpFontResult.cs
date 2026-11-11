namespace UnityMcp.Editor
{
    internal sealed class TmpFontResult
    {
        public bool ok;
        public string error;
        public string asset_path = "";
        public string guid = "";
        public string population_mode = "";
        public int character_count = 0;
        public bool all_characters_present = false;
        public string[] missing_codepoints = new string[] {  };
    }
}
