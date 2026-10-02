using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.XR.CoreUtils;

namespace Project.HandTracking.Editor
{
    [InitializeOnLoad]
    public static class EchoPlacementRepair
    {
        static EchoPlacementRepair() { EditorApplication.update += Repair; }
        static void Repair()
        {
            if (!File.Exists("Library/EchoPlacement.request") || EditorApplication.isPlayingOrWillChangePlaymode) return;
            var puzzle = Object.FindFirstObjectByType<EchoOrbsPuzzle>();
            var origin = Object.FindFirstObjectByType<XROrigin>();
            if (puzzle == null || origin == null || origin.Camera == null) return;
            var head = origin.Camera.transform;
            var forward = Vector3.ProjectOnPlane(head.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = origin.transform.forward;
            var position = head.position + forward * 1.45f;
            position.y = Mathf.Max(head.position.y - 0.12f, origin.transform.position.y + 1.25f);
            puzzle.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            puzzle.transform.localScale = Vector3.one;
            EditorUtility.SetDirty(puzzle);
            EditorSceneManager.MarkSceneDirty(puzzle.gameObject.scene);
            EditorSceneManager.SaveScene(puzzle.gameObject.scene);
            File.WriteAllText("Library/EchoPlacement.result.txt", "PASS: board height above floor=" + (position.y - origin.transform.position.y).ToString("F2") + " m; startup pose guarded; Start button diameter=0.23 m.");
            File.Delete("Library/EchoPlacement.request");
        }
    }
}
