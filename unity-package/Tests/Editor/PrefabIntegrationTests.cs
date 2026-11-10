using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace UnityMcp.Editor.Tests
{
    /// <summary>Exercises real handler execution and AssetDatabase operations on owned test assets.</summary>
    public sealed class PrefabIntegrationTests
    {
        string folder;
        string folderGuid;
        readonly HashSet<string> backupIds = new HashSet<string>();

        static double Deadline => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 + 60;
        static string RequestId => Guid.NewGuid().ToString("N");

        [SetUp]
        public void CreateOwnedAssetFolder()
        {
            folder = "Assets/__UnityMcpTests_" + Guid.NewGuid().ToString("N");
            folderGuid = AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            Assert.That(folderGuid, Is.Not.Empty);
            backupIds.Clear();
        }

        [TearDown]
        public void CleanOnlyOwnedAssetsAndBackups()
        {
            try
            {
                string project = Path.GetDirectoryName(Application.dataPath);
                foreach (string id in backupIds)
                {
                    // Never form a deletion path from an unvalidated response value.
                    Assert.That(Guid.TryParseExact(id, "N", out _), Is.True);
                    foreach (string extension in new[] { ".prefab", ".json" })
                    {
                        string file = Path.Combine(project, "Library", "UnityMcp", "Backups", id + extension);
                        if (File.Exists(file)) File.Delete(file);
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(folderGuid) && AssetDatabase.IsValidFolder(folder))
                {
                    // Refuse to remove a folder that was replaced during the test.
                    Assert.That(AssetDatabase.AssetPathToGUID(folder), Is.EqualTo(folderGuid));
                    Assert.That(AssetDatabase.DeleteAsset(folder), Is.True);
                }
            }
        }

        CreateUiPrefabReply Create(params string[] childNames)
        {
            if (childNames.Length == 0) childNames = new[] { "Panel" };
            var nodes = new UiPrefabNode[childNames.Length];
            for (int i = 0; i < nodes.Length; i++) nodes[i] = new UiPrefabNode { name = childNames[i] };
            var request = new CreateUiPrefabRequest {
                id = RequestId, deadline = Deadline, output_path = folder + "/Canvas.prefab",
                spec = new UiPrefabSpec { name = "Canvas", children = nodes }
            };
            RequestProtocolValidation.Validate(request);
            var reply = JsonUtility.FromJson<CreateUiPrefabReply>(CreateUiPrefabRequestHandler.Execute(request));
            Assert.That(reply.ok, Is.True, reply.error);
            Assert.That(reply.id, Is.EqualTo(request.id));
            ResponseProtocolValidation.Validate(reply);
            return reply;
        }

        BatchEditUiPrefabReply Edit(string path, string version, params UiBatchOperation[] operations)
        {
            var request = new BatchEditUiPrefabRequest {
                id = RequestId, deadline = Deadline, asset_path = path,
                expected_version = version, operations = operations
            };
            RequestProtocolValidation.Validate(request);
            var reply = JsonUtility.FromJson<BatchEditUiPrefabReply>(BatchEditUiPrefabRequestHandler.Execute(request));
            TrackBackup(reply.backup_id);
            Assert.That(reply.id, Is.EqualTo(request.id));
            return reply;
        }

        RestoreUiPrefabReply Restore(string backupId, string version)
        {
            var request = new RestoreUiPrefabRequest {
                id = RequestId, deadline = Deadline, backup_id = backupId, expected_version = version
            };
            RequestProtocolValidation.Validate(request);
            var reply = JsonUtility.FromJson<RestoreUiPrefabReply>(RestoreUiPrefabRequestHandler.Execute(request));
            TrackBackup(reply.backup_id);
            Assert.That(reply.id, Is.EqualTo(request.id));
            return reply;
        }

        void TrackBackup(string id)
        {
            if (!string.IsNullOrEmpty(id)) backupIds.Add(id);
        }

        [Test]
        public void CreateSavesRectTransformRootWithoutCanvasInfrastructure()
        {
            var reply = Create();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(reply.asset_path);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<Canvas>(), Is.Null);
            Assert.That(prefab.GetComponent<UnityEngine.UI.CanvasScaler>(), Is.Null);
            Assert.That(prefab.GetComponent<UnityEngine.UI.GraphicRaycaster>(), Is.Null);
            var rect = prefab.GetComponent<RectTransform>();
            Assert.That(rect.anchorMin, Is.EqualTo(Vector2.zero));
            Assert.That(rect.anchorMax, Is.EqualTo(Vector2.one));
            Assert.That(rect.sizeDelta, Is.EqualTo(Vector2.zero));
            Assert.That(prefab.activeSelf, Is.True);
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(prefab.transform.GetChild(0).name, Is.EqualTo("Panel"));
            Assert.That(reply.node_count, Is.EqualTo(1));
            Assert.That(UiPrefabVersion.Read(reply.asset_path), Has.Length.EqualTo(64));
        }

        [Test]
        public void AllFiveActionsSaveOnceAndRestorePreservesGuidAndOriginalContents()
        {
            var created = Create("Panel", "DeleteMe");
            string path = created.asset_path;
            string originalGuid = AssetDatabase.AssetPathToGUID(path);
            string originalVersion = UiPrefabVersion.Read(path);
            byte[] originalContents = File.ReadAllBytes(UiPrefabBackupStore.AssetFile(path));

            var edited = Edit(path, originalVersion,
                new UiBatchOperation { action = "modify", index_path = "0/0", property = "name", value = "Edited" },
                new UiBatchOperation { action = "add", parent_path = "0", node = new UiPrefabNode { name = "Added" } },
                new UiBatchOperation { action = "move", index_path = "0/0", parent_path = "0", sibling_index = 2 },
                new UiBatchOperation { action = "delete", index_path = "0/1" },
                new UiBatchOperation { action = "add_component", index_path = "0/0", component = "CanvasGroup" });

            Assert.That(edited.ok, Is.True, edited.error);
            Assert.That(edited.saved, Is.True);
            Assert.That(edited.executed_operations, Is.EqualTo(5));
            Assert.That(edited.changed_properties, Is.EqualTo(1));
            Assert.That(edited.node_operations, Is.EqualTo(3));
            Assert.That(edited.added_components, Is.EqualTo(1));
            Assert.That(edited.version, Is.Not.EqualTo(originalVersion));
            ResponseProtocolValidation.Validate(edited);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab.transform.childCount, Is.EqualTo(2));
            Assert.That(prefab.transform.GetChild(0).name, Is.EqualTo("Added"));
            Assert.That(prefab.transform.GetChild(1).name, Is.EqualTo("Edited"));
            Assert.That(prefab.transform.GetChild(1).GetComponent<CanvasGroup>(), Is.Not.Null);
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(UiPrefabBackupStore.List(path), Has.Length.EqualTo(1));
            CollectionAssert.AreEqual(originalContents, UiPrefabBackupStore.ReadContents(UiPrefabBackupStore.Read(edited.backup_id)));

            var restored = Restore(edited.backup_id, edited.version);
            Assert.That(restored.ok, Is.True, restored.error);
            ResponseProtocolValidation.Validate(restored);
            Assert.That(AssetDatabase.AssetPathToGUID(path), Is.EqualTo(originalGuid));
            CollectionAssert.AreEqual(originalContents, File.ReadAllBytes(UiPrefabBackupStore.AssetFile(path)));
            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab.transform.GetChild(0).name, Is.EqualTo("Panel"));
            Assert.That(prefab.transform.GetChild(1).name, Is.EqualTo("DeleteMe"));
            Assert.That(UiPrefabBackupStore.List(path), Has.Length.EqualTo(2));
        }

        [Test]
        public void FailedLaterActionDoesNotPersistEarlierChanges()
        {
            string path = Create().asset_path;
            string version = UiPrefabVersion.Read(path);
            byte[] original = File.ReadAllBytes(UiPrefabBackupStore.AssetFile(path));

            var reply = Edit(path, version,
                new UiBatchOperation { action = "delete", index_path = "0/0" },
                new UiBatchOperation { action = "modify", index_path = "0/0", property = "name", value = "MustNotSave" });

            Assert.That(reply.ok, Is.False);
            Assert.That(reply.saved, Is.False);
            Assert.That(reply.phase, Is.EqualTo("execute"));
            Assert.That(reply.executed_operations, Is.EqualTo(1));
            Assert.That(reply.failed_operation_index, Is.EqualTo(1));
            Assert.That(UiPrefabVersion.Read(path), Is.EqualTo(version));
            CollectionAssert.AreEqual(original, File.ReadAllBytes(UiPrefabBackupStore.AssetFile(path)));
            Assert.That(UiPrefabBackupStore.List(path), Is.Empty);
        }

        [Test]
        public void StaleVersionDoesNotModifyOrBackUpTheAsset()
        {
            string path = Create().asset_path;
            string version = UiPrefabVersion.Read(path);
            var reply = Edit(path, new string('0', 64),
                new UiBatchOperation { action = "modify", index_path = "0/0", property = "name", value = "MustNotSave" });

            Assert.That(reply.ok, Is.False);
            Assert.That(reply.saved, Is.False);
            Assert.That(reply.phase, Is.EqualTo("prepare"));
            Assert.That(UiPrefabVersion.Read(path), Is.EqualTo(version));
            Assert.That(UiPrefabBackupStore.List(path), Is.Empty);
        }

        [Test]
        public void RestoreWithStaleVersionLeavesTheEditedPrefabIntact()
        {
            string path = Create().asset_path;
            string originalVersion = UiPrefabVersion.Read(path);
            var edited = Edit(path, originalVersion,
                new UiBatchOperation { action = "modify", index_path = "0/0", property = "name", value = "Edited" });
            Assert.That(edited.ok, Is.True, edited.error);

            var restored = Restore(edited.backup_id, originalVersion);

            Assert.That(restored.ok, Is.False);
            Assert.That(restored.saved, Is.False);
            Assert.That(UiPrefabVersion.Read(path), Is.EqualTo(edited.version));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(path).transform.GetChild(0).name, Is.EqualTo("Edited"));
            Assert.That(UiPrefabBackupStore.List(path), Has.Length.EqualTo(1));
        }

        [Test]
        public void SavePreservesAnIntentionalInactiveRootAndUnitScale()
        {
            string path = Create().asset_path;
            var edited = Edit(path, UiPrefabVersion.Read(path),
                new UiBatchOperation { action = "modify", index_path = "0", property = "active", value = "false" });

            Assert.That(edited.ok, Is.True, edited.error);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab.activeSelf, Is.False);
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
        }

        [Serializable] sealed class SpriteBatchProbe
        {
            public bool ok;
            public SpriteInspectionReply[] results;
        }

        string CreateReusablePrefab()
        {
            var root = new GameObject("Reusable", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            try
            {
                var rect = (RectTransform)root.transform;
                rect.sizeDelta = new Vector2(234, 87);
                rect.anchoredPosition = new Vector2(19, 23);
                var child = new GameObject("Child", typeof(RectTransform));
                child.transform.SetParent(root.transform, false);
                var path = folder + "/Reusable.prefab";
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return path;
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void CreationReusesPrefabAndOnlyOverridesExplicitLayout()
        {
            string source = CreateReusablePrefab();
            string path = folder + "/Created.prefab";
            var request = new CreateUiPrefabRequest {
                id = RequestId, deadline = Deadline, output_path = path,
                spec = new UiPrefabSpec { name = "Canvas", children = new[] {
                    new UiPrefabNode { name = "Preserved", kind = "prefab", prefab_path = source },
                    new UiPrefabNode { name = "Overridden", kind = "prefab", prefab_path = source,
                        x = 0, has_x = true, width = 321, has_width = true }
                } }
            };
            RequestProtocolValidation.Validate(request);
            var reply = JsonUtility.FromJson<CreateUiPrefabReply>(CreateUiPrefabRequestHandler.Execute(request));
            Assert.That(reply.ok, Is.True, reply.error);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path).transform;
            var preserved = (RectTransform)root.GetChild(0);
            Assert.That(preserved.sizeDelta, Is.EqualTo(new Vector2(234, 87)));
            Assert.That(preserved.anchoredPosition, Is.EqualTo(new Vector2(19, 23)));
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(preserved), Is.EqualTo(source));
            var changed = (RectTransform)root.GetChild(1);
            Assert.That(changed.rect.width, Is.EqualTo(321).Within(.01));
            Assert.That(changed.anchoredPosition, Is.EqualTo(new Vector2(0, 23)));
            Assert.That(changed.childCount, Is.EqualTo(1));
        }

        [Test]
        public void BatchDuplicatesAndInstantiatesWithFinalCreatedPaths()
        {
            string source = CreateReusablePrefab();
            var created = Create("Panel");
            var contents = PrefabUtility.LoadPrefabContents(created.asset_path);
            try
            {
                var panel = contents.transform.GetChild(0).gameObject;
                panel.AddComponent<UnityEngine.UI.Button>().targetGraphic = panel.GetComponent<UnityEngine.UI.Image>();
                PrefabUtility.SaveAsPrefabAsset(contents, created.asset_path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            var reply = Edit(created.asset_path, UiPrefabVersion.Read(created.asset_path),
                new UiBatchOperation { action = "duplicate", index_path = "0/0", parent_path = "0", name = "Copy" },
                new UiBatchOperation { action = "instantiate_prefab", prefab_path = source, parent_path = "0", name = "Inserted", sibling_index = 0 });
            Assert.That(reply.ok, Is.True, reply.error);
            Assert.That(reply.created_nodes, Has.Length.EqualTo(2));
            Assert.That(reply.created_nodes[0].operation_index, Is.EqualTo(0));
            Assert.That(reply.created_nodes[0].index_path, Is.EqualTo("0/2"));
            Assert.That(reply.created_nodes[1].index_path, Is.EqualTo("0/0"));
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(created.asset_path).transform;
            Assert.That(root.GetChild(2).name, Is.EqualTo("Copy"));
            Assert.That(root.GetChild(2).GetComponent<UnityEngine.UI.Button>().targetGraphic,
                Is.EqualTo(root.GetChild(2).GetComponent<UnityEngine.UI.Image>()));
            Assert.That(root.GetChild(0).GetComponent<UnityEngine.UI.Image>(), Is.Not.Null);
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root.GetChild(0)), Is.EqualTo(source));
        }

        [Test]
        public void RejectsDirectAndIndirectPrefabCyclesWithoutSaving()
        {
            var created = Create("Panel");
            string version = UiPrefabVersion.Read(created.asset_path);
            var direct = Edit(created.asset_path, version, new UiBatchOperation {
                action = "instantiate_prefab", prefab_path = created.asset_path, parent_path = "0" });
            Assert.That(direct.ok, Is.False);
            Assert.That(direct.saved, Is.False);
            Assert.That(direct.error, Does.Contain("circular dependency"));
            string wrapper = folder + "/Wrapper.prefab";
            var root = new GameObject("Wrapper", typeof(RectTransform));
            try
            {
                PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(created.asset_path), root.transform);
                PrefabUtility.SaveAsPrefabAsset(root, wrapper);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
            var indirect = Edit(created.asset_path, version, new UiBatchOperation {
                action = "add", parent_path = "0", node = new UiPrefabNode {
                    name = "Cycle", kind = "prefab", prefab_path = wrapper } });
            Assert.That(indirect.ok, Is.False);
            Assert.That(indirect.error, Does.Contain("circular dependency"));
            Assert.That(UiPrefabVersion.Read(created.asset_path), Is.EqualTo(version));
        }

        [Test]
        public void DuplicateOfRemovedTargetDiscardsEarlierEdits()
        {
            var created = Create("Panel");
            string version = UiPrefabVersion.Read(created.asset_path);
            var reply = Edit(created.asset_path, version,
                new UiBatchOperation { action = "delete", index_path = "0/0" },
                new UiBatchOperation { action = "duplicate", index_path = "0/0", parent_path = "0" });
            Assert.That(reply.ok, Is.False);
            Assert.That(reply.saved, Is.False);
            Assert.That(reply.failed_operation_index, Is.EqualTo(1));
            Assert.That(UiPrefabVersion.Read(created.asset_path), Is.EqualTo(version));
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(created.asset_path).transform.childCount, Is.EqualTo(1));
        }

        [Test]
        public void InspectHandlerSerializesGeneratedDerivedSpriteDetails()
        {
            string path = folder + "/Sprite.png";
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try { File.WriteAllBytes(UiPrefabBackupStore.AssetFile(path), texture.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(texture); }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            var request = new InspectUiAssetsRequest {
                id = RequestId, deadline = Deadline,
                assets = new[] { new UiAssetInspection { asset_type = "sprite", asset_path = path } }
            };
            RequestProtocolValidation.Validate(request);
            string json = InspectUiAssetsRequestHandler.Execute(request);
            // A probe with the concrete generated type detects lost derived fields.
            var reply = JsonUtility.FromJson<SpriteBatchProbe>(json);

            Assert.That(reply.ok, Is.True, json);
            Assert.That(reply.results, Has.Length.EqualTo(1));
            Assert.That(reply.results[0].ok, Is.True, json);
            Assert.That(reply.results[0].sprite, Is.Not.Null, json);
            Assert.That(reply.results[0].sprite.size.x, Is.EqualTo(2));
            Assert.That(reply.results[0].sprite.rect.width, Is.EqualTo(2));
            Assert.That(reply.results[0].asset.asset_path, Is.EqualTo(path));
            ResponseProtocolValidation.Validate(reply.results[0]);
        }

        [Test]
        public void SerializedReferenceSurvivesSaveAndTypeFailureDoesNotSave()
        {
            var created = Create("Viewport");
            var contents = PrefabUtility.LoadPrefabContents(created.asset_path);
            try
            {
                contents.AddComponent<UnityEngine.UI.ScrollRect>();
                PrefabUtility.SaveAsPrefabAsset(contents, created.asset_path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            string version = UiPrefabVersion.Read(created.asset_path);
            var reply = Edit(created.asset_path, version, new UiBatchOperation {
                action = "set_reference", index_path = "0", component_type = "UnityEngine.UI.ScrollRect",
                field_path = "m_Viewport", target_path = "0/0", target_component = "UnityEngine.RectTransform" });
            Assert.That(reply.ok, Is.True, reply.error);
            Assert.That(reply.assigned_references, Is.EqualTo(1));
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(created.asset_path);
            Assert.That(asset.GetComponent<UnityEngine.UI.ScrollRect>().viewport, Is.EqualTo(asset.transform.GetChild(0)));
            version = UiPrefabVersion.Read(created.asset_path);
            var failed = Edit(created.asset_path, version, new UiBatchOperation {
                action = "set_reference", index_path = "0", component_type = "UnityEngine.UI.ScrollRect",
                field_path = "m_Viewport", target_path = "0/0" });
            Assert.That(failed.ok, Is.False);
            Assert.That(failed.error, Does.Contain("type mismatch"));
            Assert.That(UiPrefabVersion.Read(created.asset_path), Is.EqualTo(version));
        }

        [Test]
        public void GridAndImageSettingsSurviveCreationAndBatchSaving()
        {
            string path = folder + "/VisualProperties.prefab";
            var request = new CreateUiPrefabRequest {
                id = RequestId, deadline = Deadline, output_path = path,
                spec = new UiPrefabSpec { name = "Canvas", children = new[] {
                    new UiPrefabNode { name = "Grid", layout = "grid", grid_constraint = "FixedColumnCount",
                        grid_constraint_count = 4, grid_cell_size = new UiOffset { x = 70, y = 80 },
                        child_alignment = "UpperLeft", children = new[] {
                            new UiPrefabNode { name = "Progress", kind = "image", image_type = "Filled",
                                fill_method = "Horizontal", fill_amount = .4f, preserve_aspect = true,
                                has_raycast_target = true, raycast_target = false }
                        } }
                } }
            };
            RequestProtocolValidation.Validate(request);
            var created = JsonUtility.FromJson<CreateUiPrefabReply>(CreateUiPrefabRequestHandler.Execute(request));
            Assert.That(created.ok, Is.True, created.error);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var grid = asset.transform.GetChild(0).GetComponent<UnityEngine.UI.GridLayoutGroup>();
            Assert.That(grid.constraintCount, Is.EqualTo(4));
            Assert.That(grid.cellSize, Is.EqualTo(new Vector2(70, 80)));
            Assert.That(grid.childAlignment, Is.EqualTo(TextAnchor.UpperLeft));
            var image = grid.transform.GetChild(0).GetComponent<UnityEngine.UI.Image>();
            Assert.That(image.fillAmount, Is.EqualTo(.4f));
            Assert.That(image.preserveAspect, Is.True);
            Assert.That(image.raycastTarget, Is.False);
            var edited = Edit(path, UiPrefabVersion.Read(path),
                new UiBatchOperation { action = "modify", index_path = "0/0", property = "grid_constraint_count", value = "6" },
                new UiBatchOperation { action = "modify", index_path = "0/0/0", property = "fill_amount", value = "0.8" });
            Assert.That(edited.ok, Is.True, edited.error);
            asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            grid = asset.transform.GetChild(0).GetComponent<UnityEngine.UI.GridLayoutGroup>();
            Assert.That(grid.constraintCount, Is.EqualTo(6));
            Assert.That(grid.transform.GetChild(0).GetComponent<UnityEngine.UI.Image>().fillAmount, Is.EqualTo(.8f));
        }
    }
}
