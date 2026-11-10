using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp.Editor.Tests
{
    public sealed class IsolatedPlaySceneTests
    {
        static void RequireSavedEditScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || IsolatedPlayScene.Active)
                Assert.Ignore("Requires Edit mode without an active isolated session");
            try { IsolatedPlayScene.ValidateSavedScenes(); }
            catch (Exception) { Assert.Ignore("Save the open scenes before running isolated scene setup tests"); }
        }
        [Test]
        public void DirtySceneIsRejectedWithoutChangingPlayStartScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || IsolatedPlayScene.Active) Assert.Ignore("Requires Edit mode");
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) Assert.Ignore("Close Prefab Mode first");
            var original = SceneManager.GetActiveScene();
            var start = EditorSceneManager.playModeStartScene;
            var temporary = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                EditorSceneManager.MarkSceneDirty(temporary);
                Assert.That(Assert.Throws<Exception>(() => IsolatedPlayScene.Prepare(1920, 1080)).Message, Does.Contain("Save scene"));
                Assert.That(EditorSceneManager.playModeStartScene, Is.EqualTo(start));
                Assert.That(IsolatedPlayScene.Active, Is.False);
            }
            finally
            {
                EditorSceneManager.CloseScene(temporary, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            }
        }
        [Test]
        public void PrepareAndRestorePreserveSceneSetupAndCleanOwnedScene()
        {
            RequireSavedEditScene();
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var start = EditorSceneManager.playModeStartScene;
            string folder = "";
            Scene opened = default;
            try
            {
                IsolatedPlayScene.Prepare(1280, 720);
                string path = IsolatedPlayScene.ScenePath;
                folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
                Assert.That(AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene), Is.EqualTo(path));
                Assert.That(EditorSceneManager.GetSceneManagerSetup().Select(item => item.path), Is.EqualTo(setup.Select(item => item.path)));
                opened = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                var canvas = opened.GetRootGameObjects().Single(go => go.name == "UnityMcpTestCanvas");
                Assert.That(canvas.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
                Assert.That(canvas.GetComponent<UnityEngine.UI.CanvasScaler>().referenceResolution, Is.EqualTo(new Vector2(1280, 720)));
                Assert.That(canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>(), Is.Not.Null);
                Assert.That(canvas.transform.Find("Content"), Is.Not.Null);
                Assert.That(opened.GetRootGameObjects().Any(go => go.GetComponent<UnityEngine.EventSystems.EventSystem>() != null), Is.True);
                EditorSceneManager.CloseScene(opened, true); opened = default;
                var originalActive = setup.First(item => item.isActive);
                SceneManager.SetActiveScene(SceneManager.GetSceneByPath(originalActive.path));
                IsolatedPlayScene.RestoreNow();
                Assert.That(IsolatedPlayScene.Active, Is.False);
                Assert.That(EditorSceneManager.playModeStartScene, Is.EqualTo(start));
                Assert.That(AssetDatabase.IsValidFolder(folder), Is.False);
            }
            finally
            {
                if (opened.IsValid() && opened.isLoaded) EditorSceneManager.CloseScene(opened, true);
                if (IsolatedPlayScene.Active) IsolatedPlayScene.RestoreNow();
            }
        }
    }
}
