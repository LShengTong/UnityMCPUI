using System;
using NUnit.Framework;
using UnityEngine;

namespace UnityMcp.Editor.Tests
{
    public sealed class VisualPropertyTests
    {
        GameObject node;
        [SetUp] public void Setup() => node = new GameObject("VisualTest", typeof(RectTransform));
        [TearDown] public void Cleanup() => UnityEngine.Object.DestroyImmediate(node);

        [Test]
        public void TextCreationAndModificationUseTheSameProperties()
        {
            var text = node.AddComponent<TMPro.TextMeshProUGUI>();
            UiVisualPropertyUtility.ConfigureText(text, new UiPrefabNode {
                kind = "text", text_alignment = "TopLeft", word_wrap = false, auto_size = true,
                font_size_min = 12, font_size_max = 40, text_overflow = "Ellipsis", line_spacing = 7 });
            Assert.That(text.alignment, Is.EqualTo(TMPro.TextAlignmentOptions.TopLeft));
            Assert.That(text.textWrappingMode, Is.EqualTo(TMPro.TextWrappingModes.NoWrap));
            Assert.That(text.enableAutoSizing, Is.True);
            Assert.That(text.fontSizeMin, Is.EqualTo(12));
            Assert.That(text.fontSizeMax, Is.EqualTo(40));
            Assert.That(text.overflowMode, Is.EqualTo(TMPro.TextOverflowModes.Ellipsis));
            Assert.That(text.lineSpacing, Is.EqualTo(7));
            ModifyUiNodePropertiesOperation.Execute(node, new UiPropertyChange { property = "text_alignment", value = "BottomRight" });
            ModifyUiNodePropertiesOperation.Execute(node, new UiPropertyChange { property = "word_wrap", value = "true" });
            ModifyUiNodePropertiesOperation.Execute(node, new UiPropertyChange { property = "line_spacing", value = "-3" });
            Assert.That(text.alignment, Is.EqualTo(TMPro.TextAlignmentOptions.BottomRight));
            Assert.That(text.textWrappingMode, Is.EqualTo(TMPro.TextWrappingModes.Normal));
            Assert.That(text.lineSpacing, Is.EqualTo(-3));
            Assert.Throws<Exception>(() => UiVisualPropertyUtility.TryApply(node, "font_size_min", "41"));
            Assert.Throws<Exception>(() => UiVisualPropertyUtility.TryApply(node, "text_alignment", "0"));
        }

        [Test]
        public void ImageFillAndRaycastSettingsAreAppliedAndValidated()
        {
            var image = node.AddComponent<UnityEngine.UI.Image>();
            UiVisualPropertyUtility.ConfigureImage(image, new UiPrefabNode { kind = "button" });
            Assert.That(image.raycastTarget, Is.True);
            UiVisualPropertyUtility.ConfigureImage(image, new UiPrefabNode {
                kind = "button", image_type = "Filled", fill_method = "Horizontal", fill_origin = 1,
                fill_amount = .35f, preserve_aspect = true, has_raycast_target = true, raycast_target = false });
            Assert.That(image.type, Is.EqualTo(UnityEngine.UI.Image.Type.Filled));
            Assert.That(image.fillMethod, Is.EqualTo(UnityEngine.UI.Image.FillMethod.Horizontal));
            Assert.That(image.fillOrigin, Is.EqualTo(1));
            Assert.That(image.fillAmount, Is.EqualTo(.35f));
            Assert.That(image.preserveAspect, Is.True);
            Assert.That(image.raycastTarget, Is.False);
            Assert.Throws<Exception>(() => UiVisualPropertyUtility.TryApply(node, "fill_origin", "2"));
            Assert.Throws<Exception>(() => UiVisualPropertyUtility.TryApply(node, "fill_amount", "1.1"));
            Assert.Throws<Exception>(() => UiVisualPropertyUtility.TryApply(node, "fill_amount", "NaN"));
            UiVisualPropertyUtility.TryApply(node, "fill_amount", ".8");
            Assert.That(image.fillAmount, Is.EqualTo(.8f));
        }

        [Test]
        public void LinearLayoutControlAndExpansionCanBeConfigured()
        {
            var layout = node.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            UiVisualPropertyUtility.ConfigureLayout(layout, new UiPrefabNode {
                child_alignment = "UpperLeft", child_control_width = false, child_force_expand_height = true });
            Assert.That(layout.childAlignment, Is.EqualTo(TextAnchor.UpperLeft));
            Assert.That(layout.childControlWidth, Is.False);
            Assert.That(layout.childForceExpandHeight, Is.True);
            UiVisualPropertyUtility.TryApply(node, "child_control_width", "true");
            UiVisualPropertyUtility.TryApply(node, "child_force_expand_height", "false");
            Assert.That(layout.childControlWidth, Is.True);
            Assert.That(layout.childForceExpandHeight, Is.False);
        }

        [Test]
        public void GridSupportsFixedColumnsAndRejectsInvalidCounts()
        {
            var grid = node.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            UiVisualPropertyUtility.ConfigureLayout(grid, new UiPrefabNode {
                layout = "grid", grid_constraint = "FixedColumnCount", grid_constraint_count = 4,
                grid_cell_size = new UiOffset { x = 60, y = 80 }, grid_spacing = new UiOffset { x = 5, y = 6 } });
            Assert.That(grid.constraint, Is.EqualTo(UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount));
            Assert.That(grid.constraintCount, Is.EqualTo(4));
            Assert.That(grid.cellSize, Is.EqualTo(new Vector2(60, 80)));
            Assert.That(grid.spacing, Is.EqualTo(new Vector2(5, 6)));
            UiVisualPropertyUtility.TryApply(node, "grid_constraint_count", "6");
            Assert.That(grid.constraintCount, Is.EqualTo(6));
            Assert.Throws<Exception>(() => UiVisualPropertyUtility.TryApply(node, "grid_constraint_count", "0"));
            Assert.Throws<Exception>(() => UiVisualPropertyUtility.TryApply(node, "grid_constraint_count", "2.5"));
            Assert.Throws<Exception>(() => UiVisualPropertyUtility.TryApply(node, "child_control_width", "true"));
        }
    }
}
