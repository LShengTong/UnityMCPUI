using System;
using NUnit.Framework;
using UnityEngine;

namespace UnityMcp.Editor.Tests
{
    public sealed class RuntimeUiPathTests
    {
        [TestCase(640, 480)]
        [TestCase(720, 1280)]
        [TestCase(0, 0)]
        public void ScreenshotSizesAcceptCompletePairs(int width, int height) =>
            Assert.DoesNotThrow(() => RuntimeUiTestRunner.ValidateScreenshotSize(width, height));
        [TestCase(640, 0)]
        [TestCase(0, 480)]
        [TestCase(-1, 480)]
        [TestCase(4096, 480)]
        public void ScreenshotSizesRejectIncompleteOrOversizedPairs(int width, int height) =>
            Assert.Throws<Exception>(() => RuntimeUiTestRunner.ValidateScreenshotSize(width, height));
        [Test]
        public void RelativePathsResolveIndexesAndRejectAmbiguousNames()
        {
            var root = new GameObject("RuntimePathTest");
            try
            {
                new GameObject("Panel").transform.SetParent(root.transform);
                var second = new GameObject("Panel"); second.transform.SetParent(root.transform);
                Assert.That(PlayModeSession.Relative(root.transform, "0/1"), Is.EqualTo(second.transform));
                Assert.Throws<Exception>(() => PlayModeSession.Relative(root.transform, "Panel"));
                Assert.Throws<Exception>(() => PlayModeSession.Relative(root.transform, "Missing"));
                Assert.That(PlayModeSession.Relative(root.transform, "0"), Is.EqualTo(root.transform));
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        [Test]
        public void RuntimeMutationRejectsEditMode()
        {
            if (UnityEditor.EditorApplication.isPlaying) Assert.Ignore("EditMode check");
            Assert.Throws<Exception>(() => PlayModeSession.Mount(null, "", ""));
            Assert.Throws<Exception>(() => PlayModeSession.Clear(""));
        }
        [Test]
        public void TextConditionsReadInputFields()
        {
            var node = new GameObject("Input", typeof(RectTransform));
            try
            {
                node.AddComponent<TMPro.TMP_InputField>().SetTextWithoutNotify("Hello");
                Assert.That(RuntimeUiInspection.Value(node.transform, new UiTestStep { property = "text" }), Is.EqualTo("Hello"));
            }
            finally { UnityEngine.Object.DestroyImmediate(node); }
        }
    }
}
