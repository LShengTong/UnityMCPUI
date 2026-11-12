using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal sealed class CreateUiTestSceneRequestHandler : IEditorRequestHandler
    {
        readonly EditorTaskQueue tasks;

        public CreateUiTestSceneRequestHandler(EditorTaskQueue tasks)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        public Task Handle(HttpListenerContext context) =>
            HttpRequestUtility.Handle<CreateUiTestSceneRequest>(context, tasks, Execute, Error,
                request => RequestProtocolValidation.Validate(request));

        static string Error(string id, string message) => JsonUtility.ToJson(new CreateUiTestSceneReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message
        });

        internal static string Execute(CreateUiTestSceneRequest request)
        {
            var result = CreateUiTestSceneService.Execute(request.scene_path, request.prefab_path,
                request.reference_width, request.reference_height, request.deadline);
            return JsonUtility.ToJson(new CreateUiTestSceneReply {
                id = request.id, instance_id = EditorInstanceInfo.InstanceId,
                ok = result.ok, error = result.error, scene_path = result.scene_path,
                parent_path = result.parent_path, prefab_instance_path = result.prefab_instance_path,
                saved = result.saved
            });
        }
    }
}
