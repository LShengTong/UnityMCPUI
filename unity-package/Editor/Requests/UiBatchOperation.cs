using System;
namespace UnityMcp.Editor
{
    [Serializable] public sealed class UiBatchOperation
    {
        public string action, index_path, parent_path, property, value;
        public UiPrefabNode node;
        public string component, viewport_path, content_path;
        public bool horizontal, vertical = true;
        public int sibling_index = -1;
        public bool world_position_stays;
    }
}
