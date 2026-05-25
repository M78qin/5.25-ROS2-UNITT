using UnityEditor;
using UnityEngine;

namespace DigitalTwin.Editor
{
    [CustomEditor(typeof(DartEExperimentPanel))]
    public sealed class DartEExperimentPanelEditor : UnityEditor.Editor
    {
        private static bool _debugFoldout;

        public override void OnInspectorGUI()
        {
            DartEExperimentPanel panel = (DartEExperimentPanel)target;
            serializedObject.Update();

            EditorGUILayout.LabelField("Dart_E 实验操作台", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("薄封装入口：复用现有 DartStudioBridge / SessionController / PaperRecorder，不创建第二套 socket。", MessageType.Info);

            DrawStatus(panel);
            EditorGUILayout.Space(6f);
            DrawOfflineTools(panel);
            EditorGUILayout.Space(6f);

            bool canSend = Application.isPlaying;
            if (!canSend)
            {
                EditorGUILayout.HelpBox("进入 Play Mode 后才会向 DartStudio/Robot 发送命令。", MessageType.None);
            }

            DrawBindings();
            EditorGUILayout.Space(6f);
            DrawSessionFlow(panel, canSend);
            EditorGUILayout.Space(6f);
            DrawChannelsAndRates(panel, canSend);
            EditorGUILayout.Space(6f);
            DrawRecording(panel, canSend);
            EditorGUILayout.Space(6f);
            DrawControl(panel, canSend);
            EditorGUILayout.Space(6f);
            DrawDebug(panel, canSend);

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawStatus(DartEExperimentPanel panel)
        {
            EditorGUILayout.LabelField("状态", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Bridge / TCP", $"{OnOff(panel.BridgeConnected)} / {OnOff(panel.TcpConnected)}");
            EditorGUILayout.LabelField("Session", string.IsNullOrEmpty(panel.ActiveSessionId) ? "--" : panel.ActiveSessionId);
            EditorGUILayout.LabelField("Stream / Record", $"{panel.StreamEnabled} / {panel.RecordEnabled}");
            EditorGUILayout.LabelField("Last", $"{panel.LastAction} | {panel.LastStatus} {panel.LastError}");
        }

        private static void DrawOfflineTools(DartEExperimentPanel panel)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("解析绑定", "自动查找场景里的 SessionController、DartStudioBridge、CommandSender、PaperRecorder。")))
            {
                panel.ResolveBindings();
                MarkDirty(panel);
            }

            if (!Application.isPlaying && GUILayout.Button(new GUIContent("应用主实验默认值", "joint_position + tool_force 开，其余主热路径关闭。")))
            {
                Undo.RecordObject(panel, "Apply Dart_E Defaults");
                panel.ApplyMainDefaults();
                MarkDirty(panel);
                GUI.FocusControl(null);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawBindings()
        {
            EditorGUILayout.LabelField("绑定对象", EditorStyles.boldLabel);
            Prop("sessionController");
            Prop("dartBridge");
            Prop("commandSender");
            Prop("paperRecorder");
        }

        private static void DrawSessionFlow(DartEExperimentPanel panel, bool canSend)
        {
            EditorGUILayout.LabelField("会话流程", EditorStyles.boldLabel);
            EditorGUI.BeginDisabledGroup(!canSend);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("连接")) panel.Connect();
            if (GUILayout.Button("断开")) panel.DisconnectSafe();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("连接 + 创建")) panel.ConnectAndCreate();
            if (GUILayout.Button("创建会话")) panel.CreateSession();
            if (GUILayout.Button("关闭会话")) panel.CloseSession();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
        }

        private void DrawChannelsAndRates(DartEExperimentPanel panel, bool canSend)
        {
            EditorGUILayout.LabelField("数据通道与频率", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            Prop("dartSource");
            Prop("ros2LikeSource");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            Prop("jointPosition");
            Prop("toolForce");
            Prop("tcpPose");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            Prop("jointVelocity");
            Prop("jointEffort");
            Prop("extraSignals");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            Prop("streamHz");
            Prop("jointHz");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            Prop("forceHz");
            Prop("tcpHz");
            EditorGUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();
            EditorGUI.BeginDisabledGroup(!canSend);
            if (GUILayout.Button(new GUIContent("应用通道和频率", "发送 SET_CHANNELS，包含 dart_hz/stream_hz/joint_hz/force_hz/tcp_hz。")))
            {
                panel.ApplyChannelsAndRates();
            }
            EditorGUI.EndDisabledGroup();
        }

        private static void DrawRecording(DartEExperimentPanel panel, bool canSend)
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

        private void DrawControl(DartEExperimentPanel panel, bool canSend)
        {
            EditorGUILayout.LabelField("双向控制", EditorStyles.boldLabel);
            Prop("idleMode");
            Prop("controlMode");
            Prop("enterControlModeBeforeMove");
            Prop("homeTargetDeg");
            Prop("presetTargetADeg");
            Prop("presetTargetBDeg");
            Prop("yOffsetMm");
            serializedObject.ApplyModifiedProperties();

            EditorGUI.BeginDisabledGroup(!canSend);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("进入空闲模式")) panel.EnterIdleMode();
            if (GUILayout.Button("启动预设测试")) panel.StartPresetTest();
            if (GUILayout.Button("进入控制模式")) panel.EnterControlMode();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("发送 Home")) panel.SendHome();
            if (GUILayout.Button("发送点 A")) panel.SendPresetA();
            // 发送点 B 暂禁: 控制模式下笛卡尔 amovel 易触发奇异点
            // if (GUILayout.Button("发送点 B")) panel.SendPresetB();
            EditorGUILayout.EndHorizontal();

            GUI.backgroundColor = new Color(1f, 0.55f, 0.55f);
            if (GUILayout.Button("HALT / 停止运动")) panel.Halt();
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();
        }

        private void DrawDebug(DartEExperimentPanel panel, bool canSend)
        {
            _debugFoldout = EditorGUILayout.Foldout(_debugFoldout, "调试 / 事件", true);
            if (!_debugFoldout)
            {
                return;
            }

            Prop("experimentId");
            Prop("sourceId");
            Prop("randomSeed");
            Prop("eventNote");
            EditorGUILayout.LabelField("Last JSON", string.IsNullOrEmpty(panel.LastJson) ? "--" : panel.LastJson);
            serializedObject.ApplyModifiedProperties();

            EditorGUI.BeginDisabledGroup(!canSend);
            if (GUILayout.Button("标记事件")) panel.MarkEvent();
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

        private static string OnOff(bool value)
        {
            return value ? "ON" : "OFF";
        }

        private static void MarkDirty(Object target)
        {
            EditorUtility.SetDirty(target);
        }
    }
}
