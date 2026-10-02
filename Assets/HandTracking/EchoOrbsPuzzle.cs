using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR;
using Unity.XR.CoreUtils;

namespace Project.HandTracking
{
    public sealed class EchoPattern
    {
        readonly System.Random random;
        public readonly List<int> sequence = new List<int>();
        public int Round { get; private set; }
        public int Progress { get; private set; }
        public EchoPattern(int seed) { random = new System.Random(seed); }
        public void NextRound()
        {
            Round++;
            Progress = 0;
            while (sequence.Count < Round + 2)
            {
                int next = random.Next(4);
                if (sequence.Count == 0 || next != sequence[sequence.Count - 1]) sequence.Add(next);
            }
        }
        public void Replay() { Progress = 0; }
        // -1 = wrong orb, 0 = more to go, 1 = round complete.
        public int Choose(int orb)
        {
            if (Progress >= sequence.Count) return 0;
            if (orb != sequence[Progress]) { Progress = 0; return -1; }
            return ++Progress == sequence.Count ? 1 : 0;
        }
    }

    public sealed class EchoOrbsPuzzle : MonoBehaviour
    {
        public bool placeAtStartup = true;
        readonly Color[] colors = { new Color(0.1f, 0.8f, 1f), new Color(1f, 0.4f, 0.25f), new Color(0.65f, 0.4f, 1f), new Color(0.3f, 1f, 0.5f) };
        readonly List<EchoOrb> pads = new List<EchoOrb>();
        readonly List<Material> materials = new List<Material>();
        readonly List<AudioClip> tones = new List<AudioClip>();
        TextMesh status;
        EchoOrb button;
        AudioSource audioSource;
        EchoPattern pattern;
        Coroutine routine;
        bool accepting;
        bool busy;
        bool won;
        float nextInput;
        public void SetToneVolume(float value) { if (audioSource != null) audioSource.volume = 0.3f * value; }

