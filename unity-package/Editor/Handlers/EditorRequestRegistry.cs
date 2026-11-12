using System;
using System.Collections.Generic;

namespace UnityMcp.Editor
{
    internal sealed class EditorRequestRegistry
    {
        // Hand-written bindings. Protocol generation never overwrites this file.
        readonly (EditorRequestType Type, IEditorRequestHandler Handler)[] entries;

        // Construct on the main thread so handlers can capture Unity API values.
        public EditorRequestRegistry(EditorTaskQueue tasks)
        {
            entries = new (EditorRequestType, IEditorRequestHandler)[] {
                (EditorRequestType.Health, new HealthRequestHandler()),
                (EditorRequestType.ProjectInfo, new ProjectInfoRequestHandler()),
                (EditorRequestType.FindUiAssets, new FindUiAssetsRequestHandler(tasks)),
                (EditorRequestType.InspectUiAssets, new InspectUiAssetsRequestHandler(tasks)),
                (EditorRequestType.CreateUiPrefab, new CreateUiPrefabRequestHandler(tasks)),
                (EditorRequestType.CreateUiTestScene, new CreateUiTestSceneRequestHandler(tasks)),
                (EditorRequestType.GameViewResolution, new GameViewResolutionRequestHandler()),
                (EditorRequestType.BatchEditUiPrefab, new BatchEditUiPrefabRequestHandler(tasks)),
                (EditorRequestType.PreviewUiPrefab, new PreviewUiPrefabRequestHandler(tasks)),
                (EditorRequestType.ImportUiImage, new ImportUiImageRequestHandler(tasks)),
                (EditorRequestType.CreateTmpFont, new CreateTmpFontRequestHandler(tasks)),
                (EditorRequestType.UpdateTmpFontCharacters, new UpdateTmpFontCharactersRequestHandler(tasks)),
                (EditorRequestType.ListUiBackups, new ListUiBackupsRequestHandler(tasks)),
                (EditorRequestType.RestoreUiPrefab, new RestoreUiPrefabRequestHandler(tasks)),
                (EditorRequestType.CompilationStatus, new CompilationStatusRequestHandler()),
                (EditorRequestType.RefreshAssets, new RefreshAssetsRequestHandler(tasks)),
                (EditorRequestType.PlayStatus, new PlayStatusRequestHandler()),
                (EditorRequestType.EnterPlayMode, new EnterPlayModeRequestHandler()),
                (EditorRequestType.ExitPlayMode, new ExitPlayModeRequestHandler()),
                (EditorRequestType.InspectRuntimeUi, new InspectRuntimeUiRequestHandler()),
                (EditorRequestType.MountTestPrefab, new MountTestPrefabRequestHandler()),
                (EditorRequestType.RunUiTest, new RunUiTestRequestHandler()),
                (EditorRequestType.ClearTestInstances, new ClearTestInstancesRequestHandler()),
            };
            var types = new HashSet<EditorRequestType>();
            foreach (var entry in entries)
                if (entry.Handler == null || !Enum.IsDefined(typeof(EditorRequestType), entry.Type) || !types.Add(entry.Type))
                    throw new InvalidOperationException("Duplicate or invalid request registration: " + entry.Type);
            foreach (EditorRequestType type in Enum.GetValues(typeof(EditorRequestType)))
                if (!types.Contains(type))
                    throw new InvalidOperationException("Missing request registration: " + type);
        }

        public IEditorRequestHandler Get(EditorRequestType type)
        {
            foreach (var entry in entries)
                if (entry.Type == type) return entry.Handler;
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported request type");
        }

        public bool TryResolve(string method, string path, out EditorRequestType type) =>
            EditorRequestRoutes.TryResolve(method, path, out type);
    }
}
