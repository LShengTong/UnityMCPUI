using System;
using System.Net;
using System.Threading.Tasks;
using UnityEngine;
namespace UnityMcp.Editor
{
    internal sealed class RunUiTestRequestHandler : IEditorRequestHandler
    {
        public Task Handle(HttpListenerContext context) => PlayRequestQueue.Handle<RunUiTestRequest>(context,
            request => RequestProtocolValidation.Validate(request), Execute, Error);
        static string Error(string id, string message) => JsonUtility.ToJson(new RunUiTestReply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message });
        internal static async Task<string> Execute(RunUiTestRequest request)
        {
            var result = await RuntimeUiTestRunner.Run(request.deadline, request.test_instance_id, request.steps);
            return JsonUtility.ToJson(new RunUiTestReply {
                id = request.id, instance_id = EditorInstanceInfo.InstanceId,
                ok = result.ok,
                error = result.error,
                session_id = result.session_id,
                test_instance_id = result.test_instance_id,
                passed = result.passed,
                failed_step_index = result.failed_step_index,
                steps = result.steps,
                interaction_mode = result.interaction_mode,
                saved_scene = result.saved_scene
            });
        }
    }
}
