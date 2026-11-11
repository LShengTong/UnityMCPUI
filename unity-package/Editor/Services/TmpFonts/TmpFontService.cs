using System;
using UnityEditor;

namespace UnityMcp.Editor
{
    internal static class TmpFontService
    {
        public static TmpFontResult CreateTmpFont(string source_font_path, string asset_path, string characters, string population_mode, int sampling_point_size, int atlas_size, int padding)
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play before modifying font assets");
                var font = TmpFontUtility.Create(source_font_path, asset_path, characters, population_mode, sampling_point_size, atlas_size, padding, out uint[] missing);
                return new TmpFontResult {
                    ok = true, asset_path = asset_path, guid = AssetDatabase.AssetPathToGUID(asset_path),
                    population_mode = font.atlasPopulationMode == TMPro.AtlasPopulationMode.Static ? "static" : "dynamic",
                    character_count = font.characterTable.Count, all_characters_present = missing.Length == 0,
                    missing_codepoints = TmpFontUtility.Missing(missing) };
            }
            catch (Exception error) { return new TmpFontResult { error = error.Message }; }
        }

        public static TmpFontResult UpdateTmpFontCharacters(string asset_path, string characters, string mode)
        {
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Exit Play before modifying font assets");
                var font = TmpFontUtility.Update(asset_path, characters, mode, out uint[] missing);
                return new TmpFontResult {
                    ok = true, asset_path = asset_path, guid = AssetDatabase.AssetPathToGUID(asset_path),
                    population_mode = font.atlasPopulationMode == TMPro.AtlasPopulationMode.Static ? "static" : "dynamic",
                    character_count = font.characterTable.Count, all_characters_present = missing.Length == 0,
                    missing_codepoints = TmpFontUtility.Missing(missing) };
            }
            catch (Exception error) { return new TmpFontResult { error = error.Message }; }
        }
    }
}
