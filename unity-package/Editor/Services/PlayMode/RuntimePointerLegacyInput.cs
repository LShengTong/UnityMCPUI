using UnityEngine;
using UnityEngine.EventSystems;

namespace UnityMcp.Editor
{
    internal sealed class RuntimePointerLegacyInput : BaseInput
    {
        readonly int[] pressedFrame = { -1, -1, -1 };
        readonly int[] releasedFrame = { -1, -1, -1 };
        ushort buttons;
        Vector2 position, scroll;
        int scrollFrame = -1;

        internal void SetState(Vector2 nextPosition, ushort nextButtons, Vector2 wheel)
        {
            int frame = Time.frameCount + 1;
            for (int index = 0; index < 3; index++)
            {
                int mask = 1 << index;
                if ((buttons & mask) == 0 && (nextButtons & mask) != 0) pressedFrame[index] = frame;
                if ((buttons & mask) != 0 && (nextButtons & mask) == 0) releasedFrame[index] = frame;
            }
            buttons = nextButtons;
            position = nextPosition;
            scroll = wheel;
            scrollFrame = frame;
        }

        public override bool mousePresent => true;
        public override Vector2 mousePosition => position;
        public override Vector2 mouseScrollDelta => Time.frameCount == scrollFrame ? scroll : Vector2.zero;
        public override bool GetMouseButton(int button) => (buttons & (1 << button)) != 0;
        public override bool GetMouseButtonDown(int button) => Time.frameCount == pressedFrame[button];
        public override bool GetMouseButtonUp(int button) => Time.frameCount == releasedFrame[button];
        public override bool touchSupported => false;
        public override int touchCount => 0;
        public override float GetAxisRaw(string axisName) => 0;
        public override bool GetButtonDown(string buttonName) => false;
    }
}
