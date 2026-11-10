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
        static bool shuttingDown;
        static double nextStart;
        static string lastStartError;

        static EditorServerLifecycle()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
            EditorApplication.update += Update;
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
                lastStartError = null;
            }
            catch (Exception e)
            {
                Cleanup();
                nextStart = EditorApplication.timeSinceStartup + 2;
                if (lastStartError != e.Message)
                    Debug.LogError("Unity MCP HTTP startup failed; retrying every 2 seconds: " + e.Message);
                lastStartError = e.Message;
            }
        }
        static void Update()
        {
            if (shuttingDown) return;
            if (stopped)
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < nextStart) return;
                Start();
            }
            if (!stopped) tasks.Update();
        }
        static void Stop()
        {
            // beforeAssemblyReload/quitting must never start a listener again in this domain.
            shuttingDown = true;
            Cleanup();
        }
        static void Cleanup()
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
