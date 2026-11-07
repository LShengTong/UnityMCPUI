using System;
namespace UnityMcp.Editor
{
    [Serializable] public sealed class ImportUiImageRequest
    {
        public string id, asset_path, image_base64;
        public double deadline;
        // Border order: left, bottom, right, top (source pixels).
        public int[] border = new int[4];
        public float pixels_per_unit = 100;
        public string filter_mode = "bilinear";
    }
}
