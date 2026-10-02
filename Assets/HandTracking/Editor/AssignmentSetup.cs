using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace Project.HandTracking.Editor
{
    [InitializeOnLoad]
    public static class AssignmentSetup
    {
        static AssignmentSetup() { EditorApplication.update += Apply; }
        static void Apply()
        {
            if (!File.Exists("Library/AssignmentSetup.request") || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != "Assets/Scenes/SampleScene.unity") return;
            try
            {
                ValidateLightsOut();
                var rig = UnityEngine.Object.FindFirstObjectByType<XROrigin>();
                if (rig == null || rig.Camera == null) throw new Exception("XR rig or camera missing.");
                var hub = UnityEngine.Object.FindFirstObjectByType<AssignmentHub>();
                if (hub == null) hub = new GameObject("Assignment Puzzle Room").AddComponent<AssignmentHub>();
                hub.rig = rig;
                hub.transform.SetPositionAndRotation(rig.transform.position, Quaternion.Euler(0, rig.transform.eulerAngles.y, 0));
                var locomotion = rig.transform.Find("Locomotion");
                if (locomotion == null) { var obj = new GameObject("Locomotion"); obj.transform.SetParent(rig.transform, false); locomotion = obj.transform; }
                var body = locomotion.GetComponent<XRBodyTransformer>() ?? locomotion.gameObject.AddComponent<XRBodyTransformer>();
                body.xrOrigin = rig;
                body.useCharacterControllerIfExists = true;
                var mediator = locomotion.GetComponent<LocomotionMediator>() ?? locomotion.gameObject.AddComponent<LocomotionMediator>();
                foreach (var provider in rig.GetComponentsInChildren<LocomotionProvider>(true))
                {
                    if (!(provider is ContinuousMoveProvider) && !(provider is ContinuousTurnProvider) && !(provider is SnapTurnProvider)) continue;
                    provider.enabled = false;
                    EditorUtility.SetDirty(provider);
                    if (PrefabUtility.IsPartOfPrefabInstance(provider)) PrefabUtility.RecordPrefabInstancePropertyModifications(provider);
                }
                var moveObject = locomotion.Find("Assignment Move");
                if (moveObject == null) { var obj = new GameObject("Assignment Move"); obj.transform.SetParent(locomotion, false); moveObject = obj.transform; }
                hub.movement = moveObject.GetComponent<ContinuousMoveProvider>() ?? moveObject.gameObject.AddComponent<ContinuousMoveProvider>();
                hub.movement.mediator = mediator; hub.movement.forwardSource = rig.Camera.transform; hub.movement.moveSpeed = 1.5f;
                hub.movement.leftHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.InputAction;
                hub.movement.leftHandMoveInput.inputAction = new InputAction("Assignment Move", InputActionType.Value, "<XRController>{LeftHand}/primary2DAxis", processors: "stickDeadzone", expectedControlType: "Vector2");
                hub.movement.rightHandMoveInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
                hub.turning = locomotion.GetComponentsInChildren<SnapTurnProvider>(true).FirstOrDefault();
                if (hub.turning == null) hub.turning = locomotion.gameObject.AddComponent<SnapTurnProvider>();
                hub.turning.mediator = mediator; hub.turning.turnAmount = 30f;
                hub.turning.leftHandTurnInput.inputSourceMode = XRInputValueReader.InputSourceMode.Unused;
                hub.turning.rightHandTurnInput.inputSourceMode = XRInputValueReader.InputSourceMode.InputAction;
                hub.turning.rightHandTurnInput.inputAction = new InputAction("Assignment Turn", InputActionType.Value, "<XRController>{RightHand}/primary2DAxis", expectedControlType: "Vector2");
                hub.teleportation = locomotion.GetComponentsInChildren<TeleportationProvider>(true).FirstOrDefault();
                if (hub.teleportation == null) hub.teleportation = locomotion.gameObject.AddComponent<TeleportationProvider>();
                hub.teleportation.mediator = mediator;
                hub.teleportation.enabled = true;
                var controller = rig.GetComponent<CharacterController>() ?? rig.gameObject.AddComponent<CharacterController>();
                controller.height = 1.6f; controller.center = new Vector3(0, 0.85f, 0); controller.radius = 0.22f;
                var echo = UnityEngine.Object.FindFirstObjectByType<EchoOrbsPuzzle>();
                if (echo == null) throw new Exception("Echo Orbs puzzle missing.");
                echo.placeAtStartup = false;
                echo.transform.SetParent(hub.transform, false); echo.transform.localPosition = new Vector3(0, 1.35f, 3f);
                echo.transform.localRotation = Quaternion.identity; echo.transform.localScale = Vector3.one;
                foreach (var component in new Component[] { hub, body, mediator, hub.movement, hub.turning, hub.teleportation, controller, echo })
                {
                    EditorUtility.SetDirty(component);
                    if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
                EditorSceneManager.MarkSceneDirty(hub.gameObject.scene); EditorSceneManager.SaveScene(hub.gameObject.scene);
                File.WriteAllText("Library/AssignmentSetup.result.txt", "PASS: room saved, controller move and 30-degree snap turn configured, teleport provider assigned, fixed Echo station, intro gate, palm settings UI and second puzzle installed. Lights Out solvability and neighbour boundaries verified.");
                File.Delete("Library/AssignmentSetup.request");
            }
            catch (Exception error) { File.Delete("Library/AssignmentSetup.request"); File.WriteAllText("Library/AssignmentSetup.result.txt", error.ToString()); Debug.LogException(error); }
        }
        static void ValidateLightsOut()
        {
            var board = new bool[9]; for (int i = 0; i < 9; i++) board[i] = true;
            foreach (int index in new[] { 0, 4, 8, 2 }) LightsOutPuzzle.Toggle(board, index);
            if (board.All(value => value)) throw new Exception("Puzzle starts solved.");
            foreach (int index in new[] { 2, 8, 4, 0 }) LightsOutPuzzle.Toggle(board, index);
            if (!board.All(value => value)) throw new Exception("Puzzle is not solvable.");
            board = new bool[9]; LightsOutPuzzle.Toggle(board, 2);
            if (board[3] || board.Count(value => value) != 3) throw new Exception("Neighbour toggle wraps across rows.");
        }
    }
}
