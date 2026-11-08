using System;
using UnityEditor;
using UnityEngine;
namespace UnityMcp.Editor
{
    internal sealed class SpriteAssetQuery : UiAssetQuery
    {
        public override UiAssetType Type => UiAssetType.Sprite;
        protected override string Filter => "t:Sprite";
        public override bool Matches(UnityEngine.Object asset) => asset is Sprite;
        [Serializable] internal sealed class Details
        {
            public Vector2 size, texture_size, pivot;
            public Rect rect;
            public Vector4 border;
            public float pixels_per_unit;
            public string texture_path, filter_mode;
        }
        [Serializable] sealed class Result : UiAssetInspectionResult { public Details sprite; }
        public override UiAssetInspectionResult Inspect(UiAssetInspection query, int index, double deadline)
        {
            UiAssetQueryUtility.CheckDeadline(deadline);
            var sprite = (Sprite)Resolve(query);
            return new Result { index = index, ok = true, asset_type = "sprite", asset = UiAssetQueryUtility.Describe(sprite, Type),
                sprite = new Details { size = sprite.rect.size, rect = sprite.rect, pivot = sprite.pivot, border = sprite.border,
                    pixels_per_unit = sprite.pixelsPerUnit, texture_size = new Vector2(sprite.texture.width, sprite.texture.height),
                    texture_path = AssetDatabase.GetAssetPath(sprite.texture), filter_mode = sprite.texture.filterMode.ToString() } };
        }
    }
}