        void Awake()
        {
            var oldTarget = GameObject.Find("Pinch Test Target");
            if (oldTarget != null) oldTarget.SetActive(false);
            BuildBoard();
            if (placeAtStartup) PlaceInFrontOfHead();
        }
        IEnumerator Start()
        {
            if (!placeAtStartup) yield break;
            // A tracking flag can arrive before the camera pose. Never place at the
            // temporary floor-level startup pose; wait for a stable usable height.
            float stableTime = 0f;
            while (stableTime < 0.5f)
            {
                bool tracked = InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.isTracked, out bool headTracked) && headTracked;
                var origin = FindFirstObjectByType<XROrigin>();
                var head = origin != null ? origin.Camera : Camera.main;
                float floor = origin != null ? origin.transform.position.y : 0f;
                bool usable = tracked && head != null && head.transform.position.y > floor + 0.55f;
                stableTime = usable ? stableTime + Time.unscaledDeltaTime : 0f;
                yield return null;
            }
            if (pattern == null) PlaceInFrontOfHead();
        }
        void PlaceInFrontOfHead()
        {
            var origin = FindFirstObjectByType<XROrigin>();
            var head = origin != null ? origin.Camera : Camera.main;
            if (head == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(head.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
            float floor = origin != null ? origin.transform.position.y : 0f;
            Vector3 position = head.transform.position + forward * 1.45f;
            position.y = Mathf.Max(head.transform.position.y - 0.12f, floor + 1.25f);
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward));
            transform.localScale = Vector3.one;
        }
        void BuildBoard()
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.spatialBlend = 0.5f;
            audioSource.volume = 0.3f;
            var panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "Puzzle backdrop";
            panel.transform.SetParent(transform, false);
            panel.transform.localPosition = new Vector3(0, 0, 0.13f);
            panel.transform.localScale = new Vector3(1.55f, 1.15f, 0.05f);
            Destroy(panel.GetComponent<Collider>());
            panel.GetComponent<Renderer>().sharedMaterial = MakeMaterial(new Color(0.025f, 0.035f, 0.075f));
            Label("ECHO ORBS", new Vector3(0, 0.44f, -0.03f), 0.065f, Color.white);
            status = Label("Select START to play", new Vector3(0, 0.3f, -0.03f), 0.035f, Color.white);
            Label("Watch the lights. Select the orbs in order.", new Vector3(0, -0.48f, -0.03f), 0.027f, new Color(0.7f, 0.8f, 0.95f));
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                var pad = MakeOrb("Orb " + (i + 1), new Vector3((i - 1.5f) * 0.34f, 0.04f, -0.06f), 0.23f, colors[i]);
                pad.clicked = () => PressOrb(index);
                pads.Add(pad);
                Label((i + 1).ToString(), new Vector3((i - 1.5f) * 0.34f, 0.04f, -0.19f), 0.055f, Color.white);
                tones.Add(MakeTone(330f * Mathf.Pow(2, i / 4f), 0.22f));
            }
            button = MakeOrb("Start or replay", new Vector3(0, -0.29f, -0.06f), 0.23f, new Color(1f, 0.85f, 0.25f));
            button.clicked = PressButton;
            Label("START / REPLAY", new Vector3(0, -0.29f, -0.2f), 0.029f, Color.white);
        }
        Material MakeMaterial(Color color)
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", color);
            materials.Add(mat);
            return mat;
        }
        EchoOrb MakeOrb(string name, Vector3 position, float size, Color color)
        {
            var orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = name;
            orb.transform.SetParent(transform, false);
            orb.transform.localPosition = position;
            orb.transform.localScale = Vector3.one * size;
            orb.GetComponent<Renderer>().sharedMaterial = MakeMaterial(color * 0.45f);
            orb.AddComponent<XRSimpleInteractable>();
            var pad = orb.AddComponent<EchoOrb>();
            pad.color = color;
            return pad;
        }
        TextMesh Label(string text, Vector3 position, float size, Color color)
        {
            var obj = new GameObject(text);
            obj.transform.SetParent(transform, false);
            obj.transform.localPosition = position;
            var label = obj.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 64;
            label.characterSize = size * 0.25f;
            label.color = color;
            return label;
        }
        void PressButton()
        {
            if (AssignmentHub.Active != null && !AssignmentHub.Active.GameplayEnabled) return;
            if (busy || Time.unscaledTime < nextInput) return;
            if (pattern == null || won)
            {
                pattern = new EchoPattern(System.Environment.TickCount);
                pattern.NextRound();
                won = false;
            }
            pattern.Replay();
            routine = StartCoroutine(ShowPattern());
        }
        IEnumerator ShowPattern()
        {
            busy = true;
            accepting = false;
            status.text = "Round " + pattern.Round + " / 5  •  Watch!";
            yield return new WaitForSeconds(0.65f);
            foreach (int index in pattern.sequence)
            {
                pads[index].Flash(0.5f);
                audioSource.PlayOneShot(tones[index]);
                yield return new WaitForSeconds(0.75f);
            }
            status.text = "Your turn!  0 / " + pattern.sequence.Count;
            nextInput = Time.unscaledTime + 0.15f;
            accepting = true;
            busy = false;
        }
        void PressOrb(int index)
        {
            if (!accepting || Time.unscaledTime < nextInput) return;
            nextInput = Time.unscaledTime + 0.18f;
            int result = pattern.Choose(index);
            pads[index].Flash(0.25f);
            audioSource.PlayOneShot(tones[index], result < 0 ? 0.4f : 1f);
            if (result < 0)
            {
                accepting = false;
                routine = StartCoroutine(Retry());
            }
            else if (result == 1)
            {
                accepting = false;
                routine = StartCoroutine(Celebrate());
            }
            else status.text = "Your turn!  " + pattern.Progress + " / " + pattern.sequence.Count;
        }
        IEnumerator Retry()
        {
            busy = true;
            status.text = "Oops! Same pattern — try again.";
            yield return new WaitForSeconds(1.2f);
            yield return ShowPattern();
        }
        IEnumerator Celebrate()
        {
            busy = true;
            status.text = pattern.Round == 5 ? "You did it! Select START to play again." : "Nice! Next round...";
            for (int i = 0; i < 4; i++)
            {
                pads[i].Flash(0.6f);
                audioSource.PlayOneShot(tones[i]);
                yield return new WaitForSeconds(0.15f);
            }
            yield return new WaitForSeconds(0.7f);
            if (pattern.Round == 5) { won = true; busy = false; yield break; }
            pattern.NextRound();
            yield return ShowPattern();
        }
        static AudioClip MakeTone(float frequency, float duration)
        {
            const int rate = 24000;
            var samples = new float[(int)(rate * duration)];
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)rate;
                samples[i] = Mathf.Sin(2 * Mathf.PI * frequency * t) * Mathf.Sin(Mathf.PI * i / samples.Length) * 0.4f;
            }
            var clip = AudioClip.Create("Orb tone", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }
        void OnDestroy()
        {
            if (routine != null) StopCoroutine(routine);
            foreach (var material in materials) Destroy(material);
            foreach (var tone in tones) Destroy(tone);
        }
    }

    public sealed class EchoOrb : MonoBehaviour
    {
        public Color color;
        public System.Action clicked;
        XRSimpleInteractable target;
        Renderer visual;
        MaterialPropertyBlock block;
        Vector3 scale;
        float flashUntil;
        void Awake()
        {
            target = GetComponent<XRSimpleInteractable>();
            visual = GetComponent<Renderer>();
            block = new MaterialPropertyBlock();
            scale = transform.localScale;
        }
        void OnEnable() { target.selectEntered.AddListener(Selected); }
        void OnDisable() { target.selectEntered.RemoveListener(Selected); }
        void Selected(SelectEnterEventArgs args) { clicked?.Invoke(); }
        public void Flash(float duration) { flashUntil = Time.time + duration; }
        void Update()
        {
            bool lit = Time.time < flashUntil;
            block.SetColor("_BaseColor", lit ? Color.Lerp(color, Color.white, 0.5f) : color * (target.isHovered ? 0.85f : 0.45f));
            visual.SetPropertyBlock(block);
            transform.localScale = scale * (lit ? 1.12f : target.isHovered ? 1.05f : 1f);
        }
    }
}
