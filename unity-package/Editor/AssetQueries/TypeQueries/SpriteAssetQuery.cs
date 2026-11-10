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
        public override UiAssetInspectionResult Inspect(UiAssetInspection query, int index, double deadline)
        {
            UiAssetQueryUtility.CheckDeadline(deadline);
            var sprite = (Sprite)Resolve(query);
            return new SpriteInspectionReply { index = index, ok = true, asset_type = "sprite", asset = UiAssetQueryUtility.Describe(sprite, Type),
                sprite = new SpriteDetails { size = sprite.rect.size, rect = new RectReply { x = sprite.rect.x, y = sprite.rect.y, width = sprite.rect.width, height = sprite.rect.height }, pivot = sprite.pivot, border = sprite.border,
                    pixels_per_unit = sprite.pixelsPerUnit, texture_size = new Vector2(sprite.texture.width, sprite.texture.height),
                    texture_path = AssetDatabase.GetAssetPath(sprite.texture), filter_mode = sprite.texture.filterMode.ToString() } };
        }
    }
}
