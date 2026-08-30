using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ProtocolRunVR.MetaHands
{
    [AddComponentMenu("ProtocolRun VR/Meta Study HUD")]
    public sealed class MetaStudyHud : MonoBehaviour
    {
        public ProtocolRunMetaSession session;
        [Tooltip("Place this panel once, in front of the HMD when hand tracking becomes available.")]
        public bool placeInFrontOfHead = true;
        private readonly List<Control> controls = new List<Control>();
        private TextMesh instruction, state, rating;
        private Font font;
        private bool positioned;
        private int difficulty;
        private float lastPress;
        private sealed class Control
        {
            public Collider collider;
            public Action action;
            public Func<bool> allowed;
            public GameObject button;
            public GameObject label;
        }

        private void Start()
        {
            if (!session) session = FindFirstObjectByType<ProtocolRunMetaSession>();
            if (!session) return;
            session.hud = this;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var panel = Box("Panel", new Vector3(0, 0, 0.012f), new Vector3(0.90f, 0.78f, 0.015f), new Color(0.05f, 0.08f, 0.10f));
            panel.GetComponent<Collider>().enabled = false;
            Text("ProtocolRun-VR", new Vector3(-0.42f, 0.35f, -0.01f), 0.010f, Color.white);
            instruction = Text("Waiting for connection.", new Vector3(-0.42f, 0.26f, -0.01f), 0.006f, Color.white);
            state = Text("", new Vector3(-0.42f, 0.05f, -0.01f), 0.0042f, Color.cyan);
            Button("AGREE / START", new Vector3(-0.22f, -0.09f, 0), 0.36f, () => session.AcceptConsent(), () => session.Current != null && !session.Consented && !session.Blocked);
            Button("HELP: cannot grab", new Vector3(0.22f, -0.09f, 0), 0.40f, () => session.HelpGrab(), () => !session.StudyPaused && session.Current.step == 3);
            rating = Text("Difficulty: choose 1-7", new Vector3(-0.42f, -0.16f, -0.01f), 0.005f, Color.white);
            for (int i = 1; i <= 7; i++)
            {
                int choice = i;
                Button(i.ToString(), new Vector3(-0.30f + (i - 1) * 0.10f, -0.24f, 0), 0.075f,
                    () => { difficulty = choice; rating.text = "Difficulty: " + choice + "/7"; }, () => !session.StudyPaused && session.Current.step == 5);
            }
            Button("STOP", new Vector3(-0.22f, -0.34f, 0), 0.22f, () => session.PauseStudy(), () => !session.StudyPaused);
            Button("SUBMIT", new Vector3(0.22f, -0.34f, 0), 0.25f, () => session.SubmitSurvey(difficulty), () => !session.StudyPaused && session.Current.step == 5 && difficulty > 0);
        }
        private void Update()
        {
            if (!session || !instruction) return;
            if (!positioned && session.head && session.HasTrackedHand)
            {
                if (placeInFrontOfHead)
                {
                    Vector3 forward = Vector3.ProjectOnPlane(session.head.forward, Vector3.up).normalized;
                    if (forward.sqrMagnitude < 0.1f) forward = Vector3.forward;
                    transform.position = session.head.position + forward * 0.60f - Vector3.up * 0.20f;
                    transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
                }
                positioned = true;
            }
            instruction.text = Wrap(session.Instruction, 55);
            state.text = session.ConnectionStatus + " | queued: " + session.PendingCount + "\nPinch near a button with your hand. Panel stays fixed.";
            bool survey = !session.StudyPaused && session.Current != null && session.Current.step == 5;
            rating.gameObject.SetActive(survey);
            foreach (var control in controls)
            {
                bool visible = control.allowed();
                if (control.button.activeSelf != visible) control.button.SetActive(visible);
                if (control.label.activeSelf != visible) control.label.SetActive(visible);
            }
        }
        public bool TryPinch(Vector3 handPosition)
        {
            if (!positioned || Time.realtimeSinceStartup - lastPress < 0.7f) return false;
            Control nearest = null; float distance = 0.10f;
            foreach (var control in controls)
            {
                if (!control.collider || !control.allowed()) continue;
                float d = Vector3.Distance(handPosition, control.collider.ClosestPoint(handPosition));
                if (d < distance) { distance = d; nearest = control; }
            }
            if (nearest == null) return false;
            lastPress = Time.realtimeSinceStartup; nearest.action(); return true;
        }
        private void Button(string label, Vector3 pos, float width, Action action, Func<bool> allowed)
        {
            var button = Box(label, pos, new Vector3(width, 0.048f, 0.015f), new Color(0.05f, 0.30f, 0.26f));
            var text = Text(label, pos + new Vector3(-width * 0.43f, 0.014f, -0.011f), 0.0045f, Color.white);
            controls.Add(new Control { collider = button.GetComponent<Collider>(), action = action, allowed = allowed, button = button, label = text.gameObject });
        }
        private GameObject Box(string label, Vector3 pos, Vector3 size, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = label;
            go.transform.SetParent(transform, false); go.transform.localPosition = pos; go.transform.localScale = size;
            go.GetComponent<Collider>().isTrigger = true;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader)
            {
                var material = new Material(shader);
                material.SetColor(material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color", color);
                go.GetComponent<Renderer>().material = material;
            }
            return go;
        }
        private TextMesh Text(string value, Vector3 pos, float size, Color color)
        {
            var go = new GameObject("Label"); go.transform.SetParent(transform, false); go.transform.localPosition = pos;
            var text = go.AddComponent<TextMesh>(); text.text = value; text.font = font; text.fontSize = 64;
            text.characterSize = size; text.anchor = TextAnchor.UpperLeft; text.color = color; text.lineSpacing = 0.9f;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material; return text;
        }
        private static string Wrap(string value, int limit)
        {
            var output = new StringBuilder(); int count = 0;
            foreach (string word in (value ?? "").Split(' '))
            { if (count + word.Length > limit) { output.Append('\n'); count = 0; } output.Append(word).Append(' '); count += word.Length + 1; }
            return output.ToString();
        }
    }
}
