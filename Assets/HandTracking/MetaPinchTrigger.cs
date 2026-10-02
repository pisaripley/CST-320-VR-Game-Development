using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Hands;

namespace Project.HandTracking
{
    /// <summary>Native Quest pinch events for gameplay; XRI handles pointing and selection.</summary>
    public sealed class MetaPinchTrigger : MonoBehaviour
    {
        public Handedness handedness = Handedness.Left;
        public UnityEvent triggerPressed = new UnityEvent();
        public UnityEvent triggerReleased = new UnityEvent();
        public UnityEvent<float> triggerValueChanged = new UnityEvent<float>();
        public bool IsTracked { get; private set; }
        public bool IsPressed { get; private set; }
        public float TriggerValue { get; private set; }
        bool waitingForRelease = true;

        void Update()
        {
            var hand = handedness == Handedness.Left ? MetaAimHand.left : MetaAimHand.right;
            var flags = hand != null && hand.added ? (MetaAimFlags)hand.aimFlags.ReadValue() : MetaAimFlags.None;
            IsTracked = hand != null && hand.added && hand.isTracked.isPressed && (flags & MetaAimFlags.Valid) != 0;
            bool blocked = !IsTracked || (flags & (MetaAimFlags.SystemGesture | MetaAimFlags.MenuPressed)) != 0;
            if (blocked)
            {
                waitingForRelease = true;
                SetState(false, 0f);
                return;
            }
            bool pressed = hand.indexPressed.isPressed;
            if (!pressed) waitingForRelease = false;
            SetState(!waitingForRelease && pressed, waitingForRelease ? 0f : hand.pinchStrengthIndex.ReadValue());
        }

        void OnDisable()
        {
            IsTracked = false;
            waitingForRelease = true;
            SetState(false, 0f);
        }

        void SetState(bool pressed, float value)
        {
            if (!Mathf.Approximately(TriggerValue, value))
            {
                TriggerValue = value;
                triggerValueChanged.Invoke(value);
            }
            if (IsPressed == pressed) return;
            IsPressed = pressed;
            if (pressed) triggerPressed.Invoke();
            else triggerReleased.Invoke();
        }
    }
}
