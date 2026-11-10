using System;
using System.Net;
using System.Threading.Tasks;
using System.IO;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class ProjectInfoRequestHandler : IEditorRequestHandler
    {
        readonly string json;
        // Construct on the main thread; Execute returns the snapshot without Unity API calls.
        public ProjectInfoRequestHandler()
        {
            string projectPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            json = JsonUtility.ToJson(new ProjectInfoReply {
                protocol = EditorInstanceInfo.ProtocolVersion,
                project_path = projectPath,
                instance_id = EditorInstanceInfo.InstanceId,
                project_name = Path.GetFileName(projectPath),
                unity_version = Application.unityVersion
            });
        }
        public Task Handle(HttpListenerContext context) => EditorHttpResponse.Send(context, 200, json);
    }
}
