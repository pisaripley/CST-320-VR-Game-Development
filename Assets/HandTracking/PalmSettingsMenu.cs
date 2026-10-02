using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;

namespace Project.HandTracking
{
    public sealed class PalmSettingsMenu : MonoBehaviour
    {
        public AssignmentHub hub;
        public bool IsOpen => panel != null && panel.activeSelf;
        public bool Dragging { get; set; }
        GameObject panel;
        Text volumeText;
        Text toneText;
        InputAction controllerMenu;
        readonly List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();
        float gestureTime, lastGesture, suppressUntil;
        bool onPalm;
        bool gestureArmed = true;
        void Start()
        {
            controllerMenu = new InputAction("Settings", InputActionType.Button, "<XRController>{LeftHand}/primaryButton"); controllerMenu.Enable();
            var canvas = PuzzleUI.Canvas("Palm settings menu", null, hub.rig.Camera, new Vector2(540, 500), 0.0008f);
            panel = canvas.gameObject;
            PuzzleUI.Text(panel.transform, "SETTINGS", new Vector2(0, 205), new Vector2(500, 55), 36);
            volumeText = PuzzleUI.Text(panel.transform, "", new Vector2(0, 140), new Vector2(500, 45), 26);
            MakeSlider("Master volume", 85, PlayerPrefs.GetFloat("Assignment.MasterVolume", 0.7f), SetVolume);
            toneText = PuzzleUI.Text(panel.transform, "", new Vector2(0, 25), new Vector2(500, 45), 26);
            MakeSlider("Puzzle tones", -30, PlayerPrefs.GetFloat("Assignment.ToneVolume", 1f), SetToneVolume);
            PuzzleUI.Text(panel.transform, "Point and hold trigger / pinch, then drag.", new Vector2(0, -100), new Vector2(510, 45), 20);
            PuzzleUI.Button(panel.transform, "RETURN TO START", new Vector2(0, -155), new Vector2(420, 48), () => { hub.ReturnToStart(); Close(); });
            PuzzleUI.Button(panel.transform, "CLOSE", new Vector2(0, -215), new Vector2(420, 45), Close);
            panel.SetActive(false);
        }
        void MakeSlider(string title, float y, float initial, UnityEngine.Events.UnityAction<float> change)
        {
            var track = PuzzleUI.Rect(title + " slider", panel.transform, new Vector2(0, y), new Vector2(440, 55));
            var background = track.gameObject.AddComponent<Image>(); background.color = new Color(0.1f, 0.18f, 0.25f);
            var fillArea = PuzzleUI.Rect("Fill area", track, Vector2.zero, new Vector2(405, 16));
            var fill = PuzzleUI.Rect("Fill", fillArea, Vector2.zero, new Vector2(0, 16));
            fill.gameObject.AddComponent<Image>().color = new Color(0.15f, 0.8f, 0.95f);
            var handleArea = PuzzleUI.Rect("Handle area", track, Vector2.zero, new Vector2(405, 55));
            var handle = PuzzleUI.Rect("Drag handle", handleArea, Vector2.zero, new Vector2(35, 55));
            var knob = handle.gameObject.AddComponent<Image>(); knob.color = Color.white;
            var slider = track.gameObject.AddComponent<Slider>(); slider.minValue = 0; slider.maxValue = 1;
            slider.fillRect = fill; slider.handleRect = handle; slider.targetGraphic = knob;
            slider.value = initial;
            change(slider.value); slider.onValueChanged.AddListener(change);
            track.gameObject.AddComponent<PalmSliderDrag>().menu = this;
        }
        void SetToneVolume(float value)
        {
            foreach (var puzzle in FindObjectsByType<EchoOrbsPuzzle>(FindObjectsSortMode.None)) puzzle.SetToneVolume(value);
            PlayerPrefs.SetFloat("Assignment.ToneVolume", value);
            toneText.text = "PUZZLE TONES  " + Mathf.RoundToInt(value * 100) + "%";
        }
        void SetVolume(float value)
        {
            AudioListener.volume = value; PlayerPrefs.SetFloat("Assignment.MasterVolume", value);
            volumeText.text = "MASTER VOLUME  " + Mathf.RoundToInt(value * 100) + "%";
        }
        public void OpenAtHead()
        {
            if (panel == null) return;
            onPalm = false; panel.SetActive(true);
            var head = hub.rig.Camera.transform;
            panel.transform.position = head.position + head.forward * 0.65f - Vector3.up * 0.08f;
            panel.transform.rotation = Quaternion.LookRotation(panel.transform.position - head.position, Vector3.up);
        }
        public void Close()
        {
            if (panel != null) panel.SetActive(false);
            Dragging = false; onPalm = false; gestureTime = 0; suppressUntil = Time.unscaledTime + 1;
            gestureArmed = false;
            PlayerPrefs.Save();
        }
        void Update()
        {
            if (panel == null) return;
            if (controllerMenu.WasPressedThisFrame()) { if (IsOpen) Close(); else OpenAtHead(); }
            bool palmUp = TryPalm(out Vector3 position);
            if (palmUp)
            {
                gestureTime += Time.unscaledDeltaTime; lastGesture = Time.unscaledTime;
                if (!IsOpen && gestureArmed && gestureTime > 0.35f && Time.unscaledTime > suppressUntil)
                { onPalm = true; panel.SetActive(true); panel.transform.position = position + Vector3.up * 0.15f; }
                if (IsOpen && onPalm && !Dragging)
                {
                    Vector3 target = position + Vector3.up * 0.15f;
                    panel.transform.position = Vector3.Lerp(panel.transform.position, target, 1 - Mathf.Exp(-12 * Time.unscaledDeltaTime));
                    panel.transform.rotation = Quaternion.LookRotation(panel.transform.position - hub.rig.Camera.transform.position, Vector3.up);
                }
            }
            else { gestureTime = 0; gestureArmed = true; }
            if (IsOpen && onPalm && !Dragging && Time.unscaledTime - lastGesture > 0.8f) Close();
        }
        bool TryPalm(out Vector3 position)
        {
            position = default;
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var subsystem in subsystems)
            {
                if (!subsystem.running || !subsystem.leftHand.isTracked) continue;
                var hand = subsystem.leftHand;
                if (!hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm) ||
                    !hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist) ||
                    !hand.GetJoint(XRHandJointID.MiddleTip).TryGetPose(out var middle) ||
                    !hand.GetJoint(XRHandJointID.IndexMetacarpal).TryGetPose(out var index) ||
                    !hand.GetJoint(XRHandJointID.LittleMetacarpal).TryGetPose(out var little)) return false;
                var space = hub.rig.Camera.transform.parent;
                Vector3 along = (middle.position - wrist.position).normalized;
                // Left-hand joint order: this cross product points out through the palm.
                Vector3 normal = Vector3.Cross(index.position - little.position, along).normalized;
                normal = space.TransformDirection(normal);
                if (Vector3.Dot(normal, Vector3.up) < 0.65f || Mathf.Abs(Vector3.Dot(space.TransformDirection(along), Vector3.up)) > 0.45f) return false;
                foreach (var pair in new[] { (XRHandJointID.IndexTip, XRHandJointID.IndexProximal), (XRHandJointID.MiddleTip, XRHandJointID.MiddleProximal), (XRHandJointID.RingTip, XRHandJointID.RingProximal), (XRHandJointID.LittleTip, XRHandJointID.LittleProximal) })
                {
                    if (!hand.GetJoint(pair.Item1).TryGetPose(out var tip) || !hand.GetJoint(pair.Item2).TryGetPose(out var proximal)) return false;
                    if (Vector3.Distance(tip.position, wrist.position) < Vector3.Distance(proximal.position, wrist.position) * 1.35f) return false;
                }
                position = space.TransformPoint(palm.position); return true;
            }
            return false;
        }
        void OnDestroy()
        {
            controllerMenu?.Dispose(); if (panel != null) Destroy(panel); PlayerPrefs.Save();
        }
    }
    public sealed class PalmSliderDrag : MonoBehaviour, IBeginDragHandler, IEndDragHandler
    {
        public PalmSettingsMenu menu;
        public void OnBeginDrag(PointerEventData eventData) { menu.Dragging = true; }
        public void OnEndDrag(PointerEventData eventData) { menu.Dragging = false; }
        void OnDisable() { if (menu != null) menu.Dragging = false; }
    }
}
