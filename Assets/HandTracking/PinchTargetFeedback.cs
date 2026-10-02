using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Project.HandTracking
{
    [RequireComponent(typeof(XRSimpleInteractable), typeof(Renderer))]
    public sealed class PinchTargetFeedback : MonoBehaviour
    {
        XRSimpleInteractable target;
        Renderer targetRenderer;
        MaterialPropertyBlock properties;
        void Awake()
        {
            target = GetComponent<XRSimpleInteractable>();
            targetRenderer = GetComponent<Renderer>();
            properties = new MaterialPropertyBlock();
        }
        void OnEnable()
        {
            target.selectEntered.AddListener(OnSelected);
            target.selectExited.AddListener(OnReleased);
            SetColor(false);
        }
        void OnDisable()
        {
            target.selectEntered.RemoveListener(OnSelected);
            target.selectExited.RemoveListener(OnReleased);
        }
        void OnSelected(SelectEnterEventArgs args) { SetColor(true); }
        void OnReleased(SelectExitEventArgs args) { SetColor(target.isSelected); }
        void SetColor(bool selected)
        {
            targetRenderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", selected ? Color.green : new Color(0.1f, 0.55f, 1f));
            targetRenderer.SetPropertyBlock(properties);
        }
    }
}
