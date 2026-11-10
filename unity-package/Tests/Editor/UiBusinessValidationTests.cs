using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UnityMcp.Editor.Tests
{
    public sealed class UiBusinessValidationTests
    {
        static void Validate(params UiPrefabNode[] nodes)
        {
            int count = 0;
            UiPrefabBridge.Validate(nodes, 1, ref count);
        }

        [Test]
        public void RejectsInvalidNodeRelationshipsBeforeCreatingObjects()
        {
            Assert.Throws<Exception>(() => Validate(new UiPrefabNode { name = "Same" }, new UiPrefabNode { name = "Same" }));
            StringAssert.Contains("nonempty text", Assert.Throws<Exception>(() => Validate(new UiPrefabNode { name = "Label", kind = "text" })).Message);
            Assert.Throws<Exception>(() => Validate(new UiPrefabNode { name = "Button", kind = "button", children = new[] { new UiPrefabNode { name = "Child" } } }));
            Assert.Throws<Exception>(() => Validate(new UiPrefabNode { name = "Prefab", kind = "prefab" }));
            Assert.Throws<Exception>(() => Validate(new UiPrefabNode { name = "Panel", prefab_path = "Assets/Other.prefab" }));
            Assert.Throws<Exception>(() => Validate(new UiPrefabNode { name = "Panel", font_size_min = 50, font_size_max = 20 }));
            Assert.Throws<Exception>(() => Validate(new UiPrefabNode { name = "Panel", fill_method = "Horizontal", fill_origin = 2 }));
            Assert.Throws<Exception>(() => Validate(new UiPrefabNode { name = "Grid", layout = "grid", grid_cell_size = new UiOffset { x = 0, y = 100 } }));
        }

        [Test]
        public void TreeLimitsIncludeAllDescendantsAndAllowTwelveLevels()
        {
            Validate(Enumerable.Range(0, 128).Select(i => new UiPrefabNode { name = "Node" + i }).ToArray());
            Assert.Throws<Exception>(() => Validate(Enumerable.Range(0, 129).Select(i => new UiPrefabNode { name = "Node" + i }).ToArray()));
            var node = new UiPrefabNode { name = "Leaf" };
            for (int i = 1; i < 12; i++) node = new UiPrefabNode { name = "Parent", children = new[] { node } };
            Validate(node);
            var tooDeep = new UiPrefabNode { name = "Root", children = new[] { node } };
            Assert.Throws<Exception>(() => Validate(tooDeep));
        }

        [Test]
        public void OtherComponentsRejectScrollRectOnlyArgumentsBeforeMutation()
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            try
            {
                StringAssert.Contains("only for ScrollRect", Assert.Throws<Exception>(() => AddUiComponentOperation.Execute(
                    go.transform, go.transform, null, null, new UiBatchOperation { component = "Image", viewport_path = "0/0" })).Message);
                Assert.That(go.GetComponent<UnityEngine.UI.Image>(), Is.Null);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
