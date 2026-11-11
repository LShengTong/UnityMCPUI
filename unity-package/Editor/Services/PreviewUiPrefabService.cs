using System;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class PreviewUiPrefabService
    {
        public static PreviewUiPrefabResult Execute(double deadline, string asset_path, int width, int height, string backgroundColor)
        {
            PreviewRenderUtility preview = null;
            Texture2D image = null;
            var previousTarget = RenderTexture.active;
            bool begun = false;
            try
            {
                CheckDeadline(deadline);
                if (width < 64 || width > 2048 || height < 64 || height > 2048)
                    throw new Exception("Image dimensions must be 64..2048");
                if (!ColorUtility.TryParseHtmlString(backgroundColor, out var background))
                    throw new Exception("Invalid background color");
                string version = UiPrefabVersion.Read(asset_path);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(asset_path);
                if (prefab == null || prefab.GetComponentInChildren<RectTransform>(true) == null)
                    throw new Exception("Expected a uGUI prefab");
                preview = new PreviewRenderUtility();
                var camera = preview.camera;
                // Screen-space uGUI must use the game-camera rendering path. Preview cameras
                // are intended for mesh previews and may skip Canvas geometry in the pipeline.
                camera.cameraType = CameraType.Game;
                camera.clearFlags = CameraClearFlags.SolidColor;
                background.a = 1;
                camera.backgroundColor = QualitySettings.activeColorSpace == ColorSpace.Linear ? background.linear : background;
                camera.orthographic = true;
                camera.orthographicSize = height / 2f;
                camera.nearClipPlane = .1f;
                camera.farClipPlane = 2000;
                camera.transform.position = new Vector3(0, 0, -100);
                camera.transform.rotation = Quaternion.identity;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                camera.cullingMask = ~0;
                // BeginStaticPreview creates the render target and lets Unity handle readback/color conversion.
                preview.BeginStaticPreview(new Rect(0, 0, width, height));
                begun = true;
                camera.aspect = (float)width / height;
                var instance = preview.InstantiatePrefabInScene(prefab);
                foreach (var existing in instance.GetComponentsInChildren<Camera>(true)) existing.enabled = false;
                if (instance.GetComponent<Canvas>() == null)
                {
                    var wrapper = new GameObject("UI Preview Canvas", typeof(RectTransform), typeof(Canvas));
                    // A newly added Canvas can default to WorldSpace; this wrapper is screen-space UI.
                    var wrapperCanvas = wrapper.GetComponent<Canvas>();
                    wrapperCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                    wrapperCanvas.worldCamera = camera;
                    wrapperCanvas.planeDistance = 100;
                    preview.AddSingleGO(wrapper);
                    var scaler = wrapper.AddComponent<UnityEngine.UI.CanvasScaler>();
                    scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.referenceResolution = new Vector2(width, height);
                    instance.transform.SetParent(wrapper.transform, false);
                }
                // Render only this isolated scene; reassign the cloned screen-space canvases to the preview camera.
                foreach (var canvas in camera.scene.GetRootGameObjects()) ConfigureCanvases(canvas, camera);
                Canvas.ForceUpdateCanvases();
                foreach (var sceneRoot in camera.scene.GetRootGameObjects())
                    foreach (var scaler in sceneRoot.GetComponentsInChildren<UnityEngine.UI.CanvasScaler>(true))
                        if (scaler.isActiveAndEnabled) scaler.SendMessage("Update", SendMessageOptions.DontRequireReceiver);
                foreach (var text in instance.GetComponentsInChildren<TMPro.TMP_Text>(true))
                    if (text.isActiveAndEnabled) text.ForceMeshUpdate();
                foreach (var root in camera.scene.GetRootGameObjects())
                    foreach (var canvas in root.GetComponentsInChildren<Canvas>())
                        if (canvas.isRootCanvas) UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
                Canvas.ForceUpdateCanvases();
                CheckDeadline(deadline);
                preview.Render(true, false);
                image = preview.EndStaticPreview();
                begun = false;
                byte[] png = image.EncodeToPNG();
                if (png == null || png.Length > 12 * 1024 * 1024) throw new Exception("Preview image exceeds size limit");
                CheckDeadline(deadline);
                return new PreviewUiPrefabResult {
                    asset_path = asset_path,
                    version = version, ok = true, width = image.width, height = image.height,
                    png_base64 = Convert.ToBase64String(png)
                };
            }
            catch (Exception e) { return new PreviewUiPrefabResult { error = e.Message }; }
            finally
            {
                // Pair begin/end even if layout or rendering fails, then release all preview resources.
                try
                {
                    if (begun && preview != null)
                    {
                        var discarded = preview.EndStaticPreview();
                        if (discarded != null) UnityEngine.Object.DestroyImmediate(discarded);
                    }
                }
                finally
                {
                    try { preview?.Cleanup(); }
                    finally
                    {
                        if (image != null) UnityEngine.Object.DestroyImmediate(image);
                        RenderTexture.active = previousTarget;
                    }
                }
            }
        }
        static void ConfigureCanvases(GameObject root, Camera camera)
        {
            foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
            {
                if (!canvas.isRootCanvas) continue;
                if (canvas.renderMode == RenderMode.WorldSpace)
                    throw new Exception("World-space Canvas preview is not supported: " + canvas.name);
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 100;
                canvas.targetDisplay = 0;
            }
        }
        static void CheckDeadline(double deadline)
        {
            if (deadline <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0)
                throw new Exception("Preview request expired");
        }
    }
}
