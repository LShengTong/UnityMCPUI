using System;

namespace UnityMcp.Editor
{
    [Serializable] public sealed class CreateUiPrefabRequest
    {
        public string id, token, output_path;
        public double deadline;
        public UiPrefabSpec spec;
    }
}
