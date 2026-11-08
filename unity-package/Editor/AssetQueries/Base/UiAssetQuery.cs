using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace UnityMcp.Editor
{
    internal abstract class UiAssetQuery
    {
        public abstract UiAssetType Type { get; }
        protected abstract string Filter { get; }
        public abstract bool Matches(UnityEngine.Object asset);
        public abstract UiAssetInspectionResult Inspect(UiAssetInspection query, int index, double deadline);
        protected virtual void ValidateInspection(UiAssetInspection query) { }
        protected UnityEngine.Object Resolve(UiAssetInspection query)
        {
            UiAssetQueryUtility.ValidatePath(query.asset_path);
            ValidateInspection(query);
            var candidates = AssetDatabase.LoadAllAssetsAtPath(query.asset_path).Where(a => a != null && Matches(a));
            if (!string.IsNullOrEmpty(query.local_id))
                candidates = candidates.Where(a => UiAssetQueryUtility.Describe(a, Type).local_id == query.local_id);
            var assets = candidates.Take(2).ToArray();
            if (assets.Length == 0) throw new Exception("Asset not found, wrong type or local_id");
            if (assets.Length > 1) throw new Exception("Multiple sub-assets; supply local_id from find_ui_assets");
            return assets[0];
        }
        public UiAssetSearchResult Find(UiAssetSearch query, int index, double deadline)
        {
            if (query.limit < 1 || query.limit > 100 || query.offset < 0 || query.offset > 1000000
                || (query.search?.Length ?? 0) > 200) throw new Exception("Invalid search limits");
            UiAssetQueryUtility.ValidatePath(query.folder, true);
            if (!AssetDatabase.IsValidFolder(query.folder)) throw new Exception("Search folder does not exist");
            var paths = AssetDatabase.FindAssets(Filter, new[] { query.folder }).Select(AssetDatabase.GUIDToAssetPath)
                .Distinct().OrderBy(p => p, StringComparer.Ordinal);
            var matches = new List<UiAssetQueryUtility.AssetReference>();
            foreach (string path in paths)
            {
                UiAssetQueryUtility.CheckDeadline(deadline);
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset == null || !Matches(asset) || asset.name.IndexOf(query.search ?? "", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    matches.Add(UiAssetQueryUtility.Describe(asset, Type));
                }
            }
            var ordered = matches.OrderBy(a => a.asset_path, StringComparer.Ordinal).ThenBy(a => a.name, StringComparer.Ordinal)
                .ThenBy(a => a.local_id, StringComparer.Ordinal).ToArray();
            var page = ordered.Skip(query.offset).Take(query.limit).ToArray();
            int next = query.offset + page.Length;
            return new UiAssetSearchResult { index = index, ok = true, total = ordered.Length, assets = page,
                asset_type = UiAssetQueryRegistry.Name(Type), next_offset = next < ordered.Length ? next : -1 };
        }
    }

    [Serializable] internal class UiAssetInspectionResult
    {
        public int index;
        public bool ok;
        public string error, asset_type;
        public UiAssetQueryUtility.AssetReference asset;
    }
    [Serializable] internal sealed class UiAssetSearchResult
    {
        public int index, total, next_offset = -1;
        public bool ok;
        public string error, asset_type;
        public UiAssetQueryUtility.AssetReference[] assets;
    }
}
