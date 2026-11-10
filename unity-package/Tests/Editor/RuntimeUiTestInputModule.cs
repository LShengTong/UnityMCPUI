namespace UnityMcp.Editor.Tests
{
    // Test fixture only; callback tests do not simulate pointer input.
    public sealed class RuntimeUiTestInputModule : UnityEngine.EventSystems.BaseInputModule
    {
        public override void Process() { }
    }
}
