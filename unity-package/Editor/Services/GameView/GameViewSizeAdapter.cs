using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor
{
    // All version-sensitive Game view reflection is confined to this adapter.
    internal sealed class GameViewSizeAdapter
    {
        const BindingFlags Members = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        readonly Type viewType;
        readonly object sizes;
        readonly object sizeGroup;
        readonly object groupType;
        internal readonly EditorWindow Window;
        internal string Group => groupType.ToString();

        internal GameViewSizeAdapter(bool create, ulong windowId = 0)
        {
            viewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView", true);
            if (windowId != 0)
                Window = EditorUtility.EntityIdToObject(EntityId.FromULong(windowId)) as EditorWindow;
            else
            {
                Window = Resources.FindObjectsOfTypeAll(viewType).Cast<EditorWindow>()
                    .OrderByDescending(window => window == EditorWindow.focusedWindow).FirstOrDefault();
                if (Window == null && create) Window = EditorWindow.GetWindow(viewType, false, "Game", false);
            }
            if (Window == null || !viewType.IsInstanceOfType(Window)) throw new Exception("Open a Game view before querying or restoring resolution");
            var sizesType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameViewSizes", true);
            sizes = sizesType.BaseType.GetProperty("instance", Members).GetValue(null);
            groupType = Property(sizes, "currentGroupType");
            sizeGroup = Invoke(sizes, "GetGroup", groupType);
            // Fail before mutation if a required API changed in this Editor version.
            RequireProperty(viewType, "selectedSizeIndex");
            RequireProperty(viewType, "targetRenderSize");
            RequireMethod(viewType, "SizeSelectionCallback");
        }

        internal int Selected => (int)Property(Window, "selectedSizeIndex");
        internal Vector2 Actual => EditorApplication.isPlaying
            ? new Vector2(Screen.width, Screen.height) : (Vector2)Property(Window, "targetRenderSize");
        internal ulong WindowId => EntityId.ToULong(Window.GetEntityId());

        internal GameViewPreset[] Presets()
        {
            int count = (int)Invoke(sizeGroup, "GetTotalCount");
            int builtin = (int)Invoke(sizeGroup, "GetBuiltinCount");
            return Enumerable.Range(0, count).Select(index => {
                var size = Invoke(sizeGroup, "GetGameViewSize", index);
                return new GameViewPreset {
                    index = index, name = (string)Property(size, "displayText"),
                    size_type = Property(size, "sizeType").ToString(),
                    width = (int)Property(size, "width"), height = (int)Property(size, "height"), is_custom = index >= builtin
                };
            }).ToArray();
        }

        internal int FixedPreset(int width, int height)
        {
            if (width < 10 || height < 10 || width > 2048 || height > 2048)
                throw new Exception("New fixed Game view resolutions require 10..2048 pixels per side");
            var match = Presets().FirstOrDefault(item => item.size_type == "FixedResolution" && item.width == width && item.height == height);
            if (match != null) return match.index;
            var assembly = typeof(UnityEditor.Editor).Assembly;
            var sizeType = assembly.GetType("UnityEditor.GameViewSize", true);
            var enumType = assembly.GetType("UnityEditor.GameViewSizeType", true);
            var constructor = sizeType.GetConstructor(Members, null, new[] { enumType, typeof(int), typeof(int), typeof(string) }, null)
                ?? throw new NotSupportedException("This Unity version has no compatible GameViewSize constructor");
            var size = constructor.Invoke(new object[] { Enum.Parse(enumType, "FixedResolution"), width, height, "UnityMCP " + width + "x" + height });
            Invoke(sizeGroup, "AddCustomSize", size);
            Invoke(sizes, "SaveToHDD");
            return Presets().Length - 1;
        }

        internal void Select(int index)
        {
            CheckGroup();
            if (index < 0 || index >= Presets().Length) throw new Exception("Game view preset index is out of range");
            Invoke(Window, "SizeSelectionCallback", index, null);
            Window.Repaint();
            EditorApplication.QueuePlayerLoopUpdate();
        }

        internal void CheckGroup()
        {
            if (Window == null || !Equals(Property(sizes, "currentGroupType"), groupType))
                throw new Exception("Game view window or platform preset group changed during the operation");
        }

        static PropertyInfo RequireProperty(Type type, string name) => type.GetProperty(name, Members)
            ?? throw new NotSupportedException("Unsupported Unity Game view API: " + name);
        static MethodInfo RequireMethod(Type type, string name) => type.GetMethods(Members).SingleOrDefault(method => method.Name == name)
            ?? throw new NotSupportedException("Unsupported Unity Game view API: " + name);
        static object Property(object target, string name) => RequireProperty(target.GetType(), name).GetValue(target);
        static object Invoke(object target, string name, params object[] arguments) => RequireMethod(target.GetType(), name).Invoke(target, arguments);
    }
}
