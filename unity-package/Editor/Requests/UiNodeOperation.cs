using System;

namespace UnityMcp.Editor
{
    [Serializable] public sealed class UiNodeOperation
    {
        public string action, index_path, parent_path;
        public UiPrefabNode node;
        public int sibling_index = -1;
        public bool world_position_stays;
    }
}
