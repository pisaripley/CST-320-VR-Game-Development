using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

namespace Project.HandTracking.Editor
{
    [InitializeOnLoad]
    public static class EchoPuzzleSetup
    {
        static EchoPuzzleSetup() { EditorApplication.update += Apply; }
        static void Apply()
        {
            if (!File.Exists("Library/EchoPuzzle.request") || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity") return;
            try
            {
                ValidateRules();
                var inputReport = new StringBuilder();
                var modality = UnityEngine.Object.FindFirstObjectByType<XRInputModalityManager>();
                if (modality != null)
                {
                    var offset = modality.transform.Find("Camera Offset");
                    modality.leftController = offset.Find("Left Controller").gameObject;
                    modality.rightController = offset.Find("Right Controller").gameObject;
                    modality.leftController.SetActive(true);
                    modality.rightController.SetActive(true);
                    EnsureControllerRay(modality.leftController, "Left");
                    EnsureControllerRay(modality.rightController, "Right");
                    PrefabUtility.RecordPrefabInstancePropertyModifications(modality.leftController);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(modality.rightController);
                    EditorUtility.SetDirty(modality);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(modality);
                    foreach (var controller in new[] { modality.leftController, modality.rightController })
                        foreach (var ray in controller.GetComponentsInChildren<XRBaseInputInteractor>(true))
                        {
                            inputReport.AppendLine(controller.name + "/" + ray.name + ": select=" + ray.selectInput.inputSourceMode + ", activate=" + ray.activateInput.inputSourceMode);
                            if (ray.activateInput.inputActionReferencePerformed == null) continue;
                            ray.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.InputActionReference;
                            ray.selectInput.inputActionReferencePerformed = ray.activateInput.inputActionReferencePerformed;
                            ray.selectInput.inputActionReferenceValue = ray.activateInput.inputActionReferenceValue;
                            EditorUtility.SetDirty(ray);
                            PrefabUtility.RecordPrefabInstancePropertyModifications(ray);
                        }
                }
                var puzzle = UnityEngine.Object.FindFirstObjectByType<EchoOrbsPuzzle>();
                if (puzzle == null)
                {
                    var root = new GameObject("Echo Orbs Puzzle");
                    root.AddComponent<EchoOrbsPuzzle>();
                    var head = Camera.main;
                    if (head != null)
                    {
                        var forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
                        root.transform.SetPositionAndRotation(head.transform.position + forward * 1.45f, Quaternion.LookRotation(forward));
                    }
                }
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                File.WriteAllText("Library/EchoPuzzle.result.txt", "PASS: puzzle saved; five-round sequence, mistakes, replay, and completion rules verified.\n" + inputReport);
                File.Delete("Library/EchoPuzzle.request");
                Debug.Log("Echo Orbs puzzle installed. Use controller trigger or hand pinch to select START.");
            }
            catch (Exception error)
            {
                File.Delete("Library/EchoPuzzle.request");
                File.WriteAllText("Library/EchoPuzzle.result.txt", error.ToString());
                Debug.LogException(error);
            }
        }
        static void ValidateRules()
        {
            var pattern = new EchoPattern(42);
            for (int round = 1; round <= 5; round++)
            {
                pattern.NextRound();
                if (pattern.sequence.Count != round + 2) throw new Exception("Wrong sequence length.");
                if (pattern.Choose((pattern.sequence[0] + 1) % 4) != -1 || pattern.Progress != 0) throw new Exception("Mistake failed to reset.");
                pattern.Choose(pattern.sequence[0]);
                pattern.Replay();
                if (pattern.Progress != 0) throw new Exception("Replay failed to reset.");
                for (int i = 0; i < pattern.sequence.Count; i++)
                    if (pattern.Choose(pattern.sequence[i]) != (i == pattern.sequence.Count - 1 ? 1 : 0)) throw new Exception("Completion rule failed.");
            }
        }
        static void EnsureControllerRay(GameObject controller, string side)
        {
            string path = "<XRController>{" + side + "Hand}";
            var pose = controller.GetComponent<TrackedPoseDriver>() ?? controller.AddComponent<TrackedPoseDriver>();
            pose.positionInput = new InputActionProperty(new InputAction("Controller Position", InputActionType.Value, path + "/devicePosition", expectedControlType: "Vector3"));
            pose.rotationInput = new InputActionProperty(new InputAction("Controller Rotation", InputActionType.Value, path + "/deviceRotation", expectedControlType: "Quaternion"));
            pose.trackingStateInput = new InputActionProperty(new InputAction("Controller Tracking", InputActionType.Value, path + "/trackingState", expectedControlType: "Integer"));
            EditorUtility.SetDirty(pose);
            PrefabUtility.RecordPrefabInstancePropertyModifications(pose);
            var ray = controller.GetComponentInChildren<NearFarInteractor>(true);
            if (ray == null)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/Interactors/" + side + "_NearFarInteractor.prefab");
                if (prefab == null) throw new Exception("Controller ray prefab missing.");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, controller.transform);
                ray = instance.GetComponent<NearFarInteractor>();
            }
            ray.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.InputAction;
            ray.selectInput.inputActionPerformed = new InputAction("Puzzle Select", InputActionType.Button, path + "/triggerPressed");
            ray.selectInput.inputActionValue = new InputAction("Puzzle Select Value", InputActionType.Value, path + "/trigger");
            ray.farInteractionCaster.castOrigin = controller.transform;
            var caster = ray.farInteractionCaster as UnityEngine.Object;
            EditorUtility.SetDirty(caster);
            PrefabUtility.RecordPrefabInstancePropertyModifications(caster);
            foreach (var visual in ray.GetComponentsInChildren<CurveVisualController>(true))
            {
                visual.lineOriginTransform = controller.transform;
                visual.restingVisualLineLength = 2f;
                EditorUtility.SetDirty(visual);
                PrefabUtility.RecordPrefabInstancePropertyModifications(visual);
            }
            EditorUtility.SetDirty(ray);
            PrefabUtility.RecordPrefabInstancePropertyModifications(ray);
        }
    }
}
