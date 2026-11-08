using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace UnityMcp.Editor
{
    /// <summary>Serializes array items without losing derived fields.</summary>
    internal static class JsonReplySerializer
    {
        public static string Array<T>(IEnumerable<T> items)
        {
            // Serialize each item at its runtime type; base-typed arrays lose derived fields.
            return "[" + string.Join(",", items.Select(item => JsonUtility.ToJson(item))) + "]";
        }
    }
}
