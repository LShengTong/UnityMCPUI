using System;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    /// <summary>Owns bridge startup, request dispatch, main-thread scheduling and shutdown.</summary>
    [InitializeOnLoad]
    internal static class EditorServerLifecycle
    {
        static EditorPortListener portListener;
        static EditorTaskQueue tasks;
        static EditorHttpRequestHandler handler;
        static volatile bool stopped = true;

        static EditorServerLifecycle()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            EditorApplication.update += Update;
            EditorApplication.delayCall += Start;
        }
        static void Start()
        {
            if (!stopped) return;
            try
            {
                tasks = new EditorTaskQueue();
                handler = new EditorHttpRequestHandler(tasks);
                portListener = new EditorPortListener();
                portListener.ContextReceived += handler.OnContextReceived;
                stopped = false;
                portListener.Open();
                EditorDiscoveryFile.Write(portListener.Port);
            }
            catch (Exception e) { Stop(); Debug.LogError("Unity MCP HTTP startup failed: " + e.Message); }
        }
        static void Update()
        {
            if (!stopped) tasks.Update();
        }
        static void Stop()
        {
            stopped = true;
            if (portListener != null && handler != null) portListener.ContextReceived -= handler.OnContextReceived;
            tasks?.Stop();
            try { portListener?.Close(); } catch { }
            portListener = null;
            handler = null;
            tasks = null;
            try { EditorDiscoveryFile.DeleteOwned(); } catch { }
        }
    }
}
