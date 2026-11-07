using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
namespace UnityMcp.Editor
{
    internal sealed class TmpFontAssetQuery : UiAssetQuery
    {
        public override UiAssetType Type => UiAssetType.TmpFont;
        protected override string Filter => "t:TMP_FontAsset";
        public override bool Matches(UnityEngine.Object asset) => asset is TMPro.TMP_FontAsset;
        protected override void ValidateInspection(UiAssetInspection query)
        {
            if ((query.text?.Length ?? 0) > 1024) throw new Exception("Text exceeds 1024 UTF-16 code units");
        }
        [Serializable] internal sealed class Details
        {
            public string family, style, atlas_population_mode, source_font_path;
            public int atlas_width, atlas_height, atlas_count;
            public UiAssetQueryUtility.AssetReference[] fallbacks;
            public bool fallbacks_truncated, text_checked, all_characters_present, search_fallbacks;
            public string[] missing_characters, missing_codepoints;
            public string coverage_mode = "existing_glyphs_only";
        }
        [Serializable] sealed class Reply : UiAssetInspectionResult { public Details font; }
        public override UiAssetInspectionResult Inspect(UiAssetInspection query, int index, double deadline)
        {
            UiAssetQueryUtility.CheckDeadline(deadline);
            var font = (TMPro.TMP_FontAsset)Resolve(query);
            return new Reply { index = index, ok = true, asset_type = "tmp_font", asset = UiAssetQueryUtility.Describe(font, Type), font = InspectFont(font, query) };
        }
        static Details InspectFont(TMPro.TMP_FontAsset font, UiAssetInspection query)
        {
            var fallbacks = font.fallbackFontAssetTable?.Where(f => f != null).ToArray() ?? Array.Empty<TMPro.TMP_FontAsset>();
            var result = new Details { family = font.faceInfo.familyName, style = font.faceInfo.styleName,
                atlas_population_mode = font.atlasPopulationMode.ToString(), atlas_width = font.atlasWidth,
                atlas_height = font.atlasHeight, atlas_count = font.atlasTextures?.Length ?? 0,
                source_font_path = font.sourceFontFile == null ? "" : AssetDatabase.GetAssetPath(font.sourceFontFile),
                fallbacks = fallbacks.Take(64).Select(f => UiAssetQueryUtility.Describe(f, UiAssetType.TmpFont)).ToArray(),
                fallbacks_truncated = fallbacks.Length > 64, search_fallbacks = query.search_fallbacks,
                text_checked = !string.IsNullOrEmpty(query.text) };
            if (result.text_checked)
            {
                // Read existing atlas characters only; never grow dynamic atlases during a query.
                string text = query.text.Replace("\r", "").Replace("\n", "").Replace("\t", "");
                if (font.characterLookupTable == null) throw new Exception("Font character table is unavailable");
                result.all_characters_present = font.HasCharacters(text, out uint[] missing,
                    searchFallbacks: query.search_fallbacks, tryAddCharacter: false);
                var unique = (missing ?? Array.Empty<uint>()).Distinct().ToArray();
                result.missing_codepoints = unique.Select(c => "U+" + c.ToString("X4")).ToArray();
                result.missing_characters = unique.Select(c => char.ConvertFromUtf32((int)c)).ToArray();
            }
            return result;
        }
    }
}
