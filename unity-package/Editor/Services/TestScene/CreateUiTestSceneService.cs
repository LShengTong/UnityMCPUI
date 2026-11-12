using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp.Editor
{
    internal static class CreateUiTestSceneService
    {
        internal static CreateUiTestSceneResult Execute(string scenePath, string prefabPath,
            float referenceWidth, float referenceHeight, double deadline)
        {
            Scene scene = default;
            Scene previousActive = SceneManager.GetActiveScene();
            var result = new CreateUiTestSceneResult { scene_path = scenePath };
            bool saveAttempted = false;
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new Exception("Exit Play before creating a test scene");
                CheckDeadline(deadline);
                UiPrefabAssetUtility.ValidateAssetPath(scenePath, ".unity");
                if (!AssetDatabase.IsValidFolder(Path.GetDirectoryName(scenePath).Replace('\\', '/')))
                    throw new Exception("Output folder must already exist");
                RequireUnusedPath(scenePath);
                GameObject source = null;
                if (!string.IsNullOrEmpty(prefabPath))
                {
                    source = UiPrefabAssetUtility.LoadValidated(prefabPath, null);
                    if (source.GetComponent<Canvas>() != null)
                        throw new Exception("Test prefab root must not contain a Canvas");
                }

                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var parent = UiTestSceneUtility.Build(scene, referenceWidth, referenceHeight, true);
                string instancePath = "";
                if (source != null)
                {
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, parent);
                    instancePath = UiTestSceneUtility.ParentPath + "/" + instance.name;
                }
                CheckDeadline(deadline);
                RequireUnusedPath(scenePath);
                SceneManager.SetActiveScene(scene);
                saveAttempted = true;
                if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new Exception("Cannot save test scene");
                result.saved = true;
                result.ok = true;
                result.parent_path = UiTestSceneUtility.ParentPath;
                result.prefab_instance_path = instancePath;
                return result;
            }
            catch (Exception error)
            {
                result.error = error.Message;
                try
                {
                    if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                    if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                    if (saveAttempted && (File.Exists(scenePath) || File.Exists(scenePath + ".meta")))
                    {
                        if (!AssetDatabase.DeleteAsset(scenePath))
                            result.error += "; Could not remove incomplete scene asset";
                        result.saved = File.Exists(scenePath);
                    }
                }
                catch (Exception cleanupError) { result.error += "; Cleanup failed: " + cleanupError.Message; }
                return result;
            }
        }

        static void RequireUnusedPath(string path)
        {
            if (File.Exists(path) || Directory.Exists(path) || File.Exists(path + ".meta")
                || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new Exception("Output already exists; refusing overwrite");
        }

        static void CheckDeadline(double deadline)
        {
            if (double.IsNaN(deadline) || double.IsInfinity(deadline)
                || deadline <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0)
                throw new Exception("Test scene creation deadline expired");
        }
    }
}
