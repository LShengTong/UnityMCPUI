using System;
using NUnit.Framework;
using UnityEngine;

namespace UnityMcp.Editor.Tests
{
    public sealed class ReferenceOperationTests
    {
        GameObject root, target;
        [SetUp] public void Setup()
        {
            root = new GameObject("Root", typeof(RectTransform), typeof(UnityEngine.UI.ScrollRect));
            target = new GameObject("Viewport", typeof(RectTransform));
            target.transform.SetParent(root.transform, false);
        }
        [TearDown] public void Cleanup() => UnityEngine.Object.DestroyImmediate(root);
        UiBatchOperation Assignment() => new UiBatchOperation {
            action = "set_reference", component_type = "UnityEngine.UI.ScrollRect", field_path = "m_Viewport",
            target_component = "UnityEngine.RectTransform" };

        [Test] public void AssignsTypedSerializedReferenceAndClearsIt()
        {
            var operation = Assignment();
            SetUiReferenceOperation.Execute(root.transform, root.transform, target.transform, operation);
            Assert.That(root.GetComponent<UnityEngine.UI.ScrollRect>().viewport, Is.EqualTo(target.transform));
            operation.clear = true;
            SetUiReferenceOperation.Execute(root.transform, root.transform, null, operation);
            Assert.That(root.GetComponent<UnityEngine.UI.ScrollRect>().viewport, Is.Null);
        }
        [Test] public void RejectsWrongTypeAndMissingOrNonReferenceFields()
        {
            var operation = Assignment();
            operation.target_component = "";
            Assert.Throws<Exception>(() => SetUiReferenceOperation.Execute(root.transform, root.transform, target.transform, operation));
            operation.target_component = "UnityEngine.RectTransform";
            operation.field_path = "missing";
            Assert.Throws<Exception>(() => SetUiReferenceOperation.Execute(root.transform, root.transform, target.transform, operation));
            operation.field_path = "m_Horizontal";
            Assert.Throws<Exception>(() => SetUiReferenceOperation.Execute(root.transform, root.transform, target.transform, operation));
        }
        [Test] public void RejectsAmbiguousComponentsUnlessIndexIsExplicit()
        {
            root.AddComponent<UnityEngine.UI.Image>();
            root.AddComponent<UnityEngine.UI.Shadow>();
            root.AddComponent<UnityEngine.UI.Shadow>();
            var operation = Assignment();
            operation.component_type = "UnityEngine.UI.Shadow";
            Assert.That(Assert.Throws<Exception>(() => SetUiReferenceOperation.Execute(root.transform, root.transform, target.transform, operation)).Message,
                Does.Contain("Multiple components"));
            operation.component_index = 0;
            Assert.That(Assert.Throws<Exception>(() => SetUiReferenceOperation.Execute(root.transform, root.transform, target.transform, operation)).Message,
                Does.Not.Contain("Multiple components"));
        }
        [Test] public void RejectsMissingScriptWithoutAddingComponents()
        {
            int count = root.GetComponents<Component>().Length;
            Assert.Throws<Exception>(() => AddUiScriptOperation.Execute(root.transform, root.transform,
                new UiBatchOperation { script_path = "Assets/__MissingUnityMcpScript.cs" }));
            Assert.That(root.GetComponents<Component>().Length, Is.EqualTo(count));
        }
        [Test] public void CompilationSnapshotMatchesGeneratedProtocol()
        {
            var status = JsonUtility.FromJson<CompilationStatusReply>(EditorCompilationState.Json);
            Assert.DoesNotThrow(() => ResponseProtocolValidation.Validate(status));
            Assert.That(status.instance_id, Is.EqualTo(EditorInstanceInfo.InstanceId));
        }
    }
}
