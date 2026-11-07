using System;
using System.IO;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class ProjectInfoOperation
    {
        [Serializable] class Response
        {
            public int protocol;
            public string project_path, instance_id, project_name, unity_version;
            public string source = "editor";
        }
        readonly string json;
        // Construct on the main thread; Execute returns the snapshot without Unity API calls.
        public ProjectInfoOperation()
        {
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            json = JsonUtility.ToJson(new Response {
                protocol = EditorInstanceInfo.ProtocolVersion,
                project_path = projectPath,
                instance_id = EditorInstanceInfo.InstanceId,
                project_name = Path.GetFileName(projectPath),
                unity_version = Application.unityVersion
            });
        }
        public string Execute() => json;
    }
}
