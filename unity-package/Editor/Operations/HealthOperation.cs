using System;
using System.IO;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class HealthOperation
    {
        [Serializable] class Response
        {
            public int protocol;
            public string project_path, instance_id;
        }
        readonly string json;
        // Construct on the main thread; Execute returns the snapshot without Unity API calls.
        public HealthOperation()
        {
            json = JsonUtility.ToJson(new Response {
                protocol = EditorInstanceInfo.ProtocolVersion,
                project_path = Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                instance_id = EditorInstanceInfo.InstanceId
            });
        }
        public string Execute() => json;
    }
}
