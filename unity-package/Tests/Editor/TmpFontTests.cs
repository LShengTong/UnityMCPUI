using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor.Tests
{
    public class TmpFontTests
    {
        string folder, source, output;
        [SetUp] public void SetUp()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) Assert.Ignore("Requires Edit mode");
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") == null) Assert.Ignore("Import TMP Essential Resources first");
            string original = AssetDatabase.FindAssets("t:Font").Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => p.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".otf", StringComparison.OrdinalIgnoreCase));
            if (original == null) Assert.Ignore("Import a TTF/OTF source font before running font integration tests");
            folder = "Assets/__UnityMcpFontTests_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            source = folder + "/Source" + Path.GetExtension(original);
            Assert.That(AssetDatabase.CopyAsset(original, source), Is.True);
            output = folder + "/Test.asset";
        }
        [TearDown] public void TearDown()
        {
            if (folder != null) AssetDatabase.DeleteAsset(folder);
        }
        TMP_FontAsset Create(string mode = "dynamic")
        {
            var font = TmpFontUtility.Create(new CreateTmpFontRequest { source_font_path = source,
                asset_path = output, characters = "AB", population_mode = mode }, out var missing);
            Assert.That(missing, Is.Empty, "Fixture source font must contain Latin A and B");
            return font;
        }
        [TestCase("static")]
        [TestCase("dynamic")]
        public void AppendReplacePreserveGuidModeAndPersistGlyphs(string mode)
        {
            var font = Create(mode);
            string guid = AssetDatabase.AssetPathToGUID(output);
            Assert.That(font.isMultiAtlasTexturesEnabled, Is.False);
            var serialized = new SerializedObject(font);
            Assert.That(serialized.FindProperty("m_ClearDynamicDataOnBuild").boolValue, Is.True);
            font = TmpFontUtility.Update(new UpdateTmpFontCharactersRequest { asset_path = output, characters = "C" }, out var missing);
            Assert.That(missing, Is.Empty);
            Assert.That(font.HasCharacter('A'), Is.True);
            Assert.That(font.HasCharacter('C'), Is.True);
            font = TmpFontUtility.Update(new UpdateTmpFontCharactersRequest { asset_path = output,
                characters = "B\nB\t", mode = "replace" }, out missing);
            Assert.That(missing, Is.Empty);
            AssetDatabase.ImportAsset(output, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(output);
            Assert.That(font.HasCharacter('A'), Is.False);
            Assert.That(font.HasCharacter('B'), Is.True);
            Assert.That(font.HasCharacter('C'), Is.False);
            Assert.That(font.atlasPopulationMode.ToString().ToLowerInvariant(), Is.EqualTo(mode));
            Assert.That(AssetDatabase.AssetPathToGUID(output), Is.EqualTo(guid));
            Assert.That(font.atlasTextures.All(t => AssetDatabase.GetAssetPath(t) == output), Is.True);
        }
        [Test] public void MissingCharactersAreReportedAndCreationNeverOverwrites()
        {
            var font = Create();
            TmpFontUtility.Update(new UpdateTmpFontCharactersRequest { asset_path = output,
                characters = char.ConvertFromUtf32(0x10FFFF) }, out var missing);
            Assert.That(TmpFontUtility.Missing(missing), Does.Contain("U+10FFFF"));
            byte[] saved = File.ReadAllBytes(output);
            Assert.Throws<Exception>(() => Create());
            Assert.That(File.ReadAllBytes(output), Is.EqualTo(saved));
        }
        [Test] public void UnicodeScalarsAreDeduplicatedAndInvalidSurrogatesRejected()
        {
            Assert.That(TmpFontUtility.Characters("AA\r\n\t" + char.ConvertFromUtf32(0x1F600)), Is.EqualTo(new uint[] { 65, 0x1F600 }));
            Assert.Throws<Exception>(() => TmpFontUtility.Characters("\uD800"));
            Assert.Throws<Exception>(() => TmpFontUtility.PathFor("Assets/../Font.asset", ".asset"));
        }
    }
}
