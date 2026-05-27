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
            EditorGUILayout.HelpBox(
                "真机链路：真机→ROS2(RViz)→Unity。待机=只收状态（可改通道/数据流/记录/Capture）；控制模式才可发 Home/A/B。",
                MessageType.Info);

            DrawStatus(panel);
            EditorGUILayout.Space(6f);
            DrawOfflineTools(panel);
            EditorGUILayout.Space(6f);

            bool canSend = Application.isPlaying;
            if (!canSend)
            {
                EditorGUILayout.HelpBox("进入 Play Mode 后才会连接 ROS2 或发布 /dt/cmd/*。", MessageType.None);
            }

            DrawOperatingMode(panel, canSend);
            EditorGUILayout.Space(6f);
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
            EditorGUILayout.LabelField("ROS2 Running / Link / Data", $"{OnOff(panel.Ros2Running)} / {OnOff(panel.Ros2LinkConnected)} / {OnOff(panel.Ros2Connected)}");
            EditorGUILayout.LabelField("Session", string.IsNullOrEmpty(panel.ActiveSessionId) ? "--" : panel.ActiveSessionId);
            EditorGUILayout.LabelField("Operating", panel.IsControlMode ? "CONTROL (motion ON)" : "STANDBY (receive-only)");
            string recordLabel = panel.RecordPending ? "pending" : panel.RecordEnabled.ToString();
            EditorGUILayout.LabelField("Stream / Record / ROS2", $"{panel.StreamEnabled} / {recordLabel} / {panel.Ros2RecordEnabled}");
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
            Prop("sessionStatusTopic");
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

        private static void DrawOperatingMode(Ros2ExperimentPanel panel, bool canSend)
        {
            EditorGUILayout.LabelField("运行模式（待机 / 控制）", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "待机：只收真机状态；可改通道、开关数据流、Capture 位姿、开论文记录。控制：才允许 Home/A/B。HALT 任意模式可用。",
                MessageType.None);
            EditorGUI.BeginDisabledGroup(!canSend);
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = panel.IsControlMode ? Color.white : new Color(0.75f, 0.9f, 1f);
            if (GUILayout.Button("待机模式 (Standby)"))
            {
                panel.SetIdleMode();
            }

            GUI.backgroundColor = panel.IsControlMode ? new Color(1f, 0.85f, 0.6f) : Color.white;
            if (GUILayout.Button("控制模式 (Control)"))
            {
                panel.SetControlMode();
            }

            GUI.backgroundColor = Color.white;
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
            EditorGUILayout.LabelField("数据通道（对齐真机 / channels.yaml）", EditorStyles.boldLabel);
            Prop("ros2MainHz");
            EditorGUILayout.LabelField("关节", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            Prop("jointPosition");
            Prop("jointVelocity");
            Prop("jointEffort");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("位姿 / 力", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            Prop("actualTcpPose");
            Prop("actualFlangePose");
            Prop("externalTcpForce");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            Prop("externalJointTorque");
            Prop("rawForceTorque");
            Prop("actualJointTorque");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField("目标 / 状态", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            Prop("targetJointPosition");
            Prop("targetTcpPosition");
            Prop("robotMode");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            Prop("robotState");
            Prop("controlMode");
            Prop("jointTemperature");
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            Prop("solutionSpace");
            Prop("operationSpeedRate");
            Prop("actualMotorTorque");
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
            if (!panel.IsControlMode)
            {
                EditorGUILayout.HelpBox("当前为待机模式。可采集/记录；发 Home/A/B 需切到控制模式。", MessageType.Warning);
            }

            Prop("speedPercent");
            Prop("presetJ1DeltaADeg");
            Prop("presetJ2DeltaBDeg");
            Prop("publishDirectlyToRos2");
            Prop("homeTargetDeg");
            Prop("presetTargetADeg");
            Prop("presetTargetBDeg");
            if (!string.IsNullOrEmpty(panel.CapturedPoseSummary))
            {
                EditorGUILayout.LabelField("Captured Home (deg)", panel.CapturedPoseSummary);
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUI.BeginDisabledGroup(!canSend);
            if (GUILayout.Button(new GUIContent("读取当前位姿 → Home/A/B",
                    "Home=当前; A=J1+Δ; B=J2+Δ。需已开启数据流。")))
            {
                panel.CaptureCurrentPoseAsPresets();
                MarkDirty(panel);
            }

            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(!canSend || !panel.IsControlMode);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("发送 Home")) panel.SendHome();
            if (GUILayout.Button("发送点 A")) panel.SendPresetA();
            if (GUILayout.Button("发送点 B")) panel.SendPresetB();
            EditorGUILayout.EndHorizontal();
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(!canSend);
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

    public static class Ros2ExperimentPanelPlanBMenu
    {
        private static Ros2ExperimentPanel FindPanel()
        {
            Ros2ExperimentPanel panel = Object.FindObjectOfType<Ros2ExperimentPanel>();
            if (panel == null)
            {
                Debug.LogError("[PlanB] Ros2ExperimentPanel not found in loaded scenes.");
            }

            return panel;
        }

        [MenuItem("DigitalTwin/ROS2 Plan B/0 Apply Minimal Channels (Edit Mode)")]
        public static void ApplyMinimalChannels()
        {
            Ros2ExperimentPanel panel = FindPanel();
            if (panel == null) return;

            Undo.RecordObject(panel, "Plan B Minimal Channels");
            panel.ApplyRos2Defaults();
            SerializedObject so = new SerializedObject(panel);
            so.FindProperty("ros2MainHz").floatValue = 100f;
            so.FindProperty("jointPosition").boolValue = true;
            so.FindProperty("jointVelocity").boolValue = false;
            so.FindProperty("jointEffort").boolValue = false;
            so.FindProperty("actualTcpPose").boolValue = false;
            so.FindProperty("actualFlangePose").boolValue = false;
            so.FindProperty("externalTcpForce").boolValue = false;
            so.FindProperty("externalJointTorque").boolValue = false;
            so.FindProperty("actualJointTorque").boolValue = false;
            so.FindProperty("actualMotorTorque").boolValue = false;
            so.FindProperty("rawForceTorque").boolValue = false;
            so.FindProperty("targetJointPosition").boolValue = false;
            so.FindProperty("targetTcpPosition").boolValue = false;
            so.FindProperty("robotMode").boolValue = false;
            so.FindProperty("robotState").boolValue = false;
            so.FindProperty("controlMode").boolValue = false;
            so.FindProperty("jointTemperature").boolValue = false;
            so.FindProperty("solutionSpace").boolValue = false;
            so.FindProperty("operationSpeedRate").boolValue = false;
            so.FindProperty("enableWrenchTopic").boolValue = false;
            so.FindProperty("enableTcpPoseTopic").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(panel);
            Debug.Log("[PlanB] Minimal channels applied: joint_position only @100Hz target.");
        }

        [MenuItem("DigitalTwin/ROS2 Plan B/1 Resolve Bindings", true)]
        private static bool PlayModeOnly() => Application.isPlaying;

        [MenuItem("DigitalTwin/ROS2 Plan B/1 Resolve Bindings")]
        public static void Step1Resolve()
        {
            Ros2ExperimentPanel panel = FindPanel();
            if (panel == null) return;
            panel.ResolveBindings();
            Debug.Log("[PlanB 1 Resolve] bindings resolved.");
        }

        [MenuItem("DigitalTwin/ROS2 Plan B/2 Connect ROS2", true)]
        private static bool PlayModeOnly2() => Application.isPlaying;

        [MenuItem("DigitalTwin/ROS2 Plan B/2 Connect ROS2")]
        public static void Step2Connect() => Run("2 Connect", p => p.ConnectRos2());

        [MenuItem("DigitalTwin/ROS2 Plan B/2b Reconnect ROS2", true)]
        private static bool PlayModeOnly2b() => Application.isPlaying;

        [MenuItem("DigitalTwin/ROS2 Plan B/2b Reconnect ROS2")]
        public static void Step2Reconnect()
        {
            Ros2ExperimentPanel panel = FindPanel();
            if (panel == null) return;
            panel.DisconnectRos2();
            Run("2b Reconnect", p => p.ConnectRos2());
        }

        [MenuItem("DigitalTwin/ROS2 Plan B/3 Create Session", true)]
        private static bool PlayModeOnly3() => Application.isPlaying;

        [MenuItem("DigitalTwin/ROS2 Plan B/3 Create Session")]
        public static void Step3Session() => Run("3 CreateSession", p => p.CreateSession());

        [MenuItem("DigitalTwin/ROS2 Plan B/4 Apply Channels", true)]
        private static bool PlayModeOnly4() => Application.isPlaying;

        [MenuItem("DigitalTwin/ROS2 Plan B/4 Apply Channels")]
        public static void Step4Channels() => Run("4 ApplyChannels", p => p.ApplyChannelsAndRates());

        [MenuItem("DigitalTwin/ROS2 Plan B/5 Start Stream", true)]
        private static bool PlayModeOnly5() => Application.isPlaying;

        [MenuItem("DigitalTwin/ROS2 Plan B/5 Start Stream")]
        public static void Step5Stream() => Run("5 StartStream", p => p.StartStream());

        [MenuItem("DigitalTwin/ROS2 Plan B/6 Send Home", true)]
        private static bool PlayModeOnly6() => Application.isPlaying;

        [MenuItem("DigitalTwin/ROS2 Plan B/6 Send Home")]
        public static void Step6Home() => Run("6 Home", p => p.SendHome());

        [MenuItem("DigitalTwin/ROS2 Plan B/7 Send Preset A", true)]
        private static bool PlayModeOnly7() => Application.isPlaying;

        [MenuItem("DigitalTwin/ROS2 Plan B/7 Send Preset A")]
        public static void Step7A() => Run("7 PresetA", p => p.SendPresetA());

        [MenuItem("DigitalTwin/ROS2 Plan B/8 Halt", true)]
        private static bool PlayModeOnly8() => Application.isPlaying;

        [MenuItem("DigitalTwin/ROS2 Plan B/8 Halt")]
        public static void Step8Halt() => Run("8 Halt", p => p.Halt());

        [MenuItem("DigitalTwin/ROS2 Plan B/Run Steps 1-5 (Link+Stream)", true)]
        private static bool PlayModeOnlyRun() => Application.isPlaying;

        [MenuItem("DigitalTwin/ROS2 Plan B/Run Steps 1-5 (Link+Stream)")]
        public static void RunSteps1To5()
        {
            Step1Resolve();
            Step2Connect();
            Step3Session();
            Step4Channels();
            Step5Stream();
        }

        [MenuItem("DigitalTwin/ROS2 Plan B/Log Panel Status", true)]
        private static bool PlayModeOnlyLog() => Application.isPlaying;

        [MenuItem("DigitalTwin/ROS2 Plan B/Log Panel Status")]
        public static void LogStatus()
        {
            Ros2ExperimentPanel panel = FindPanel();
            if (panel == null) return;

            Debug.Log(
                $"[PlanB Status] Running={panel.Ros2Running} Link={panel.Ros2LinkConnected} Data={panel.Ros2Connected} " +
                $"Session={panel.ActiveSessionId} Stream={panel.StreamEnabled} Record={panel.RecordEnabled} " +
                $"Hz={panel.Ros2FrameRateHz:F1} Dropped={panel.Ros2DroppedFrames} " +
                $"Last={panel.LastAction}|{panel.LastStatus}|{panel.LastError}");
        }

        private static void Run(string label, System.Func<Ros2ExperimentPanel, RobotCommandResult> action)
        {
            Ros2ExperimentPanel panel = FindPanel();
            if (panel == null) return;

            RobotCommandResult result = action(panel);
            Debug.Log(
                $"[PlanB {label}] ok={result.Success} status={result.Status} err={result.ErrorMessage} " +
                $"Link={panel.Ros2LinkConnected} Data={panel.Ros2Connected} Hz={panel.Ros2FrameRateHz:F1}");
        }
    }
}
