using UnityEditor;
using UnityEngine;

namespace ProtocolRunVR.MetaHands.Editor
{
    [CustomEditor(typeof(ProtocolRunMetaObject))]
    public sealed class ProtocolRunMetaObjectEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // Permissions and IDs are captured once per run, never changed mid-session.
            using (new EditorGUI.DisabledScope(Application.isPlaying)) DrawDefaultInspector();
            var studyObject = (ProtocolRunMetaObject)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Module Version", ProtocolRunMetaObject.ModuleVersion);
            EditorGUILayout.LabelField("Runtime Phase", studyObject.Phase);
            EditorGUILayout.HelpBox(studyObject.LastMessage, MessageType.Info);
            EditorGUILayout.HelpBox(studyObject.ServerManaged ? "Server-managed run. Manual restore is locked; only an approved server command can restore B." : "Local demo controls. Add Meta Network Session for server diagnosis/recovery.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Application.isPlaying || !studyObject.CanInject))
                if (GUILayout.Button("DEMO: Inject Hand Grab Fault")) studyObject.InjectDemoFault();
            using (new EditorGUI.DisabledScope(!Application.isPlaying || !studyObject.CanRestoreDemo))
                if (GUILayout.Button("DEMO: Restore Baseline")) studyObject.RestoreDemoBaseline();
            if (Application.isPlaying) Repaint();
        }
    }
}
