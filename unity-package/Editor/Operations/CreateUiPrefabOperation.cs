using System;
using UnityEngine;

namespace UnityMcp.Editor
{
    /// <summary>Concrete UI creation operation; execute on the Unity main thread.</summary>
    internal static class CreateUiPrefabOperation
    {
        public static string Execute(CreateUiPrefabRequest request)
        {
            UiPrefabBridge.Reply reply;
            try
            {
                // Recheck at execution time: a valid request may have expired in the queue.
                double now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
                if (request.deadline <= now) throw new Exception("Request expired before execution");
                reply = UiPrefabBridge.Create(request);
            }
            catch (Exception e)
            {
                reply = new UiPrefabBridge.Reply { id = request.id, ok = false, error = e.Message };
            }
            reply.instance_id = EditorInstanceInfo.InstanceId;
            return JsonUtility.ToJson(reply);
        }
    }
}
