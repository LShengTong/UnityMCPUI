using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityMcp.Editor
{
    internal static class UiTestSceneUtility
    {
        internal const string ParentPath = "UnityMcpTestCanvas/Content";

        internal static Transform Build(Scene scene, float width, float height, bool addInputModule)
        {
            if (float.IsNaN(width) || float.IsInfinity(width) || width <= 0 || width > 16384
                || float.IsNaN(height) || float.IsInfinity(height) || height <= 0 || height > 16384)
                throw new Exception("Invalid test Canvas reference resolution");
            var canvas = new GameObject("UnityMcpTestCanvas", typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(canvas, scene);
            canvas.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvas.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(width, height);
            scaler.matchWidthOrHeight = .5f;
            canvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(canvas.transform, false);
            var rect = (RectTransform)content.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.sizeDelta = Vector2.zero;

            var events = new GameObject("UnityMcpTestEventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            SceneManager.MoveGameObjectToScene(events, scene);
            if (addInputModule) AddInputModule(events);

            var cameraObject = new GameObject("UnityMcpTestCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f, .12f, .12f);
            camera.cullingMask = 0;
            return content.transform;
        }

        internal static void AddInputModule(GameObject events)
        {
#if ENABLE_INPUT_SYSTEM
            var module = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem")
                ?? throw new Exception("Input System is enabled but its UI input module is unavailable");
            events.AddComponent(module);
#else
            events.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }
    }
}
