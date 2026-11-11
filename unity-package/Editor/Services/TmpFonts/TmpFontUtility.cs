using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace UnityMcp.Editor
{
    internal static class TmpFontUtility
    {
        internal static string PathFor(string path, string extension)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)
                || path.Contains('\\') || path.Split('/').Any(p => p == ".." || p == "." || p.Length == 0)
                || !path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Expected an Assets path ending in " + extension);
            string full = Path.GetFullPath(path);
            if (!full.StartsWith(Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new Exception("Asset path escapes Assets");
            return full;
        }
        internal static string[] Missing(uint[] missing) => (missing ?? Array.Empty<uint>()).Distinct()
            .Select(c => "U+" + c.ToString("X4")).ToArray();
        internal static uint[] Characters(string text)
        {
            var values = new System.Collections.Generic.List<uint>();
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r' || c == '\n' || c == '\t') continue;
                if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    values.Add((uint)char.ConvertToUtf32(c, text[++i]));
                else if (char.IsSurrogate(c)) throw new Exception("Characters contain an unpaired UTF-16 surrogate");
                else values.Add(c);
            }
            return values.Distinct().ToArray();
        }
        internal static uint[] Add(TMP_FontAsset font, uint[] characters)
        {
            if (characters.Length == 0) return Array.Empty<uint>();
            font.TryAddCharacters(characters, out uint[] missing);
            return missing ?? Array.Empty<uint>();
        }
        internal static void Save(TMP_FontAsset font)
        {
            foreach (var texture in font.atlasTextures.Where(t => t != null))
            {
                if (!AssetDatabase.Contains(texture)) AssetDatabase.AddObjectToAsset(texture, font);
                EditorUtility.SetDirty(texture);
            }
            if (font.material != null)
            {
                if (!AssetDatabase.Contains(font.material)) AssetDatabase.AddObjectToAsset(font.material, font);
                EditorUtility.SetDirty(font.material);
            }
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssetIfDirty(font);
        }
        internal static TMP_FontAsset Create(string source_font_path, string asset_path, string characters, string population_mode, int sampling_point_size, int atlas_size, int padding, out uint[] missing)
        {
            PathFor(asset_path, ".asset");
            // Load only imported project font files, never OS paths or package assets.
            PathFor(source_font_path, Path.GetExtension(source_font_path));
            string extension = Path.GetExtension(source_font_path).ToLowerInvariant();
            if (extension != ".ttf" && extension != ".otf" && extension != ".ttc") throw new Exception("Expected an imported TTF/OTF/TTC font");
            if (!AssetDatabase.IsValidFolder(Path.GetDirectoryName(asset_path).Replace('\\', '/')))
                throw new Exception("Output folder must already exist");
            if (File.Exists(asset_path) || File.Exists(asset_path + ".meta") || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(asset_path)))
                throw new Exception("Output already exists; refusing overwrite");
            var source = AssetDatabase.LoadAssetAtPath<Font>(source_font_path)
                ?? throw new Exception("Source font was not imported; enable Include Font Data");
            var codepoints = Characters(characters);
            if (population_mode == "static" && codepoints.Length == 0) throw new Exception("Static fonts require characters");
            TMP_FontAsset font = null;
            bool created = false;
            try
            {
                font = TMP_FontAsset.CreateFontAsset(source, sampling_point_size, padding,
                    GlyphRenderMode.SDFAA, atlas_size, atlas_size, AtlasPopulationMode.Dynamic, false)
                    ?? throw new Exception("Cannot create TMP font; check Include Font Data and TMP shader resources");
                font.name = Path.GetFileNameWithoutExtension(asset_path);
                // Bound this operation to one atlas. Missing glyphs are reported instead of unbounded growth.
                var serialized = new SerializedObject(font);
                serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                missing = Add(font, codepoints);
                font.atlasPopulationMode = population_mode == "static" ? AtlasPopulationMode.Static : AtlasPopulationMode.Dynamic;
                AssetDatabase.CreateAsset(font, asset_path); created = true;
                Save(font);
                return font;
            }
            catch
            {
                if (created) AssetDatabase.DeleteAsset(asset_path);
                else if (font != null)
                {
                    foreach (var texture in font.atlasTextures.Where(t => t != null)) UnityEngine.Object.DestroyImmediate(texture);
                    if (font.material != null) UnityEngine.Object.DestroyImmediate(font.material);
                    UnityEngine.Object.DestroyImmediate(font);
                }
                throw;
            }
        }
        internal static TMP_FontAsset Update(string asset_path, string characters, string mode, out uint[] missing)
        {
            string full = PathFor(asset_path, ".asset");
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(asset_path) ?? throw new Exception("TMP font asset not found");
            var codepoints = Characters(characters);
            if (codepoints.Length == 0) throw new Exception("No printable characters supplied");
            var assets = AssetDatabase.LoadAllAssetsAtPath(asset_path);
            if (assets.Any(EditorUtility.IsDirty)) throw new Exception("Save font asset changes before modifying characters");
            if (font.atlasTextures.Any(t => t != null && AssetDatabase.GetAssetPath(t) != asset_path)
                || font.material == null || AssetDatabase.GetAssetPath(font.material) != asset_path)
                throw new Exception("Font must own its material and atlas textures in the same asset file");
            if (font.atlasWidth < 1 || font.atlasHeight < 1 || font.atlasWidth > 2048 || font.atlasHeight > 2048 || font.atlasTextures.Length > 1)
                throw new Exception("Character updates currently support single-atlas fonts up to 2048 pixels");
            if (font.atlasPopulationMode == AtlasPopulationMode.DynamicOS) throw new Exception("Dynamic OS fonts are not supported");
            byte[] backup = File.ReadAllBytes(full);
            var originalMode = font.atlasPopulationMode;
            bool multiAtlas = font.isMultiAtlasTexturesEnabled;
            try
            {
                font.atlasPopulationMode = AtlasPopulationMode.Dynamic;
                if (font.sourceFontFile == null) throw new Exception("Source font is missing; cannot rebuild characters");
                font.isMultiAtlasTexturesEnabled = false;
                if (mode == "replace") font.ClearFontAssetData();
                missing = Add(font, codepoints);
                font.atlasPopulationMode = originalMode;
                font.isMultiAtlasTexturesEnabled = multiAtlas;
                Save(font);
                return font;
            }
            catch
            {
                File.WriteAllBytes(full, backup);
                AssetDatabase.ImportAsset(asset_path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                throw;
            }
        }
    }
}
