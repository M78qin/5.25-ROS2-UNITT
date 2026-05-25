using UnityEditor;
using UnityEngine;

namespace DigitalTwin.Editor
{
    [CustomEditor(typeof(ExperimentSessionController))]
    public sealed class ExperimentSessionControllerEditor : UnityEditor.Editor
    {
        private static bool _debugFoldout;

        public override void OnInspectorGUI()
        {
            ExperimentSessionController controller = (ExperimentSessionController)target;

            serializedObject.Update();
            DrawPanel(controller, serializedObject);
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawPanel(ExperimentSessionController controller, SerializedObject so)
        {
            EditorGUILayout.LabelField("Manual Twin Experiment", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Single manual lifecycle entry: connect first, then session, stream, motion, and paper recording.", MessageType.Info);
            if (!Application.isPlaying && GUILayout.Button("Apply Main Paper Defaults"))
            {
                Undo.RecordObject(controller, "Apply Main Paper Defaults");
                controller.ApplyMainPaperExperimentDefaults();
                EditorUtility.SetDirty(controller);
                so.Update();
            }

            bool canSend = Application.isPlaying;
            if (!canSend)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to send commands.", MessageType.None);
            }

            DrawStatus(controller);
            EditorGUILayout.Space(6f);
            DrawConnection(controller, canSend);
            EditorGUILayout.Space(6f);
            DrawSessionAndChannels(controller, so, canSend);
            EditorGUILayout.Space(6f);
            DrawStreamAndRecord(controller, canSend);
            EditorGUILayout.Space(6f);
            DrawMotionAndEvents(controller, so, canSend);
            EditorGUILayout.Space(6f);
            DrawSettings(controller, so, canSend);
            EditorGUILayout.Space(6f);
            DrawDebug(controller, so, canSend);
        }

        private static void DrawStatus(ExperimentSessionController controller)
        {
            EditorGUILayout.LabelField("State", controller.State.ToString());
            EditorGUILayout.LabelField("Experiment", string.IsNullOrEmpty(controller.ExperimentId) ? "--" : controller.ExperimentId);
            EditorGUILayout.LabelField("Session", string.IsNullOrEmpty(controller.SessionId) ? "--" : controller.SessionId);
            EditorGUILayout.LabelField("Phase / Segment", $"{controller.PhaseId} / {controller.SegmentId}");
            EditorGUILayout.LabelField("Stream / Record", $"{controller.StreamEnabled} / {controller.RecordEnabled}");
            EditorGUILayout.LabelField("Last", $"{controller.LastStatus} {controller.LastError}");
        }

        private static void DrawConnection(ExperimentSessionController controller, bool canSend)
        {
            EditorGUILayout.LabelField("Connection", EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(!canSend);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Connect")) controller.Connect();
            if (GUILayout.Button("Disconnect")) controller.Disconnect();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        private static void DrawSessionAndChannels(ExperimentSessionController controller, SerializedObject so, bool canSend)
        {
            EditorGUILayout.LabelField("Session & Channels", EditorStyles.boldLabel);
            Prop(so, "experimentId");
            Prop(so, "mode");
            Prop(so, "sourceId");
            Prop(so, "randomSeed");

            EditorGUILayout.BeginHorizontal();
            Prop(so, "dartSource");
            Prop(so, "ros2LikeSource");
            EditorGUILayout.EndHorizontal();

            Prop(so, "jointPosition");
            Prop(so, "jointVelocity");
            Prop(so, "jointEffort");
            Prop(so, "toolForce");
            Prop(so, "tcpPose");
            Prop(so, "extraSignals");

            so.ApplyModifiedProperties();
            EditorGUI.BeginDisabledGroup(!canSend);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create Session")) controller.CreateSession();
            if (GUILayout.Button("Close Session")) controller.CloseSession();
            if (GUILayout.Button("Apply Channels")) controller.ApplyChannels();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        private static void DrawStreamAndRecord(ExperimentSessionController controller, bool canSend)
        {
            EditorGUILayout.LabelField("Stream & Record", EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(!canSend);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Start Stream")) controller.StartStream();
            if (GUILayout.Button("Stop Stream")) controller.StopStream();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Start Record")) controller.StartRecord();
            if (GUILayout.Button("Pause Record")) controller.PauseRecord();
            if (GUILayout.Button("Resume Record")) controller.ResumeRecord();
            if (GUILayout.Button("Stop Record")) controller.StopRecord();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        private static void DrawMotionAndEvents(ExperimentSessionController controller, SerializedObject so, bool canSend)
        {
            EditorGUILayout.LabelField("Motion & Events", EditorStyles.boldLabel);
            Prop(so, "presetMotionMode");
            Prop(so, "phaseNote");
            Prop(so, "eventNote");
            EditorGUILayout.Space(4f);
            Prop(so, "controlModePresetTarget");
            Prop(so, "enterControlModeBeforeMove");
            so.ApplyModifiedProperties();

            EditorGUI.BeginDisabledGroup(!canSend);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Start Motion")) controller.StartPresetMotion();
            if (GUILayout.Button("Halt")) controller.HaltMotion();
            if (GUILayout.Button("Mark Event")) controller.MarkEvent();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Control Mode")) controller.EnterControlMode();
            if (GUILayout.Button("Ctrl + Move")) controller.EnterControlModeWithPreset();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        private static void DrawSettings(ExperimentSessionController controller, SerializedObject so, bool canSend)
        {
            EditorGUILayout.LabelField("Runtime Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            Prop(so, "dartHz");
            Prop(so, "ros2Hz");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Network Impairment", EditorStyles.miniLabel);
            Prop(so, "delayMs");
            Prop(so, "jitterMs");
            EditorGUILayout.BeginHorizontal();
            Prop(so, "dropRate");
            Prop(so, "duplicateRate");
            Prop(so, "reorderRate");
            EditorGUILayout.EndHorizontal();

            so.ApplyModifiedProperties();
            EditorGUI.BeginDisabledGroup(!canSend);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Channels + Hz")) controller.ApplyChannels();
            if (GUILayout.Button("Apply Impairment")) controller.ApplyNetworkImpairment();
            if (GUILayout.Button("Reset Impairment")) controller.ResetNetworkImpairment();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        private static void DrawDebug(ExperimentSessionController controller, SerializedObject so, bool canSend)
        {
            _debugFoldout = EditorGUILayout.Foldout(_debugFoldout, "Debug / Advanced", true);
            if (!_debugFoldout)
            {
                return;
            }

            Prop(so, "commandSender");
            Prop(so, "dartBridge");
            Prop(so, "recorder");
            Prop(so, "enableLegacyFrameRecorder");
            Prop(so, "idleMode");
            Prop(so, "controlMode");
            so.ApplyModifiedProperties();

            EditorGUI.BeginDisabledGroup(!canSend);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Get Status")) controller.GetStatus();
            if (GUILayout.Button("New Phase")) controller.NewPhase();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        private static void Prop(SerializedObject so, string name)
        {
            SerializedProperty prop = so.FindProperty(name);
            if (prop != null)
            {
                EditorGUILayout.PropertyField(prop);
            }
        }
    }
}
