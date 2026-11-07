using System;
namespace UnityMcp.Editor
{
    [Serializable] public sealed class PreviewUiPrefabRequest
    {
        public string id, asset_path, background = "#202020";
        public double deadline;
        public int width = 1280, height = 720;
    }
}
