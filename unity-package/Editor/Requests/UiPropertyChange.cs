using System;

namespace UnityMcp.Editor
{
    [Serializable] public sealed class UiPropertyChange
    {
        public string index_path, property, value;
    }
}
