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
            return JsonUtility.ToJson(await RuntimeUiTestRunner.Run(request));
        }
    }
}
