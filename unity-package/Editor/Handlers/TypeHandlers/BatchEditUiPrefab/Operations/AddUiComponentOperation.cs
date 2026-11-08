using System;
using UnityEngine;

namespace UnityMcp.Editor
{
    internal static class AddUiComponentOperation
    {
        public static void Execute(Transform root, Transform node, Transform viewport, Transform content, UiBatchOperation operation)
        {
            UiNodeUtility.EnsureEditable(node, root);
            if (!(node is RectTransform)) throw new Exception("UI components require RectTransform");
            Type type;
            switch (operation.component)
            {
                case "Image": type = typeof(UnityEngine.UI.Image); break;
                case "Button": type = typeof(UnityEngine.UI.Button); break;
                case "CanvasGroup": type = typeof(CanvasGroup); break;
                case "LayoutElement": type = typeof(UnityEngine.UI.LayoutElement); break;
                case "ContentSizeFitter": type = typeof(UnityEngine.UI.ContentSizeFitter); break;
                case "HorizontalLayoutGroup": type = typeof(UnityEngine.UI.HorizontalLayoutGroup); break;
                case "VerticalLayoutGroup": type = typeof(UnityEngine.UI.VerticalLayoutGroup); break;
                case "GridLayoutGroup": type = typeof(UnityEngine.UI.GridLayoutGroup); break;
                case "RectMask2D": type = typeof(UnityEngine.UI.RectMask2D); break;
                case "ScrollRect": type = typeof(UnityEngine.UI.ScrollRect); break;
                default: throw new Exception("Unsupported component: " + operation.component);
            }
            var go = node.gameObject;
            if (go.GetComponent(type) != null) throw new Exception("Component already exists: " + operation.component);
            if (typeof(UnityEngine.UI.LayoutGroup).IsAssignableFrom(type) && go.GetComponent<UnityEngine.UI.LayoutGroup>() != null)
                throw new Exception("Node already has a LayoutGroup");
            if (type == typeof(UnityEngine.UI.Image) && go.GetComponent<UnityEngine.UI.Graphic>() != null)
                throw new Exception("Node already has a Graphic; use a separate Image node");
            if (type == typeof(UnityEngine.UI.Button) && go.GetComponent<UnityEngine.UI.Selectable>() != null)
                throw new Exception("Node already has a Selectable");
            var graphic = go.GetComponent<UnityEngine.UI.Graphic>();
            if (type == typeof(UnityEngine.UI.Button) && graphic == null)
                throw new Exception("Button requires an existing Graphic; add Image first");
            if (type == typeof(UnityEngine.UI.RectMask2D) && go.GetComponent<UnityEngine.UI.Mask>() != null)
                throw new Exception("Node already has a Mask");
            if (type == typeof(UnityEngine.UI.ScrollRect))
            {
                UiNodeUtility.EnsureEditable(viewport, root);
                UiNodeUtility.EnsureEditable(content, root);
                if (!(viewport is RectTransform) || !(content is RectTransform)
                    || viewport == node || !viewport.IsChildOf(node) || content == viewport || !content.IsChildOf(viewport))
                    throw new Exception("ScrollRect requires a descendant Viewport containing Content");
                if (viewport.GetComponent<UnityEngine.UI.RectMask2D>() == null && viewport.GetComponent<UnityEngine.UI.Mask>() == null)
                    throw new Exception("Viewport requires RectMask2D or Mask; add it first");
                if (viewport.GetComponent<UnityEngine.UI.Graphic>() == null)
                    throw new Exception("Viewport requires a Graphic (for example Image) for pointer events");
                if (!operation.horizontal && !operation.vertical) throw new Exception("Enable at least one scroll axis");
            }
            var component = go.AddComponent(type);
            if (component == null) throw new Exception("Unity could not add component");
            if (component is UnityEngine.UI.Button button)
            {
                button.targetGraphic = graphic;
                graphic.raycastTarget = true;
            }
            if (component is UnityEngine.UI.ScrollRect scroll)
            {
                scroll.viewport = (RectTransform)viewport;
                viewport.GetComponent<UnityEngine.UI.Graphic>().raycastTarget = true;
                scroll.content = (RectTransform)content;
                scroll.horizontal = operation.horizontal;
                scroll.vertical = operation.vertical;
            }
        }
    }
}
