using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityMcp.Editor
{
    internal static class UiAssetQueryRegistry
    {
        // Wire name          Enum type                 Query implementation
        static readonly (string Name, UiAssetType Type, UiAssetQuery Query)[] entries = {
            ("ui_prefab",     UiAssetType.UiPrefab,      new UiPrefabAssetQuery()),
            ("sprite",        UiAssetType.Sprite,        new SpriteAssetQuery()),
            ("tmp_font",      UiAssetType.TmpFont,       new TmpFontAssetQuery()),
        };

        static UiAssetQueryRegistry()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var types = new HashSet<UiAssetType>();
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Name) || !names.Add(entry.Name) || !types.Add(entry.Type))
                    throw new InvalidOperationException("Duplicate or invalid asset query registration: " + entry.Name);
                if (entry.Query == null || entry.Query.Type != entry.Type)
                    throw new InvalidOperationException("Asset query type mismatch: " + entry.Name);
            }
        }

        public static UiAssetQuery Get(string type)
        {
            foreach (var entry in entries)
                if (string.Equals(entry.Name, type, StringComparison.Ordinal)) return entry.Query;
            throw new Exception("asset_type must be " + string.Join(", ", entries.Select(entry => entry.Name)));
        }

        public static string Name(UiAssetType type)
        {
            foreach (var entry in entries)
                if (entry.Type == type) return entry.Name;
            throw new Exception("Unsupported asset type: " + type);
        }
    }
}
