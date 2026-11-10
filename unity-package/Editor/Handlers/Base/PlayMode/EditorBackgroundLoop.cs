using System;
using System.Reflection;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    /// <summary>Wake the native editor loop without executing Unity work on a worker thread.</summary>
    [InitializeOnLoad]
    internal static class EditorBackgroundLoop
    {
        const string OwnedKey = "UnityMcp.BackgroundOverride";
        const string PreviousKey = "UnityMcp.PreviousRunInBackground";
        static Timer timer;
        static int stopped;
        static Action signalTick;

        static EditorBackgroundLoop()
        {
            // Unity exposes SignalTick as an internal, thread-safe native binding. Keep this
            // version-dependent adapter here; QueuePlayerLoopUpdate is NOT thread-safe.
            var method = typeof(EditorApplication).GetMethod("SignalTick", BindingFlags.Static | BindingFlags.NonPublic,
                null, Type.EmptyTypes, null);
            if (method != null)
            {
                try { signalTick = (Action)Delegate.CreateDelegate(typeof(Action), method); }
                catch (Exception error) { Debug.LogWarning("Unity MCP background wake unavailable: " + error.Message); }
            }
            if (signalTick != null) timer = new Timer(Wake, null, 0, 33);
            else Debug.LogWarning("Unity MCP: this Editor does not expose SignalTick; background updates may stall.");
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += () => { Stop(); Restore(); };
        }
        static void Wake(object unused)
        {
            if (Volatile.Read(ref stopped) != 0) return;
            try { signalTick(); }
            catch { Interlocked.Exchange(ref stopped, 1); }
        }
        static void Stop()
        {
            Interlocked.Exchange(ref stopped, 1);
            timer?.Dispose(); timer = null;
        }
        static void Update()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                if (!SessionState.GetBool(OwnedKey, false))
                {
                    SessionState.SetBool(PreviousKey, Application.runInBackground);
                    SessionState.SetBool(OwnedKey, true);
                }
                Application.runInBackground = true;
                if (EditorApplication.isPlaying && !EditorApplication.isPaused) EditorApplication.QueuePlayerLoopUpdate();
            }
            else Restore();
        }
        static void Restore()
        {
            if (!SessionState.GetBool(OwnedKey, false)) return;
            Application.runInBackground = SessionState.GetBool(PreviousKey, false);
            SessionState.EraseBool(OwnedKey);
            SessionState.EraseBool(PreviousKey);
        }
    }
}
