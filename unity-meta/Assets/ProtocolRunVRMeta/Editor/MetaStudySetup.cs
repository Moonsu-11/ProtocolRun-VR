using System;
using System.IO;
using System.Linq;
using Oculus.Interaction.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProtocolRunVR.MetaHands.Editor
{
    public static class MetaStudySetup
    {
        [MenuItem("Tools/ProtocolRun VR/Install Network Study")]
        public static void Install()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play before installation.");
            var objects = UnityEngine.Object.FindObjectsByType<ProtocolRunMetaObject>(FindObjectsSortMode.None);
            if (objects.Length != 3 || !new[] { "CUBE_A", "CUBE_B", "CUBE_C" }.All(id => objects.Count(o => o.ObjectId == id) == 1))
                throw new InvalidOperationException("Expected exactly CUBE_A, CUBE_B and CUBE_C with Meta Study Object already configured. This installer never adds grab components.");
            var session = UnityEngine.Object.FindFirstObjectByType<ProtocolRunMetaSession>();
            if (!session)
            {
                var go = new GameObject("ProtocolRunSession"); Undo.RegisterCreatedObjectUndo(go, "Install ProtocolRun session");
                session = Undo.AddComponent<ProtocolRunMetaSession>(go);
            }
            Undo.RecordObject(session, "Configure ProtocolRun session");
            session.studyObjects = objects;
            if (Camera.main) session.head = Camera.main.transform;
            var rigHands = UnityEngine.Object.FindObjectsByType<Hand>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(h => h && h.GetType() == typeof(Hand)).ToArray();
            var left = rigHands.FirstOrDefault(h => h.gameObject.name == "ComprehensiveInteractorsLeft")
                ?? rigHands.FirstOrDefault(h => h.gameObject.name.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0);
            var right = rigHands.FirstOrDefault(h => h.gameObject.name == "ComprehensiveInteractorsRight")
                ?? rigHands.FirstOrDefault(h => h.gameObject.name.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0);
            if (left && right && left != right)
            {
                session.leftHandSource = left;
                session.rightHandSource = right;
            }
            if (!session.hud)
            {
                var go = new GameObject("ProtocolRunHUD"); Undo.RegisterCreatedObjectUndo(go, "Create study HUD");
                go.transform.SetParent(session.transform, false);
                var hud = Undo.AddComponent<MetaStudyHud>(go); hud.session = session; session.hud = hud;
            }
            var visual = GameObject.Find("DropZONE");
            if (!visual) throw new InvalidOperationException("Create/name your visible drop area DropZONE, then run this installer again.");
            var marker = visual.transform.Find("ProtocolRunDropVolume");
            if (!marker)
            {
                var go = new GameObject("ProtocolRunDropVolume"); Undo.RegisterCreatedObjectUndo(go, "Create drop volume");
                var renderer = visual.GetComponent<Renderer>();
                var bounds = renderer ? renderer.bounds : new Bounds(visual.transform.position, new Vector3(0.4f, 0.05f, 0.4f));
                go.transform.position = new Vector3(bounds.center.x, bounds.max.y + 0.18f, bounds.center.z);
                // Preserve a world-unit volume even if the visual marker has a thin nonuniform scale.
                go.transform.SetParent(visual.transform, true);
                var collider = Undo.AddComponent<BoxCollider>(go); collider.isTrigger = true;
                collider.size = new Vector3(Mathf.Max(0.3f, bounds.size.x), 0.36f, Mathf.Max(0.3f, bounds.size.z));
                var zone = Undo.AddComponent<MetaStudyDropZone>(go); zone.session = session;
            }
            else
            {
                var zone = marker.GetComponent<MetaStudyDropZone>();
                if (zone) { Undo.RecordObject(zone, "Set drop session"); zone.session = session; }
            }
            EditorUtility.SetDirty(session); EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
            Selection.activeGameObject = session.gameObject;
            Debug.Log("[ProtocolRun] Network study installed. Save scene. Configure a NEW meta-hands-v1 connection. Check drop volume and HUD reachability in VR; hand sources can be explicitly assigned if automatic discovery is ambiguous.");
        }
    }

    public sealed class MetaConnectionWindow : EditorWindow
    {
        private string json = "";
        [MenuItem("Tools/ProtocolRun VR/Configure Connection")]
        private static void Open() => GetWindow<MetaConnectionWindow>("Study connection");
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Paste Connection JSON copied from the dashboard. Stored outside Assets, never in the scene. A new Play requires a new session. Do not share or commit this file.", MessageType.Info);
            json = EditorGUILayout.TextArea(json, GUILayout.MinHeight(100));
            if (GUILayout.Button("Save private connection"))
            {
                var connection = JsonUtility.FromJson<MetaConnection>(json);
                if (!ProtocolRunMetaSession.ValidConnection(connection))
                { EditorUtility.DisplayDialog("Invalid connection", "Use the complete Connection JSON from the dashboard. HTTPS is required except loopback.", "OK"); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(ProtocolRunMetaSession.ConnectionPath));
                File.WriteAllText(ProtocolRunMetaSession.ConnectionPath, JsonUtility.ToJson(connection));
                json = "";
                EditorUtility.DisplayDialog("Saved", "Connection saved outside the Unity project. Enter Play and accept consent inside VR.", "OK");
            }
        }
    }

    [CustomEditor(typeof(ProtocolRunMetaSession))]
    public sealed class MetaSessionEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            using (new EditorGUI.DisabledScope(Application.isPlaying)) DrawDefaultInspector();
            var session = (ProtocolRunMetaSession)target;
            if (!Application.isPlaying) return;
            EditorGUILayout.HelpBox(session.Instruction, session.Blocked ? MessageType.Error : MessageType.Info);
            EditorGUILayout.LabelField("Connection", session.ConnectionStatus);
            EditorGUILayout.LabelField("Pending events", session.PendingCount.ToString());
            using (new EditorGUI.DisabledScope(!session.CanInjectFault || !session.Target || !session.Target.CanInject))
                if (GUILayout.Button("RESEARCHER DEMO: Inject B fault")) session.InjectDemoFault();
            EditorGUILayout.HelpBox("meta-hands-v1 automatically disables B immediately after consent. Practice only with A. Do not manually restore B; wait for Gemini, the firewall and a fresh same-object retest.", MessageType.Info);
            Repaint();
        }
    }
}
