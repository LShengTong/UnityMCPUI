using System;
using NUnit.Framework;
using UnityEngine;

namespace UnityMcp.Editor.Tests
{
    /// <summary>Run with the real Unity JsonUtility, not a substitute JSON serializer.</summary>
    public sealed class ProtocolSerializationTests
    {
        [Test]
        public void EveryGeneratedRequestTypeHasAManuallyRegisteredHandler()
        {
            var registry = new EditorRequestRegistry(new EditorTaskQueue());
            foreach (EditorRequestType type in Enum.GetValues(typeof(EditorRequestType)))
                Assert.That(registry.Get(type), Is.Not.Null, type.ToString());
        }

        [Test]
        public void MissingNodeFieldsUseProtocolDefaults()
        {
            var node = JsonUtility.FromJson<UiPrefabNode>("{\"name\":\"Panel\"}");

            Assert.That(node.kind, Is.EqualTo("panel"));
            Assert.That(node.width, Is.EqualTo(320));
            Assert.That(node.height, Is.EqualTo(80));
            Assert.That(node.anchor, Is.EqualTo("center"));
            Assert.That(node.padding, Is.EqualTo(16));
            Assert.That(node.children, Is.Empty);
            Assert.DoesNotThrow(() => RequestProtocolValidation.Validate(node));
        }

        [Test]
        public void OmittedOffsetsAreIgnoredEvenIfUnityMaterializesAnObject()
        {
            var node = JsonUtility.FromJson<UiPrefabNode>("{\"name\":\"Panel\"}");

            Assert.That(node.has_offset_min, Is.False);
            Assert.That(node.has_offset_max, Is.False);
            // Null alone is not the contract: the generated presence bits are authoritative.
            Assert.DoesNotThrow(() => RequestProtocolValidation.Validate(node));
        }

        [Test]
        public void ExplicitZeroOffsetIsPreserved()
        {
            var node = JsonUtility.FromJson<UiPrefabNode>(
                "{\"name\":\"Panel\",\"has_offset_min\":true,\"offset_min\":{\"x\":0,\"y\":0}}");

            Assert.That(node.has_offset_min, Is.True);
            Assert.That(node.offset_min, Is.Not.Null);
            Assert.That(node.offset_min.x, Is.Zero);
            Assert.That(node.offset_min.y, Is.Zero);
            Assert.DoesNotThrow(() => RequestProtocolValidation.Validate(node));
        }

        [Test]
        public void IncompleteExplicitOffsetIsRejected()
        {
            var node = JsonUtility.FromJson<UiPrefabNode>(
                "{\"name\":\"Panel\",\"has_offset_min\":true,\"offset_min\":{\"x\":0}}");

            Assert.Throws<Exception>(() => RequestProtocolValidation.Validate(node));
        }

        [Test]
        public void NestedCreateRequestParsesAndImplementsRequestMetadata()
        {
            string id = new string('a', 32);
            var request = JsonUtility.FromJson<CreateUiPrefabRequest>(
                "{\"id\":\"" + id + "\",\"deadline\":1234,\"output_path\":\"Assets/Test.prefab\"," +
                "\"spec\":{\"name\":\"Canvas\",\"children\":[{\"name\":\"Panel\",\"children\":[{\"name\":\"Child\"}]}]}}");

            Assert.That(((IEditorRequest)request).Id, Is.EqualTo(id));
            Assert.That(((IEditorRequest)request).Deadline, Is.EqualTo(1234));
            Assert.That(request.spec.children[0].children[0].name, Is.EqualTo("Child"));
            Assert.DoesNotThrow(() => RequestProtocolValidation.Validate(request));
        }

        [Test]
        public void BatchMoveUsesNegativeOneAsDefaultInsertionIndex()
        {
            var operation = JsonUtility.FromJson<UiBatchOperation>(
                "{\"action\":\"move\",\"index_path\":\"0/0\",\"parent_path\":\"0\"}");

            Assert.That(operation.sibling_index, Is.EqualTo(-1));
            Assert.That(operation.world_position_stays, Is.False);
            Assert.DoesNotThrow(() => RequestProtocolValidation.Validate(operation));
        }

        [TestCase("{\"name\":\"Panel\",\"kind\":\"unknown\"}")]
        [TestCase("{\"name\":\"Panel\",\"width\":0}")]
        public void InvalidNodeValuesAreRejectedAfterParsing(string json)
        {
            var node = JsonUtility.FromJson<UiPrefabNode>(json);
            Assert.Throws<Exception>(() => RequestProtocolValidation.Validate(node));
        }

        [Test]
        public void ImageRequestKeepsBorderAndImportDefaults()
        {
            var request = JsonUtility.FromJson<ImportUiImageRequest>(
                "{\"id\":\"" + new string('a', 32) + "\",\"deadline\":1234," +
                "\"asset_path\":\"Assets/Image.png\",\"image_base64\":\"AA==\"}");

            CollectionAssert.AreEqual(new[] { 0, 0, 0, 0 }, request.border);
            Assert.That(request.pixels_per_unit, Is.EqualTo(100));
            Assert.That(request.filter_mode, Is.EqualTo("bilinear"));
            Assert.DoesNotThrow(() => RequestProtocolValidation.Validate(request));
        }

        [Test]
        public void SuccessReplyRoundTripsThroughJsonUtility()
        {
            var expected = new CreateUiPrefabReply {
                id = new string('a', 32), instance_id = "test", ok = true,
                asset_path = "Assets/Test.prefab", node_count = 2, warning = "Visual only"
            };

            var actual = JsonUtility.FromJson<CreateUiPrefabReply>(JsonUtility.ToJson(expected));

            Assert.That(actual.id, Is.EqualTo(expected.id));
            Assert.That(actual.instance_id, Is.EqualTo(expected.instance_id));
            Assert.That(actual.node_count, Is.EqualTo(2));
            Assert.That(actual.warning, Is.EqualTo("Visual only"));
            Assert.DoesNotThrow(() => ResponseProtocolValidation.Validate(actual));
        }

        [Test]
        public void FailedEditReplyKeepsRecoveryInformation()
        {
            var expected = new BatchEditUiPrefabReply {
                id = new string('a', 32), instance_id = "test", ok = false,
                error = "Saved but metadata failed", saved = true,
                backup_id = new string('b', 32), phase = "save", failed_operation_index = -1
            };

            var actual = JsonUtility.FromJson<BatchEditUiPrefabReply>(JsonUtility.ToJson(expected));

            Assert.That(actual.ok, Is.False);
            Assert.That(actual.saved, Is.True);
            Assert.That(actual.backup_id, Is.EqualTo(expected.backup_id));
            Assert.That(actual.failed_operation_index, Is.EqualTo(-1));
            Assert.DoesNotThrow(() => ResponseProtocolValidation.Validate(actual));
        }
    }
}
