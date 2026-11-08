using System;
using System.Collections.Generic;

namespace UnityMcp.Editor
{
    internal sealed class EditorRequestRegistry
    {
        readonly (string Method, string Path, EditorRequestType Type, IEditorRequestHandler Handler)[] entries;

        // Construct on the main thread so operations can capture Unity API values.
        public EditorRequestRegistry(EditorTaskQueue tasks)
        {
            // Method  Path                    Enum type                             Request handler
            entries = new (string, string, EditorRequestType, IEditorRequestHandler)[] {
                ("GET",  "/health",             EditorRequestType.Health,             new HealthRequestHandler()),
                ("GET",  "/project/info",       EditorRequestType.ProjectInfo,        new ProjectInfoRequestHandler()),
                ("POST", "/ui/assets/find",     EditorRequestType.FindUiAssets,       new FindUiAssetsRequestHandler(tasks)),
                ("POST", "/ui/assets/inspect",  EditorRequestType.InspectUiAssets,    new InspectUiAssetsRequestHandler(tasks)),
                ("POST", "/ui/prefabs",         EditorRequestType.CreateUiPrefab,     new CreateUiPrefabRequestHandler(tasks)),
                ("POST", "/ui/prefabs/batch",   EditorRequestType.BatchEditUiPrefab,  new BatchEditUiPrefabRequestHandler(tasks)),
                ("POST", "/ui/prefabs/preview", EditorRequestType.PreviewUiPrefab,    new PreviewUiPrefabRequestHandler(tasks)),
                ("POST", "/ui/images/import",   EditorRequestType.ImportUiImage,      new ImportUiImageRequestHandler(tasks)),
                ("POST", "/ui/backups/list",    EditorRequestType.ListUiBackups,      new ListUiBackupsRequestHandler(tasks)),
                ("POST", "/ui/backups/restore", EditorRequestType.RestoreUiPrefab,    new RestoreUiPrefabRequestHandler(tasks)),
            };

            var routes = new HashSet<(string Method, string Path)>();
            var types = new HashSet<EditorRequestType>();
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Method) || string.IsNullOrWhiteSpace(entry.Path)
                    || entry.Handler == null || !routes.Add((entry.Method, entry.Path)) || !types.Add(entry.Type))
                    throw new InvalidOperationException("Duplicate or invalid request registration: " + entry.Type);
            }
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

        public bool TryResolve(string method, string path, out EditorRequestType type)
        {
            foreach (var entry in entries)
                if (string.Equals(entry.Method, method, StringComparison.Ordinal)
                    && string.Equals(entry.Path, path, StringComparison.Ordinal))
                {
                    type = entry.Type;
                    return true;
                }
            type = default;
            return false;
        }
    }
}
