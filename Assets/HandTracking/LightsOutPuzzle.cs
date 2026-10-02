using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Project.HandTracking
{
    public sealed class LightsOutPuzzle : MonoBehaviour
    {
        readonly bool[] lights = new bool[9];
        readonly EchoOrb[] tiles = new EchoOrb[9];
        TextMesh status;
        Material material;
        bool solved;
        float nextPress;
        public static void Toggle(bool[] board, int index)
        {
            int row = index / 3, column = index % 3;
            board[index] = !board[index];
            if (row > 0) board[index - 3] = !board[index - 3];
            if (row < 2) board[index + 3] = !board[index + 3];
            if (column > 0) board[index - 1] = !board[index - 1];
            if (column < 2) board[index + 1] = !board[index + 1];
        }
        void Awake()
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            material.SetColor("_BaseColor", Color.white);
            Label("LIGHTS OUT", new Vector3(0, 0.65f, 0), 0.016f);
            status = Label("Turn every tile blue.", new Vector3(0, 0.5f, 0), 0.008f);
            Label("Select a tile to flip it + its neighbours.", new Vector3(0, -0.56f, 0), 0.007f);
            for (int i = 0; i < 9; i++)
            {
                int index = i;
                var obj = GameObject.CreatePrimitive(PrimitiveType.Cube); obj.name = "Tile " + (i + 1);
                obj.transform.SetParent(transform, false);
                obj.transform.localPosition = new Vector3((i % 3 - 1) * 0.29f, (1 - i / 3) * 0.29f, 0);
                obj.transform.localScale = new Vector3(0.25f, 0.25f, 0.08f);
                obj.GetComponent<Renderer>().sharedMaterial = material;
                obj.AddComponent<XRSimpleInteractable>(); tiles[i] = obj.AddComponent<EchoOrb>();
                tiles[i].clicked = () => Press(index);
            }
            var reset = GameObject.CreatePrimitive(PrimitiveType.Sphere); reset.name = "Reset Lights Out";
            reset.transform.SetParent(transform, false); reset.transform.localPosition = new Vector3(0.62f, 0, 0);
            reset.transform.localScale = Vector3.one * 0.18f; reset.GetComponent<Renderer>().sharedMaterial = material;
            reset.AddComponent<XRSimpleInteractable>(); var button = reset.AddComponent<EchoOrb>();
            button.color = new Color(1, 0.8f, 0.2f); button.clicked = ResetBoard;
            Label("RESET", new Vector3(0.62f, -0.17f, -0.06f), 0.007f);
            ResetBoard();
        }
        void ResetBoard()
        {
            for (int i = 0; i < 9; i++) lights[i] = true;
            foreach (int move in new[] { 0, 4, 8, 2 }) Toggle(lights, move);
            solved = false; status.text = "Turn every tile blue."; Refresh();
        }
        void Press(int index)
        {
            if (AssignmentHub.Active != null && !AssignmentHub.Active.GameplayEnabled) return;
            if (solved || Time.unscaledTime < nextPress) return;
            nextPress = Time.unscaledTime + 0.2f; Toggle(lights, index); tiles[index].Flash(0.15f);
            solved = true; foreach (bool light in lights) solved &= light;
            status.text = solved ? "Solved! Well done." : "Turn every tile blue.";
            Refresh();
        }
        void Refresh()
        {
            for (int i = 0; i < 9; i++) tiles[i].color = lights[i] ? new Color(0.15f, 0.8f, 1f) : new Color(1f, 0.35f, 0.15f);
        }
        TextMesh Label(string text, Vector3 position, float size)
        {
            var obj = new GameObject(text); obj.transform.SetParent(transform, false); obj.transform.localPosition = position;
            var label = obj.AddComponent<TextMesh>(); label.text = text; label.fontSize = 64;
            label.characterSize = size; label.anchor = TextAnchor.MiddleCenter; return label;
        }
        void OnDestroy() { Destroy(material); }
    }
}
