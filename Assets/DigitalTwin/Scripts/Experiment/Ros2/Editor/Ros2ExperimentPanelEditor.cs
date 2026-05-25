using UnityEditor;
using UnityEngine;

namespace DigitalTwin.Editor
{
    [CustomEditor(typeof(Ros2ExperimentPanel))]
    public sealed class Ros2ExperimentPanelEditor : UnityEditor.Editor
    {
        private static bool _debugFoldout;

        public override void OnInspectorGUI()
        {
            Ros2ExperimentPanel panel = (Ros2ExperimentPanel)target;
            serializedObject.Update();

            EditorGUILayout.LabelField("ROS2 / MoveIt2 实验操作台", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("ROS2 专用入口：复用 Ros2Bridge / TwinCommandController / TwinPaperRecorder，不创建第二套 ROS2 通信脚本。", MessageType.Info);

            DrawStatus(panel);
            EditorGUILayout.Space(6f);
            DrawOfflineTools(panel);
            EditorGUILayout.Space(6f);

            bool canSend = Application.isPlaying;
            if (!canSend)
            {
                EditorGUILayout.HelpBox("进入 Play Mode 后才会连接 ROS2 或发布 /dt/cmd/*。", MessageType.None);
            }

            DrawBindings();
            EditorGUILayout.Space(6f);
            DrawTopics(panel, canSend);
            EditorGUILayout.Space(6f);
            DrawSession(panel, canSend);
            EditorGUILayout.Space(6f);
            DrawChannels(panel, canSend);
            EditorGUILayout.Space(6f);
            DrawRecording(panel, canSend);
            EditorGUILayout.Space(6f);
            DrawControl(panel, canSend);
            EditorGUILayout.Space(6f);
            DrawDebug(panel, canSend);

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawStatus(Ros2ExperimentPanel panel)
        {
            EditorGUILayout.LabelField("状态", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("ROS2 Running / Connected", $"{OnOff(panel.Ros2Running)} / {OnOff(panel.Ros2Connected)}");
            EditorGUILayout.LabelField("Session", string.IsNullOrEmpty(panel.ActiveSessionId) ? "--" : panel.ActiveSessionId);
            EditorGUILayout.LabelField("Stream / Record", $"{panel.StreamEnabled} / {panel.RecordEnabled}");
            EditorGUILayout.LabelField("ROS2 Hz / Dropped", $"{panel.Ros2FrameRateHz:0.0} / {panel.Ros2DroppedFrames}");
            EditorGUILayout.LabelField("Last", $"{panel.LastAction} | {panel.LastStatus} {panel.LastError}");
        }

        private static void DrawOfflineTools(Ros2ExperimentPanel panel)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("解析绑定", "自动查找 Runtime、Ros2Bridge、CommandController、PaperRecorder。")))
            {
                panel.ResolveBindings();
                MarkDirty(panel);
            }

            if (!Application.isPlaying && GUILayout.Button(new GUIContent("应用 ROS2 默认值", "设置 /dsr01/joint_states 与 /dt/cmd/* topic，主采集目标 200Hz。")))
            {
                Undo.RecordObject(panel, "Apply ROS2 Defaults");
                panel.ApplyRos2Defaults();
                MarkDirty(panel);
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBindings()
        {
            EditorGUILayout.LabelField("绑定对象", EditorStyles.boldLabel);
            Prop("runtime");
            Prop("ros2Bridge");
            Prop("commandController");
            Prop("paperRecorder");
        }

        private void DrawTopics(Ros2ExperimentPanel panel, bool canSend)
        {
            EditorGUILayout.LabelField("ROS2 Topics", EditorStyles.boldLabel);
            Prop("jointStateTopic");
            Prop("moveJointTopic");
            Prop("haltTopic");
            Prop("setModeTopic");
            Prop("modeStatusTopic");
            Prop("motionStatusTopic");
            Prop("enableWrenchTopic");
            Prop("wrenchTopic");
            Prop("enableTcpPoseTopic");
            Prop("tcpPoseTopic");

            serializedObject.ApplyModifiedProperties();
            EditorGUI.BeginDisabledGroup(!canSend);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("应用 Topic")) panel.ApplyTopicSettings();
            if (GUILayout.Button("连接 ROS2")) panel.ConnectRos2();
            if (GUILayout.Button("断开 ROS2")) panel.DisconnectRos2();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        private static void DrawSession(Ros2ExperimentPanel panel, bool canSend)
        {
            EditorGUILayout.LabelField("会话流程", EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(!canSend);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("创建会话")) panel.CreateSession();
            if (GUILayout.Button("关闭会话")) panel.CloseSession();
            if (GUILayout.Button("标记事件")) panel.MarkEvent();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        private void DrawChannels(Ros2ExperimentPanel panel, bool canSend)
        {
            EditorGUILayout.LabelField("数据通道与频率", EditorStyles.boldLabel);
            Prop("ros2MainHz");
            EditorGUILayout.BeginHorizontal();
            Prop("jointPosition");
            Prop("jointVelocity");
            Prop("jointEffort");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            Prop("toolForce");
            Prop("tcpPose");
            Prop("extraSignals");
            EditorGUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();
            EditorGUI.BeginDisabledGroup(!canSend);
            if (GUILayout.Button(new GUIContent("应用通道和频率", "写入 Unity 论文记录事件；ROS2 端 200Hz 采集需启动 bridge 时传 csv_main_hz:=200。")))
            {
                panel.ApplyChannelsAndRates();
            }
            EditorGUI.EndDisabledGroup();
        }

        private static void DrawRecording(Ros2ExperimentPanel panel, bool canSend)
        {
            EditorGUILayout.LabelField("数据流与论文记录", EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(!canSend);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("开启数据流")) panel.StartStream();
            if (GUILayout.Button("停止数据流")) panel.StopStream();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("开启论文记录")) panel.StartPaperRecord();
            if (GUILayout.Button("停止论文记录")) panel.StopPaperRecord();
            if (GUILayout.Button("停止记录 + 数据流")) panel.StopRecordAndStream();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        private void DrawControl(Ros2ExperimentPanel panel, bool canSend)
        {
            EditorGUILayout.LabelField("双向控制", EditorStyles.boldLabel);
            Prop("speedPercent");
            Prop("publishDirectlyToRos2");
            Prop("homeTargetDeg");
            Prop("presetTargetADeg");
            Prop("presetTargetBDeg");
            serializedObject.ApplyModifiedProperties();

            EditorGUI.BeginDisabledGroup(!canSend);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("发送 Home")) panel.SendHome();
            if (GUILayout.Button("发送点 A")) panel.SendPresetA();
            if (GUILayout.Button("发送点 B")) panel.SendPresetB();
            EditorGUILayout.EndHorizontal();
            GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
            if (GUILayout.Button("HALT / 停止运动")) panel.Halt();
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();
        }

        private void DrawDebug(Ros2ExperimentPanel panel, bool canSend)
        {
            _debugFoldout = EditorGUILayout.Foldout(_debugFoldout, "调试 / 实验字段", true);
            if (!_debugFoldout) return;

            Prop("experimentId");
            Prop("sourceId");
            Prop("phaseId");
            Prop("segmentId");
            serializedObject.ApplyModifiedProperties();

            EditorGUI.BeginDisabledGroup(!canSend);
            if (GUILayout.Button("写入 ROS2 面板标记")) panel.MarkEvent("ROS2_PANEL_DEBUG_MARK");
            EditorGUI.EndDisabledGroup();
        }

        private void Prop(string name)
        {
            SerializedProperty prop = serializedObject.FindProperty(name);
            if (prop != null)
            {
                EditorGUILayout.PropertyField(prop);
            }
        }

        private static string OnOff(bool value) => value ? "ON" : "OFF";
        private static void MarkDirty(Object target) => EditorUtility.SetDirty(target);
    }
}
