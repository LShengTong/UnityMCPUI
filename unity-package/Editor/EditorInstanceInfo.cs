using System;

namespace UnityMcp.Editor
{
    /// <summary>Identity and credentials for this bridge lifetime.</summary>
    internal static class EditorInstanceInfo
    {
        public const int ProtocolVersion = 2;
        public static readonly string InstanceId = Guid.NewGuid().ToString("N");
        public static readonly string Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
    }
}
