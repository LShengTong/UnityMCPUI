using System;
using NUnit.Framework;
using UnityEngine;

namespace UnityMcp.Editor.Tests
{
    public sealed class ServiceBoundaryTests
    {
        [Test]
        public void BatchServiceReturnsStructuredFailureAndHandlerAddsEnvelope()
        {
            var request = new BatchEditUiPrefabRequest { id = Guid.NewGuid().ToString("N"),
                asset_path = "Assets/Unused.prefab", operations = Array.Empty<UiBatchOperation>() };
            var result = BatchEditUiPrefabService.Execute(request.deadline, request.asset_path, request.expected_version, request.operations);
            Assert.That(result.ok, Is.False);
            Assert.That(result.saved, Is.False);
            Assert.That(result.phase, Is.EqualTo("prepare"));
            Assert.That(result.error, Does.Contain("1..128"));
            Assert.That(result.GetType().GetField("id"), Is.Null);
            Assert.That(result.GetType().GetField("instance_id"), Is.Null);

            var wire = JsonUtility.FromJson<BatchEditUiPrefabReply>(BatchEditUiPrefabRequestHandler.Execute(request));
            Assert.That(wire.id, Is.EqualTo(request.id));
            Assert.That(wire.instance_id, Is.EqualTo(EditorInstanceInfo.InstanceId));
            Assert.That(wire.error, Is.EqualTo(result.error));
            Assert.That(wire.phase, Is.EqualTo(result.phase));
        }

        [Test]
        public void CreateServiceChecksDeadlineWithoutHttpAndHandlerSerializesResult()
        {
            var request = new CreateUiPrefabRequest { id = Guid.NewGuid().ToString("N"), deadline = 0 };
            var result = CreateUiPrefabService.Execute(request.deadline, request.output_path, request.spec);
            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("expired"));
            Assert.That(result.GetType().GetField("id"), Is.Null);
            var wire = JsonUtility.FromJson<CreateUiPrefabReply>(CreateUiPrefabRequestHandler.Execute(request));
            Assert.That(wire.id, Is.EqualTo(request.id));
            Assert.That(wire.error, Is.EqualTo(result.error));
        }

        [Test]
        public void PreviewServiceKeepsRenderTargetOnFailure()
        {
            var previous = RenderTexture.active;
            var result = PreviewUiPrefabService.Execute(0, null, 1280, 720, "#202020");
            Assert.That(result.ok, Is.False);
            Assert.That(result.error, Does.Contain("expired"));
            Assert.That(RenderTexture.active, Is.EqualTo(previous));
            Assert.That(result.GetType().GetField("id"), Is.Null);
        }
    }
}
