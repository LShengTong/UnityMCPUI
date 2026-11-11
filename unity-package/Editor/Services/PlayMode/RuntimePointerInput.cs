using System;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityMcp.Editor
{
    // Supplies input state. EventSystem and its existing input module dispatch UI events.
    internal sealed class RuntimePointerInput : IDisposable
    {
        readonly BaseInputModule module;
        readonly BaseInput previousInput;
        readonly RuntimePointerLegacyInput legacy;
        readonly object mouse;
        readonly Type mouseStateType;
        readonly MethodInfo queueState;
        readonly MethodInfo removeDevice;
        readonly object previousMouse;
        ushort buttons;
        bool disposed;
        Vector2 position;

        internal string Backend => legacy != null ? "legacy" : "input_system";
        internal Vector2 Position => position;

        internal RuntimePointerInput()
        {
            if (Cursor.lockState != CursorLockMode.None)
                throw new Exception("Unlock the cursor before pointer tests");
            var systems = UnityEngine.Object.FindObjectsByType<EventSystem>()
                .Where(item => item.isActiveAndEnabled).ToArray();
            if (systems.Length != 1) throw new Exception("Pointer tests require exactly one active EventSystem");
            module = systems[0].currentInputModule;
            if (module == null || !module.isActiveAndEnabled)
                throw new Exception("Pointer tests require an active UI input module");

            if (module is StandaloneInputModule)
            {
                if (!systems[0].isFocused)
                    throw new Exception("StandaloneInputModule ignores unfocused input. Focus Unity or use InputSystemUIInputModule for background pointer tests");
                previousInput = module.inputOverride;
                legacy = module.gameObject.AddComponent<RuntimePointerLegacyInput>();
                legacy.hideFlags = HideFlags.HideAndDontSave;
                module.inputOverride = legacy;
                return;
            }
            if (module.GetType().FullName != "UnityEngine.InputSystem.UI.InputSystemUIInputModule")
                throw new Exception("Pointer tests support StandaloneInputModule or InputSystemUIInputModule");

            // Reflection keeps Input System optional for projects using the legacy backend.
            var inputSystem = Type.GetType("UnityEngine.InputSystem.InputSystem, Unity.InputSystem", true);
            mouseStateType = Type.GetType("UnityEngine.InputSystem.LowLevel.MouseState, Unity.InputSystem", true);
            var mouseType = Type.GetType("UnityEngine.InputSystem.Mouse, Unity.InputSystem", true);
            previousMouse = mouseType.GetProperty("current", BindingFlags.Public | BindingFlags.Static).GetValue(null);
            var deviceType = Type.GetType("UnityEngine.InputSystem.InputDevice, Unity.InputSystem", true);
            queueState = inputSystem.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == "QueueStateEvent" && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 3).MakeGenericMethod(mouseStateType);
            removeDevice = inputSystem.GetMethod("RemoveDevice", new[] { deviceType });
            mouse = inputSystem.GetMethod("AddDevice", new[] { typeof(string), typeof(string), typeof(string) })
                .Invoke(null, new object[] { "Mouse", "UnityMcpTestMouse", null });
        }

        internal Vector2 Target(Transform root, UiTestStep step)
        {
            Vector2 target = new Vector2(step.x, step.y);
            if (!string.IsNullOrEmpty(step.target_path))
            {
                var node = PlayModeSession.Relative(root, step.target_path) as RectTransform;
                if (node == null || !node.gameObject.activeInHierarchy)
                    throw new Exception("Pointer target must be an active RectTransform");
                var canvas = node.GetComponentInParent<Canvas>();
                if (canvas == null || !canvas.isActiveAndEnabled)
                    throw new Exception("Pointer target requires an active Canvas");
                var rootCanvas = canvas.rootCanvas;
                var camera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
                if (rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay && camera == null)
                    throw new Exception("Pointer target Canvas requires an assigned world camera");
                Vector3 center = node.TransformPoint(node.rect.center);
                if (camera != null && camera.WorldToScreenPoint(center).z <= 0)
                    throw new Exception("Pointer target is behind its camera");
                target += RectTransformUtility.WorldToScreenPoint(camera, center);
            }
            if (target.x < 0 || target.y < 0 || target.x >= Screen.width || target.y >= Screen.height)
                throw new Exception("Pointer position is outside the Game view");
            return target;
        }

        internal void Move(Vector2 target)
        {
            Vector2 delta = target - position;
            position = target;
            Send(delta, Vector2.zero);
        }

        internal void Button(string button, bool down)
        {
            int index = button == "left" ? 0 : button == "right" ? 1 : button == "middle" ? 2
                : throw new Exception("Unknown mouse button");
            int mask = 1 << index;
            if (((buttons & mask) != 0) == down)
                throw new Exception(down ? "Mouse button is already held" : "Mouse button is not held");
            buttons = (ushort)(down ? buttons | mask : buttons & ~mask);
            Send(Vector2.zero, Vector2.zero);
        }

        internal void Scroll(float x, float y) => Send(Vector2.zero, new Vector2(x, y));

        void Send(Vector2 delta, Vector2 scroll)
        {
            if (disposed || module == null || !module.isActiveAndEnabled
                || EventSystem.current == null || EventSystem.current.currentInputModule != module)
                throw new Exception("UI input module changed during the pointer test");
            if (legacy != null)
            {
                if (!EventSystem.current.isFocused)
                    throw new Exception("StandaloneInputModule lost focus during the pointer test");
                legacy.SetState(position, buttons, scroll);
                return;
            }
            object state = Activator.CreateInstance(mouseStateType);
            mouseStateType.GetField("position").SetValue(state, position);
            mouseStateType.GetField("delta").SetValue(state, delta);
            // Input System mouse wheel state uses 120 units per tick.
            mouseStateType.GetField("scroll").SetValue(state, scroll * 120f);
            mouseStateType.GetField("buttons").SetValue(state, buttons);
            queueState.Invoke(null, new[] { mouse, state, (object)(-1d) });
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // Cancel held presses and drags before restoring the input source.
            try { if (module != null) module.DeactivateModule(); }
            finally
            {
                if (mouse != null)
                {
                    // Input System clears pointer state in OnDisable, preventing a held press
                    // from becoming a click when the virtual device is removed.
                    bool enabled = module != null && module.enabled;
                    try
                    {
                        if (enabled) module.enabled = false;
                        removeDevice.Invoke(null, new[] { mouse });
                    }
                    finally
                    {
                        if (previousMouse != null && (bool)previousMouse.GetType().GetProperty("added").GetValue(previousMouse))
                            previousMouse.GetType().GetMethod("MakeCurrent").Invoke(previousMouse, null);
                        if (enabled && module != null) module.enabled = true;
                    }
                }
                if (legacy != null)
                {
                    if (module != null) module.inputOverride = previousInput;
                    UnityEngine.Object.DestroyImmediate(legacy);
                }
            }
        }
    }
}
