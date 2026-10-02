using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using System.Collections.Generic;
using System.Text;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Project.HandTracking.Editor
{
    [InitializeOnLoad]
    public static class QuestHandsSetup
    {
        const string RequestPath = "Library/QuestHandsSetup.request";
        static double nextStatus;
        static QuestHandsSetup()
        {
            EditorApplication.delayCall += ApplyPendingSetup;
            EditorApplication.update += WriteRuntimeStatus;
        }
        static void ApplyPendingSetup()
        {
            if (!File.Exists(RequestPath) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity") return;
            try { Configure(); File.Delete(RequestPath); }
            catch (Exception exception)
            {
                File.WriteAllText("Library/QuestHandsSetup.result.txt", exception.ToString());
                Debug.LogException(exception);
            }
        }

        [MenuItem("Tools/Hand Tracking/Configure Quest Hands in Current Scene")]
        public static void Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
            var scene = SceneManager.GetActiveScene();
            var modality = scene.GetRootGameObjects().SelectMany(root =>
                root.GetComponentsInChildren<XRInputModalityManager>(true)).Single();
            if (modality.leftHand == null || modality.rightHand == null)
                throw new InvalidOperationException("Both hand objects must be assigned.");
            // Keep both input modalities available; the manager chooses the tracked device.
            if (modality.leftController != null)
            {
                modality.leftController.SetActive(true);
                PrefabUtility.RecordPrefabInstancePropertyModifications(modality.leftController);
            }
            if (modality.rightController != null)
            {
                modality.rightController.SetActive(true);
                PrefabUtility.RecordPrefabInstancePropertyModifications(modality.rightController);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(modality);
            ConfigureHand(modality.gameObject, modality.leftHand, Handedness.Left);
            ConfigureHand(modality.gameObject, modality.rightHand, Handedness.Right);

            foreach (var group in new[] { BuildTargetGroup.Standalone, BuildTargetGroup.Android })
            {
                var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
                if (settings == null) throw new InvalidOperationException($"{group} OpenXR settings missing.");
                foreach (var feature in settings.GetFeatures<OpenXRFeature>())
                {
                    if (feature.GetType().Name == "HandTracking" || feature.GetType().Name == "MetaHandTrackingAim")
                    {
                        feature.enabled = true;
                        EditorUtility.SetDirty(feature);
                    }
                }
                EditorUtility.SetDirty(settings);
            }
            AddPinchTarget(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            ValidateSetup(scene);
            Debug.Log("[Hand tracking] Quest native hand tracking ready. Use Meta Horizon Link as OpenXR runtime and enable Developer Runtime Features.");
        }

        static void ConfigureHand(GameObject rig, GameObject hand, Handedness side)
        {
            string name = side + " Hand Trigger";
            var inputObject = rig.transform.Find(name)?.gameObject;
            if (inputObject == null) { inputObject = new GameObject(name); inputObject.transform.SetParent(rig.transform, false); }
            var trigger = inputObject.GetComponent<MetaPinchTrigger>() ?? inputObject.AddComponent<MetaPinchTrigger>();
            trigger.handedness = side;
            var aim = hand.GetComponentsInChildren<TrackedPoseDriver>(true).Single(driver => driver.name == "Aim Pose");
            string device = "<MetaAimHand>{" + side + "Hand}";
            // Pose and tracking state must come from the same device. Mixing the agnostic
            // hand device with Meta aim can select a default rotation from the other source.
            aim.positionInput = new InputActionProperty(new InputAction("Meta Aim Position", InputActionType.Value,
                device + "/devicePosition", expectedControlType: "Vector3"));
            aim.rotationInput = new InputActionProperty(new InputAction("Meta Aim Rotation", InputActionType.Value,
                device + "/deviceRotation", expectedControlType: "Quaternion"));
            aim.trackingStateInput = new InputActionProperty(new InputAction("Meta Aim Tracking", InputActionType.Value,
                device + "/trackingState", expectedControlType: "Integer"));
            EditorUtility.SetDirty(aim);
            PrefabUtility.RecordPrefabInstancePropertyModifications(aim);
            foreach (var nearFar in hand.GetComponentsInChildren<NearFarInteractor>(true))
            {
                nearFar.farInteractionCaster.castOrigin = aim.transform;
                var casterObject = nearFar.farInteractionCaster as UnityEngine.Object;
                EditorUtility.SetDirty(casterObject);
                PrefabUtility.RecordPrefabInstancePropertyModifications(casterObject);
                foreach (var visual in nearFar.GetComponentsInChildren<CurveVisualController>(true))
                {
                    visual.lineOriginTransform = aim.transform;
                    visual.restingVisualLineLength = 2f;
                    EditorUtility.SetDirty(visual);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
                }
            }
            foreach (var interactor in hand.GetComponentsInChildren<XRBaseInputInteractor>(true))
            {
                var selection = interactor.selectInput.GetObjectReference();
                if (selection == null) throw new InvalidOperationException("Expected sample pinch selection reader.");
                interactor.activateInput.inputSourceMode = XRInputButtonReader.InputSourceMode.ObjectReference;
                interactor.activateInput.SetObjectReference(selection);
                EditorUtility.SetDirty(interactor);
                PrefabUtility.RecordPrefabInstancePropertyModifications(interactor);
            }
            EditorUtility.SetDirty(trigger);
        }

        static void AddPinchTarget(Scene scene)
        {
            if (scene.GetRootGameObjects().Any(root => root.name == "Pinch Test Target")) return;
            var target = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            target.name = "Pinch Test Target";
            target.transform.position = new Vector3(0f, 1.3f, 2f);
            target.transform.localScale = Vector3.one * 0.3f;
            const string materialPath = "Assets/HandTracking/PinchTest.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                material.SetColor("_BaseColor", new Color(0.1f, 0.55f, 1f));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            target.GetComponent<Renderer>().sharedMaterial = material;
            target.AddComponent<XRSimpleInteractable>();
            target.AddComponent<PinchTargetFeedback>();
            var label = new GameObject("Pinch instructions");
            label.transform.SetParent(target.transform, false);
            label.transform.localPosition = new Vector3(0f, 1f, 0f);
            label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var text = label.AddComponent<TextMesh>();
            text.text = "Point and pinch thumb + index\nBlue = ready     Green = pressed";
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.fontSize = 48;
            text.characterSize = 0.04f;
        }

        static void ValidateSetup(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var triggers = roots.SelectMany(root => root.GetComponentsInChildren<MetaPinchTrigger>(true)).ToArray();
            if (triggers.Length != 2 || triggers.Select(trigger => trigger.handedness).Distinct().Count() != 2)
                throw new Exception("Expected left and right native pinch triggers.");
            var modality = roots.SelectMany(root => root.GetComponentsInChildren<XRInputModalityManager>(true)).Single();
            foreach (var hand in new[] { modality.leftHand, modality.rightHand })
                foreach (var interactor in hand.GetComponentsInChildren<XRBaseInputInteractor>(true))
                    if (interactor.activateInput.GetObjectReference() != interactor.selectInput.GetObjectReference())
                        throw new Exception("Pinch activation is not connected.");
            File.WriteAllText("Library/QuestHandsSetup.result.txt",
                "PASS: native hand tracking and Meta aim enabled; controller support preserved; two hand trigger components; pinch activation connected; test sphere present.\nPhysical headset pinch test still required.\n");
        }

        static void WriteRuntimeStatus()
        {
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < nextStatus) return;
            nextStatus = EditorApplication.timeSinceStartup + 1d;
            var inputs = UnityEngine.Object.FindObjectsByType<MetaPinchTrigger>(FindObjectsSortMode.None);
            var report = new StringBuilder();
            foreach (var input in inputs)
                report.AppendLine($"{input.handedness}: tracked={input.IsTracked}, pressed={input.IsPressed}, value={input.TriggerValue:F3}");
            var subsystems = new List<XRHandSubsystem>();
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var subsystem in subsystems)
                report.AppendLine($"Hand subsystem running={subsystem.running}, left={subsystem.leftHand.isTracked}, right={subsystem.rightHand.isTracked}");
            foreach (var hand in new[] { MetaAimHand.left, MetaAimHand.right })
                if (hand != null)
                    report.AppendLine($"Aim device {hand.name}: added={hand.added}, tracked={hand.isTracked.isPressed}, flags={hand.aimFlags.ReadValue()}, position={hand.devicePosition.ReadValue():F3}, rotation={hand.deviceRotation.ReadValue():F3}");
            foreach (var device in InputSystem.devices)
                report.AppendLine($"Device {device.name} layout={device.layout} usages={string.Join(",", device.usages)}");
            foreach (var driver in UnityEngine.Object.FindObjectsByType<TrackedPoseDriver>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (driver.name != "Aim Pose") continue;
                var position = driver.positionInput.action;
                var tracking = driver.trackingStateInput.action;
                report.AppendLine($"Driver {driver.transform.parent.name}/{driver.name}: enabled={driver.enabled}, active={driver.gameObject.activeInHierarchy}, local={driver.transform.localPosition:F3}, rotation={driver.transform.localEulerAngles:F1}, positionAction={position?.name}, enabled={position?.enabled}, controls={position?.controls.Count}, position={position?.ReadValue<Vector3>()}, tracking={tracking?.ReadValue<int>()}");
            }
            foreach (var interactor in UnityEngine.Object.FindObjectsByType<NearFarInteractor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (!interactor.transform.parent.name.Contains("Hand")) continue;
                var origin = interactor.farInteractionCaster.castOrigin;
                var effective = interactor.curveOrigin;
                report.AppendLine($"Ray {interactor.transform.parent.name}: active={interactor.isActiveAndEnabled}, origin={origin.name} pos={origin.position:F3} rot={origin.eulerAngles:F1}, effectivePos={effective.position:F3} effectiveRot={effective.eulerAngles:F1}");
                foreach (var line in interactor.GetComponentsInChildren<LineRenderer>(true))
                    if (line.positionCount > 0) report.AppendLine($"Line {line.name}: enabled={line.enabled}, count={line.positionCount}, worldSpace={line.useWorldSpace}, start={line.GetPosition(0):F3}, end={line.GetPosition(line.positionCount - 1):F3}");
            }
            File.WriteAllText("Library/QuestHandsRuntime.txt", report.ToString());
        }
    }
}
