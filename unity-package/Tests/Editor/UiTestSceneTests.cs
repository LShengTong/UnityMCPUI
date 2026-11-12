using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp.Editor.Tests
{
    public sealed class UiTestSceneTests
    {
        string folder, folderGuid;
        Scene original;
        static double Deadline => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 + 20;

        [SetUp]
        public void SetUp()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) Assert.Ignore("Requires Edit mode");
            original = SceneManager.GetActiveScene();
            folder = "Assets/__UnityMcpSceneTests_" + Guid.NewGuid().ToString("N");
            folderGuid = AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
        }

        [TearDown]
        public void TearDown()
        {
            if (folder == null) return;
            for (int index = SceneManager.sceneCount - 1; index >= 0; index--)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (scene.path.StartsWith(folder + "/", StringComparison.Ordinal)) EditorSceneManager.CloseScene(scene, true);
            }
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            if (AssetDatabase.AssetPathToGUID(folder) == folderGuid) AssetDatabase.DeleteAsset(folder);
        }

        [Test]
        public void SavesUiRigAndLinkedPrefabWhilePreservingExistingScenes()
        {
            string prefabPath = folder + "/Panel.prefab";
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var source = new GameObject("Panel", typeof(RectTransform));
                SceneManager.MoveGameObjectToScene(source, preview);
                PrefabUtility.SaveAsPrefabAsset(source, prefabPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
            int count = SceneManager.sceneCount;
            bool dirty = original.isDirty;
            string scenePath = folder + "/UiTest.unity";
            var request = new CreateUiTestSceneRequest { id = Guid.NewGuid().ToString("N"), deadline = Deadline,
                scene_path = scenePath, prefab_path = prefabPath, reference_width = 800, reference_height = 600 };
            var reply = JsonUtility.FromJson<CreateUiTestSceneReply>(CreateUiTestSceneRequestHandler.Execute(request));
            Assert.That(reply.ok, Is.True, reply.error);
            Assert.That(reply.saved, Is.True);
            Assert.That(reply.id, Is.EqualTo(request.id));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(count + 1));
            Assert.That(original.isLoaded, Is.True);
            Assert.That(original.isDirty, Is.EqualTo(dirty));
            var created = SceneManager.GetActiveScene();
            Assert.That(created.path, Is.EqualTo(scenePath));
            EditorSceneManager.CloseScene(created, true);
            created = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            var canvas = created.GetRootGameObjects().Single(go => go.name == "UnityMcpTestCanvas");
            Assert.That(canvas.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));
            Assert.That(canvas.GetComponent<UnityEngine.UI.CanvasScaler>().referenceResolution, Is.EqualTo(new Vector2(800, 600)));
            Assert.That(canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>(), Is.Not.Null);
            var content = canvas.transform.Find("Content");
            Assert.That(((RectTransform)content).anchorMax, Is.EqualTo(Vector2.one));
            var instance = content.Find("Panel").gameObject;
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance), Is.EqualTo(prefabPath));
            Assert.That(reply.prefab_instance_path, Is.EqualTo(reply.parent_path + "/Panel"));
            var events = created.GetRootGameObjects().Single(go => go.name == "UnityMcpTestEventSystem");
            Assert.That(events.GetComponent<UnityEngine.EventSystems.EventSystem>(), Is.Not.Null);
            Assert.That(events.GetComponents<UnityEngine.EventSystems.BaseInputModule>().Length, Is.EqualTo(1));
        }

        [Test]
        public void RefusesOverwriteAndExpiredCreationLeavesSceneStateIntact()
        {
            string path = folder + "/UiTest.unity";
            int count = SceneManager.sceneCount;
            var expired = CreateUiTestSceneService.Execute(path, "", 800, 600, 0);
            Assert.That(expired.ok, Is.False);
            Assert.That(expired.error, Does.Contain("expired"));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(count));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(original));
            var first = CreateUiTestSceneService.Execute(path, "", 800, 600, Deadline);
            Assert.That(first.ok, Is.True, first.error);
            byte[] saved = File.ReadAllBytes(path);
            var active = SceneManager.GetActiveScene();
            var duplicate = CreateUiTestSceneService.Execute(path, "", 800, 600, Deadline);
            Assert.That(duplicate.ok, Is.False);
            Assert.That(duplicate.error, Does.Contain("exists"));
            Assert.That(File.ReadAllBytes(path), Is.EqualTo(saved));
            Assert.That(SceneManager.sceneCount, Is.EqualTo(count + 1));
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(active));
        }
    }
}
