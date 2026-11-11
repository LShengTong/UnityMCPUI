using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace UnityMcp.Editor
{
    [InitializeOnLoad]
    internal static class EditorCompilationState
    {
        const string Key = "UnityMcp.CompilationState";
        static CompilationStatusReply state;
        static volatile string json;
        public static string Json => json;

        static EditorCompilationState()
        {
            try { state = JsonUtility.FromJson<CompilationStatusReply>(SessionState.GetString(Key, "")); }
            catch { state = null; }
            if (state == null) state = new CompilationStatusReply { errors = Array.Empty<CompilationDiagnostic>(), compilation_revision = "", refresh_id = "" };
            // A completed domain reload may have interrupted the refreshing caller.
            if (!EditorApplication.isCompiling && !EditorApplication.isUpdating) state.refresh_pending = false;
            CompilationPipeline.compilationStarted += _ => {
                state.compilation_revision = Guid.NewGuid().ToString("N");
                state.has_compilation_result = false;
                state.errors = Array.Empty<CompilationDiagnostic>();
                state.errors_truncated = false;
                Publish();
            };
            CompilationPipeline.assemblyCompilationFinished += (assembly, messages) => {
                var errors = new List<CompilationDiagnostic>(state.errors ?? Array.Empty<CompilationDiagnostic>());
                foreach (var message in messages)
                    if (message.type == CompilerMessageType.Error)
                    {
                        if (errors.Count >= 200) { state.errors_truncated = true; continue; }
                        errors.Add(new CompilationDiagnostic { file = message.file ?? "", line = Math.Max(0, message.line),
                            column = Math.Max(0, message.column), message = message.message ?? "", assembly = assembly ?? "" });
                    }
                state.errors = errors.ToArray();
                Publish();
            };
            CompilationPipeline.compilationFinished += _ => { state.has_compilation_result = true; state.refresh_pending = false; Publish(); };
            EditorApplication.update += Update;
            Publish();
        }
        public static string RequestRefresh()
        {
            state.refresh_id = Guid.NewGuid().ToString("N");
            state.refresh_pending = true;
            Publish();
            return state.refresh_id;
        }
        public static void RefreshReturned()
        {
            if (!EditorApplication.isCompiling) state.refresh_pending = false;
            Publish();
        }
        static void Update()
        {
            if (state.is_compiling != EditorApplication.isCompiling || state.is_updating != EditorApplication.isUpdating)
                Publish();
        }
        static void Publish()
        {
            state.instance_id = EditorInstanceInfo.InstanceId;
            state.is_compiling = EditorApplication.isCompiling;
            state.is_updating = EditorApplication.isUpdating;
            json = JsonUtility.ToJson(state);
            SessionState.SetString(Key, json);
        }
    }
}
