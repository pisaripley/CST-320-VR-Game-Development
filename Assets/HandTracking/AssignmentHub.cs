using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Unity.XR.CoreUtils;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace Project.HandTracking
{
    public sealed class AssignmentHub : MonoBehaviour
    {
        public static AssignmentHub Active { get; private set; }
        public bool GameplayEnabled { get; private set; }
        public XROrigin rig;
        public ContinuousMoveProvider movement;
        public SnapTurnProvider turning;
        public TeleportationProvider teleportation;
        public Collider floor;
        public PalmSettingsMenu settings;
        GameObject welcome;
        readonly List<Material> materials = new List<Material>();
        void Awake()
        {
            Active = this;
            var oldFloor = GameObject.Find("Plane");
            if (oldFloor != null) oldFloor.SetActive(false);
            SetMovement(false);
            EnsureEventSystem();
            BuildRoom();
            settings = gameObject.AddComponent<PalmSettingsMenu>();
            settings.hub = this;
            gameObject.AddComponent<LeftTeleportAim>().hub = this;
            BuildWelcome();
        }
        IEnumerator Start()
        {
            float stable = 0;
            while (!GameplayEnabled && stable < 0.5f)
            {
                bool tracked = InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.isTracked, out bool value) && value;
                bool usable = tracked && rig.Camera.transform.position.y > rig.transform.position.y + 0.55f;
                stable = usable ? stable + Time.unscaledDeltaTime : 0;
                yield return null;
            }
            if (GameplayEnabled) yield break;
            Vector3 forward = Vector3.ProjectOnPlane(rig.Camera.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = rig.transform.forward;
            Vector3 location = rig.Camera.transform.position;
            location.y = rig.transform.position.y;
            transform.SetPositionAndRotation(location, Quaternion.LookRotation(forward));
            welcome.transform.position = rig.Camera.transform.position + forward * 1.1f - Vector3.up * 0.05f;
        }
        void Update() { SetMovement(GameplayEnabled && !settings.IsOpen); }
        void SetMovement(bool enabled)
        {
            if (movement != null && movement.enabled != enabled) movement.enabled = enabled;
            if (turning != null && turning.enabled != enabled) turning.enabled = enabled;
        }
        void EnsureEventSystem()
        {
            var events = FindFirstObjectByType<EventSystem>();
            if (events == null) events = new GameObject("XR UI Event System").AddComponent<EventSystem>();
            foreach (var module in events.GetComponents<BaseInputModule>())
                if (!(module is XRUIInputModule)) module.enabled = false;
            if (events.GetComponent<XRUIInputModule>() == null) events.gameObject.AddComponent<XRUIInputModule>();
        }
        void BuildRoom()
        {
            var platform = Box("Assignment Floor", new Vector3(0, -0.04f, 4f), new Vector3(12, 0.08f, 12), new Color(0.13f, 0.18f, 0.23f));
            floor = platform.GetComponent<Collider>();
            for (int i = 0; i < 10; i++)
            {
                var marker = Box("Path marker", new Vector3(0.35f * i, 0.003f, 1.1f + i * 0.48f), new Vector3(0.12f, 0.008f, 0.24f), new Color(0.2f, 0.75f, 0.85f));
                Destroy(marker.GetComponent<Collider>());
            }
            foreach (float x in new[] { -6f, 6f }) Box("Room boundary", new Vector3(x, 0.5f, 4), new Vector3(0.08f, 1, 12), new Color(0.07f, 0.12f, 0.17f));
            foreach (float z in new[] { -2f, 10f }) Box("Room boundary", new Vector3(0, 0.5f, z), new Vector3(12, 1, 0.08f), new Color(0.07f, 0.12f, 0.17f));
            var second = new GameObject("Puzzle 2 - Lights Out");
            second.transform.SetParent(transform, false);
            second.transform.localPosition = new Vector3(3.4f, 1.35f, 7f);
            second.AddComponent<LightsOutPuzzle>();
            var sign = new GameObject("Station 2 sign"); sign.transform.SetParent(transform, false);
            sign.transform.localPosition = new Vector3(3.4f, 0.65f, 6.1f);
            var label = sign.AddComponent<TextMesh>();
            label.text = "2 • LIGHTS OUT";
            label.fontSize = 48; label.characterSize = 0.007f; label.anchor = TextAnchor.MiddleCenter;
        }
        GameObject Box(string name, Vector3 position, Vector3 scale, Color color)
        {
            var obj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obj.name = name; obj.transform.SetParent(transform, false);
            obj.transform.localPosition = position; obj.transform.localScale = scale;
            var material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", color); materials.Add(material);
            obj.GetComponent<Renderer>().sharedMaterial = material;
            return obj;
        }
        void BuildWelcome()
        {
            var canvas = PuzzleUI.Canvas("Welcome and controls", transform, rig.Camera, new Vector2(780, 620), 0.0013f);
            welcome = canvas.gameObject;
            welcome.transform.localPosition = new Vector3(0, 1.4f, 1.1f);
            PuzzleUI.Text(canvas.transform, "WELCOME", new Vector2(0, 245), new Vector2(710, 70), 42);
            PuzzleUI.Text(canvas.transform,
                "Two puzzles. Explore at your own pace.\n\n" +
                "CONTROLLERS\nLeft stick: move • Right stick: turn\nAim left controller + click left stick: teleport\nLeft X button: settings • Trigger: select / drag\n\n" +
                "HANDS\nLeft hand: aim at floor + pinch to teleport\nLeft palm flat, facing up: settings menu\nRight hand: point + pinch to select or drag\n\n" +
                "1. Echo Orbs: repeat the flashing pattern.\n2. Follow the blue path to Lights Out.\nTurn all nine tiles blue to solve it.",
                new Vector2(0, 5), new Vector2(710, 440), 25);
            PuzzleUI.Button(canvas.transform, "BEGIN", new Vector2(-155, -250), new Vector2(260, 70), () =>
            {
                GameplayEnabled = true;
                welcome.SetActive(false);
            });
            PuzzleUI.Button(canvas.transform, "SOUND SETTINGS", new Vector2(155, -250), new Vector2(280, 70), () => settings.OpenAtHead());
        }
        public void ReturnToStart()
        {
            if (!GameplayEnabled) return;
            teleportation.QueueTeleportRequest(new TeleportRequest { destinationPosition = transform.position, destinationRotation = transform.rotation, matchOrientation = MatchOrientation.TargetUpAndForward });
        }
        void OnDestroy()
        {
            if (Active == this) Active = null;
            foreach (var material in materials) Destroy(material);
        }
    }

    public static class PuzzleUI
    {
        public static Canvas Canvas(string name, Transform parent, Camera camera, Vector2 size, float scale)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false); obj.transform.localScale = Vector3.one * scale;
            var canvas = obj.AddComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
            ((RectTransform)obj.transform).sizeDelta = size;
            obj.AddComponent<TrackedDeviceGraphicRaycaster>();
            var background = obj.AddComponent<Image>(); background.color = new Color(0.025f, 0.045f, 0.075f, 1f);
            return canvas;
        }
        public static RectTransform Rect(string name, Transform parent, Vector2 position, Vector2 size)
        {
            var obj = new GameObject(name, typeof(RectTransform)); obj.transform.SetParent(parent, false);
            var rect = (RectTransform)obj.transform; rect.sizeDelta = size; rect.anchoredPosition = position;
            return rect;
        }
        public static Text Text(Transform parent, string value, Vector2 position, Vector2 size, int fontSize)
        {
            var rect = Rect(value, parent, position, size); var text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.text = value;
            text.fontSize = fontSize; text.alignment = TextAnchor.MiddleCenter; text.color = Color.white; text.raycastTarget = false;
            return text;
        }
        public static Button Button(Transform parent, string title, Vector2 position, Vector2 size, UnityEngine.Events.UnityAction action)
        {
            var rect = Rect(title, parent, position, size);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0.12f, 0.45f, 0.6f);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.onClick.AddListener(action);
            Text(rect, title, Vector2.zero, size, 25);
            return button;
        }
    }
}
