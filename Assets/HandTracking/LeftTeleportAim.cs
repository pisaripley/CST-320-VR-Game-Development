using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace Project.HandTracking
{
    public sealed class LeftTeleportAim : MonoBehaviour
    {
        public AssignmentHub hub;
        InputAction position, rotation, tracking, teleport;
        LineRenderer line;
        GameObject marker;
        Material material;
        bool wasPinched;
        float cooldown;
        void Start()
        {
            position = new InputAction("Left teleport position", InputActionType.Value, "<XRController>{LeftHand}/devicePosition");
            rotation = new InputAction("Left teleport rotation", InputActionType.Value, "<XRController>{LeftHand}/deviceRotation");
            tracking = new InputAction("Left teleport tracking", InputActionType.Value, "<XRController>{LeftHand}/trackingState");
            teleport = new InputAction("Left teleport", InputActionType.Button, "<XRController>{LeftHand}/primary2DAxisClick");
            position.Enable(); rotation.Enable(); tracking.Enable(); teleport.Enable();
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); material.SetColor("_BaseColor", new Color(0.2f, 1f, 0.65f));
            var obj = new GameObject("Left teleport preview"); obj.transform.SetParent(transform);
            line = obj.AddComponent<LineRenderer>(); line.sharedMaterial = material; line.useWorldSpace = true; line.widthMultiplier = 0.008f;
            marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder); marker.name = "Teleport landing marker";
            marker.transform.localScale = new Vector3(0.5f, 0.01f, 0.5f); Destroy(marker.GetComponent<Collider>());
            marker.GetComponent<Renderer>().sharedMaterial = material; marker.SetActive(false);
        }
        void Update()
        {
            if (line == null) return;
            line.enabled = false; marker.SetActive(false);
            var hand = MetaAimHand.left;
            bool handTracked = hand != null && hand.added && hand.isTracked.isPressed && ((MetaAimFlags)hand.aimFlags.ReadValue() & (MetaAimFlags.Valid | MetaAimFlags.SystemGesture)) == MetaAimFlags.Valid;
            bool pinch = handTracked && hand.indexPressed.isPressed;
            bool pressed = handTracked ? pinch && !wasPinched : teleport.WasPressedThisFrame();
            wasPinched = pinch;
            if (!hub.GameplayEnabled || hub.settings.IsOpen || Time.unscaledTime < cooldown) return;
            if (!handTracked && (tracking.ReadValue<int>() & 3) != 3) return;
            Vector3 local = handTracked ? hand.devicePosition.ReadValue() : position.ReadValue<Vector3>();
            Quaternion localRotation = handTracked ? hand.deviceRotation.ReadValue() : rotation.ReadValue<Quaternion>();
            var space = hub.rig.Camera.transform.parent;
            Vector3 start = space.TransformPoint(local);
            Vector3 velocity = space.TransformDirection(localRotation * Vector3.forward) * 5f + Vector3.up * 1.5f;
            var points = new Vector3[31]; points[0] = start; int count = 1; bool valid = false; Vector3 landing = default;
            for (int i = 1; i < points.Length; i++)
            {
                float t = i * 0.06f;
                Vector3 next = start + velocity * t + Vector3.down * (4.9f * t * t);
                Vector3 segment = next - points[count - 1];
                if (Physics.Raycast(points[count - 1], segment.normalized, out var hit, segment.magnitude, ~0, QueryTriggerInteraction.Ignore))
                {
                    points[count++] = hit.point;
                    valid = hit.collider == hub.floor && Vector3.Dot(hit.normal, Vector3.up) > 0.9f;
                    landing = hit.point; break;
                }
                points[count++] = next;
            }
            if (!valid) return;
            line.enabled = true; line.positionCount = count;
            for (int i = 0; i < count; i++) line.SetPosition(i, points[i]);
            marker.SetActive(true); marker.transform.position = landing + Vector3.up * 0.012f;
            if (!pressed) return;
            hub.teleportation.QueueTeleportRequest(new TeleportRequest { destinationPosition = landing, matchOrientation = MatchOrientation.WorldSpaceUp });
            cooldown = Time.unscaledTime + 0.6f;
        }
        void OnDestroy()
        {
            position?.Dispose(); rotation?.Dispose(); tracking?.Dispose(); teleport?.Dispose();
            if (marker != null) Destroy(marker); if (material != null) Destroy(material);
        }
    }
}
