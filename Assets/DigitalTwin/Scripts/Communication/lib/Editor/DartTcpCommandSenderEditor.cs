using UnityEditor;

namespace DigitalTwin.Editor
{
    [CustomEditor(typeof(DartTcpCommandSender))]
    public sealed class DartTcpCommandSenderEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.LabelField("Dart TCP Command Sender", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Command service only. Use ExperimentSessionController as the manual experiment/control panel.", MessageType.Info);
            DrawDefaultInspector();
        }
    }
}
