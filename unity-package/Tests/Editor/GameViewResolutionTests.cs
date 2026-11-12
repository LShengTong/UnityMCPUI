using System;
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.TestTools;

namespace UnityMcp.Editor.Tests
{
    public sealed class GameViewResolutionTests
    {
        GameViewResolutionService.Selection original;
        static double Deadline => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 + 10;

        [UnityTest]
        public IEnumerator EditModeSetsFixedResolutionAndRestoresOriginalPreset()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) Assert.Ignore("Requires settled Edit mode");
            var query = GameViewResolutionService.Execute("query", 0, 0, -1, "", Deadline);
            while (!query.IsCompleted) yield return null;
            if (query.Result.can_restore) Assert.Ignore("Restore the user's pending resolution change first");
            original = GameViewResolutionService.CaptureSelection();
            foreach (var size in new[] { (640, 480), (720, 1280) })
            {
                var set = GameViewResolutionService.Execute("set", size.Item1, size.Item2, -1, "", Deadline);
                while (!set.IsCompleted) yield return null;
                Assert.That(set.IsFaulted, Is.False, set.Exception?.ToString());
                Assert.That(set.Result.ok, Is.True, set.Result.error);
                Assert.That(set.Result.actual_width, Is.EqualTo(size.Item1));
                Assert.That(set.Result.actual_height, Is.EqualTo(size.Item2));
                Assert.That(set.Result.can_restore, Is.True);
            }
            var restore = GameViewResolutionService.Execute("restore", 0, 0, -1, "", Deadline);
            while (!restore.IsCompleted) yield return null;
            Assert.That(restore.IsFaulted, Is.False, restore.Exception?.ToString());
            Assert.That(restore.Result.ok, Is.True, restore.Result.error);
            Assert.That(restore.Result.can_restore, Is.False);
            var restored = GameViewResolutionService.CaptureSelection();
            Assert.That(restored.name, Is.EqualTo(original.name));
            Assert.That(restored.sizeType, Is.EqualTo(original.sizeType));
            Assert.That(restored.index, Is.EqualTo(original.index));
            original = null;
        }

        [UnityTearDown]
        public IEnumerator RestoreAfterFailure()
        {
            if (original == null) yield break;
            var restore = GameViewResolutionService.Execute("restore", 0, 0, -1, "", Deadline);
            while (!restore.IsCompleted) yield return null;
            if (!restore.Result.ok) GameViewResolutionService.RestoreSelection(original);
            original = null;
        }
    }
}
