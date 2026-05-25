using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DigitalTwin
{
    [DisallowMultipleComponent]
    public sealed class DartEExperimentPanel : MonoBehaviour
    {
        [Header("绑定 / Bindings")]
        [SerializeField, Tooltip("优先复用现有论文实验会话控制器。为空时自动查找。")]
        private ExperimentSessionController sessionController;

        [SerializeField, Tooltip("复用现有 DartStudioBridge，不新建第二套 UDP/TCP。为空时自动查找。")]
        private DartStudioBridge dartBridge;

        [SerializeField, Tooltip("复用现有 TCP 命令发送器。为空时自动查找。")]
        private DartTcpCommandSender commandSender;

        [SerializeField, Tooltip("复用现有论文记录器，用于 COMMAND_ECHO 和事件标记。为空时自动查找。")]
        private TwinPaperRecorder paperRecorder;

        [Header("会话 / Session")]
        [SerializeField] private string experimentId = PaperExperimentDefaults.DefaultExperimentId;
        [SerializeField] private string sourceId = PaperExperimentDefaults.DefaultSourceId;
        [SerializeField] private int randomSeed = PaperExperimentDefaults.DefaultRandomSeed;

        [Header("数据通道 / Channels")]
        [SerializeField] private bool dartSource = true;
        [SerializeField] private bool ros2LikeSource;
        [SerializeField] private bool jointPosition = true;
        [SerializeField] private bool jointVelocity;
        [SerializeField] private bool jointEffort;
        [SerializeField] private bool toolForce = true;
        [SerializeField] private bool tcpPose;
        [SerializeField] private bool extraSignals;

        [Header("频率 / Rates")]
        [SerializeField, Tooltip("Robot -> Unity UDP 遥测发布频率。兼容 DRL 的 stream_hz / dart_hz。")]
        private float streamHz = PaperExperimentDefaults.DefaultDartHz;

        [SerializeField, Tooltip("机器人端关节状态内部轮询频率。")]
        private float jointHz = 60f;

        [SerializeField, Tooltip("机器人端六轴力/力矩内部轮询频率。")]
        private float forceHz = 30f;

        [SerializeField, Tooltip("机器人端 TCP pose 内部轮询频率。默认关闭 tcp_pose，但保留频率字段。")]
        private float tcpHz = 10f;

        [Header("Control")]
        [SerializeField] private string idleMode = "idle_stream";
        [SerializeField] private string controlMode = "mode2_ctrl";
        [SerializeField] private float[] homeTargetDeg = new float[] { -9.96f, -9.54f, 102.88f, -254.22f, 11.21f, 343.62f };
        [SerializeField] private float[] presetTargetADeg = new float[] { 10.04f, -9.54f, 102.88f, -254.22f, 11.21f, 343.62f };
        [SerializeField] private float[] presetTargetBDeg = new float[] { 607.8f, -63.7f, 1056.78f, 0.82f, 90.24f, 89.73f };
        [SerializeField, Tooltip("Point B: 基坐标 Y 方向偏移 (mm)")]
        private float yOffsetMm = 10f;

        [SerializeField, Tooltip("发送点位前先进入 mode2_ctrl；DRL 端会打断 mode1_test 并停止当前预设运动。")]
        private bool enterControlModeBeforeMove = true;

        [Header("事件 / Marker")]
        [SerializeField] private string eventNote = "Dart_E manual mark";

        [Header("运行状态 / Runtime Status")]
        [SerializeField] private string fallbackSessionId = string.Empty;
        [SerializeField] private int fallbackSegmentId;
        [SerializeField] private bool fallbackStreamEnabled;
        [SerializeField] private bool fallbackRecordEnabled;
        [SerializeField] private string lastAction = "none";
        [SerializeField] private string lastStatus = "none";
        [SerializeField] private string lastError = string.Empty;
        [SerializeField] private string lastJson = string.Empty;
        [SerializeField] private long commandEchoSeq;

        public ExperimentSessionController SessionController => sessionController;
        public DartStudioBridge DartBridge => dartBridge;
        public DartTcpCommandSender CommandSender => commandSender;
        public TwinPaperRecorder PaperRecorder => paperRecorder;
        public string ActiveExperimentId => sessionController != null ? sessionController.ExperimentId : experimentId;
        public string ActiveSessionId => sessionController != null ? sessionController.SessionId : fallbackSessionId;
        public bool StreamEnabled => sessionController != null ? sessionController.StreamEnabled : fallbackStreamEnabled;
        public bool RecordEnabled => sessionController != null ? sessionController.RecordEnabled : fallbackRecordEnabled;
        public int SegmentId => sessionController != null ? sessionController.SegmentId : fallbackSegmentId;
        public string LastAction => lastAction;
        public string LastStatus => lastStatus;
        public string LastError => lastError;
        public string LastJson => lastJson;
        public bool BridgeConnected => dartBridge != null && dartBridge.IsConnected;
        public bool TcpConnected => dartBridge != null && dartBridge.IsTcpConnected;

        private void Reset()
        {
            ResolveBindings();
            ApplyMainDefaults();
        }

        private void Awake()
        {
            ResolveBindings();
        }

        private void OnValidate()
        {
            streamHz = Mathf.Max(1f, streamHz);
            jointHz = Mathf.Max(1f, jointHz);
            forceHz = Mathf.Max(1f, forceHz);
            tcpHz = Mathf.Max(1f, tcpHz);
            EnsureTargetLength(ref homeTargetDeg);
            EnsureTargetLength(ref presetTargetADeg);
            EnsureTargetLength(ref presetTargetBDeg);
        }

        public void ApplyMainDefaults()
        {
            experimentId = PaperExperimentDefaults.DefaultExperimentId;
            sourceId = PaperExperimentDefaults.DefaultSourceId;
            randomSeed = PaperExperimentDefaults.DefaultRandomSeed;
            dartSource = true;
            ros2LikeSource = false;
            jointPosition = true;
            jointVelocity = false;
            jointEffort = false;
            toolForce = true;
            tcpPose = false;
            extraSignals = false;
            streamHz = PaperExperimentDefaults.DefaultDartHz;
            jointHz = 60f;
            forceHz = 30f;
            tcpHz = 10f;
            homeTargetDeg = new float[] { -9.96f, -9.54f, 102.88f, -254.22f, 11.21f, 343.62f };
            presetTargetADeg = new float[] { 10.04f, -9.54f, 102.88f, -254.22f, 11.21f, 343.62f };
            presetTargetBDeg = new float[] { 607.8f, -63.7f, 1056.78f, 0.82f, 90.24f, 89.73f };
            yOffsetMm = 10f;
            sessionController?.ApplyMainPaperExperimentDefaults();
        }

        public void ResolveBindings()
        {
            if (sessionController == null) sessionController = GetComponent<ExperimentSessionController>();
            if (sessionController == null) sessionController = FindObjectOfType<ExperimentSessionController>();
            if (dartBridge == null) dartBridge = GetComponent<DartStudioBridge>();
            if (dartBridge == null) dartBridge = FindObjectOfType<DartStudioBridge>();
            if (commandSender == null) commandSender = GetComponent<DartTcpCommandSender>();
            if (commandSender == null) commandSender = FindObjectOfType<DartTcpCommandSender>();
            if (commandSender != null && dartBridge != null) commandSender.Bind(dartBridge);
            if (paperRecorder == null) paperRecorder = GetComponent<TwinPaperRecorder>();
            if (paperRecorder == null) paperRecorder = FindObjectOfType<TwinPaperRecorder>();
            sessionController?.ResolveBindings();
        }

        public RobotCommandResult Connect()
        {
            ResolveBindings();
            if (sessionController != null)
            {
                return RememberDelegated("CONNECT", sessionController.Connect());
            }

            if (dartBridge == null)
            {
                return Blocked("CONNECT", "DartStudioBridge is unavailable.");
            }

            if (!dartBridge.ConnectTransport())
            {
                return Blocked("CONNECT", dartBridge.LastError);
            }

            return SendRawCommand(BuildCommand("HELLO", "\"client\":\"unity_dart_e\",\"protocol_version\":\"2.0\""), "HELLO");
        }

        public RobotCommandResult DisconnectSafe()
        {
            ResolveBindings();
            if (sessionController != null)
            {
                return RememberDelegated("DISCONNECT", sessionController.Disconnect());
            }

            if (fallbackRecordEnabled) StopPaperRecord();
            if (fallbackStreamEnabled) StopStream();
            if (!string.IsNullOrEmpty(fallbackSessionId)) CloseSession();
            dartBridge?.DisconnectTransport();
            fallbackSessionId = string.Empty;
            fallbackSegmentId = 0;
            fallbackStreamEnabled = false;
            fallbackRecordEnabled = false;
            paperRecorder?.ClearSession();
            return Remember("DISCONNECT", new RobotCommandResult(true, false, "LOCAL", string.Empty));
        }

        public RobotCommandResult ConnectAndCreate()
        {
            RobotCommandResult result = Connect();
            return result.Success ? CreateSession() : result;
        }

        public RobotCommandResult CreateSession()
        {
            ResolveBindings();
            if (sessionController != null)
            {
                return RememberDelegated("CREATE_SESSION", sessionController.CreateSession());
            }

            if (string.IsNullOrWhiteSpace(experimentId))
            {
                experimentId = "exp_manual";
            }

            fallbackSessionId = experimentId + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            fallbackSegmentId = 0;
            fallbackStreamEnabled = false;
            fallbackRecordEnabled = false;
            string extra = "\"experiment_id\":\"" + Escape(experimentId) + "\"," +
                           "\"session_id\":\"" + Escape(fallbackSessionId) + "\"," +
                           "\"mode\":\"manual\"," +
                           "\"source_id\":\"" + Escape(sourceId) + "\"," +
                           "\"random_seed\":" + randomSeed;
            RobotCommandResult result = SendRawCommand(BuildCommand("CREATE_SESSION", extra), "CREATE_SESSION");
            ConfigurePaper("SESSION_CREATED", string.Empty);
            return result;
        }

        public RobotCommandResult CloseSession()
        {
            ResolveBindings();
            if (sessionController != null)
            {
                return RememberDelegated("CLOSE_SESSION", sessionController.CloseSession());
            }

            RobotCommandResult result = SendRawCommand(BuildCommand("CLOSE_SESSION", string.Empty), "CLOSE_SESSION");
            fallbackStreamEnabled = false;
            fallbackRecordEnabled = false;
            paperRecorder?.CloseSession();
            return result;
        }

        public RobotCommandResult ApplyChannelsAndRates()
        {
            ResolveBindings();
            string extra =
                "\"sources\":{\"dart\":" + Bool(dartSource) + ",\"ros2_like\":" + Bool(ros2LikeSource) + "}," +
                "\"channels\":" + BuildChannelsJson() + "," +
                "\"dart_hz\":" + Num(streamHz) + "," +
                "\"stream_hz\":" + Num(streamHz) + "," +
                "\"joint_hz\":" + Num(jointHz) + "," +
                "\"force_hz\":" + Num(forceHz) + "," +
                "\"tcp_hz\":" + Num(tcpHz);
            return SendRawCommand(BuildCommand("SET_CHANNELS", extra), "SET_CHANNELS");
        }

        public RobotCommandResult StartStream()
        {
            ResolveBindings();
            if (sessionController != null)
            {
                return RememberDelegated("START_STREAM", sessionController.StartStream());
            }

            RobotCommandResult result = SendRawCommand(BuildCommand("START_STREAM", string.Empty), "START_STREAM");
            if (result.Success)
            {
                fallbackStreamEnabled = true;
                ConfigurePaper("STREAM_STARTED", string.Empty);
            }

            return result;
        }

        public RobotCommandResult StopStream()
        {
            ResolveBindings();
            if (sessionController != null)
            {
                return RememberDelegated("STOP_STREAM", sessionController.StopStream());
            }

            RobotCommandResult result = SendRawCommand(BuildCommand("STOP_STREAM", string.Empty), "STOP_STREAM");
            if (result.Success)
            {
                fallbackStreamEnabled = false;
                ConfigurePaper("STREAM_STOPPED", string.Empty);
            }

            return result;
        }

        public RobotCommandResult StartPaperRecord()
        {
            ResolveBindings();
            if (sessionController != null)
            {
                return RememberDelegated("START_RECORD", sessionController.StartRecord());
            }

            if (fallbackSegmentId <= 0) fallbackSegmentId = 1;
            if (!fallbackStreamEnabled) StartStream();
            RobotCommandResult result = SendRawCommand(BuildCommand("START_RECORD", "\"segment_id\":" + fallbackSegmentId), "START_RECORD");
            if (result.Success)
            {
                fallbackRecordEnabled = true;
                ConfigurePaper("RECORD_STARTED", string.Empty);
            }

            return result;
        }

        public RobotCommandResult StopPaperRecord()
        {
            ResolveBindings();
            if (sessionController != null)
            {
                return RememberDelegated("STOP_RECORD", sessionController.StopRecord());
            }

            RobotCommandResult result = SendRawCommand(BuildCommand("STOP_RECORD", string.Empty), "STOP_RECORD");
            if (result.Success)
            {
                fallbackRecordEnabled = false;
                ConfigurePaper("RECORD_STOPPED", string.Empty);
            }

            return result;
        }

        public RobotCommandResult StopRecordAndStream()
        {
            RobotCommandResult result = RecordEnabled ? StopPaperRecord() : Remember("STOP_RECORD", new RobotCommandResult(true, false, "SKIPPED", "Record already stopped."));
            if (!result.Success) return result;
            return StreamEnabled ? StopStream() : Remember("STOP_STREAM", new RobotCommandResult(true, false, "SKIPPED", "Stream already stopped."));
        }

        public RobotCommandResult EnterIdleMode()
        {
            Halt();  // 打断预设等其他运动
            return SendMode(idleMode);
        }

        public RobotCommandResult StartPresetTest()
        {
            if (!StreamEnabled)
            {
                RobotCommandResult streamResult = StartStream();
                if (!streamResult.Success)
                {
                    return streamResult;
                }
            }

            string json = BuildCommand("PRESET", "\"action\":\"start\"");
            return SendRawCommand(json, "PRESET:start");
        }

        public RobotCommandResult EnterControlMode()
        {
            Halt();  // 打断预设等其他运动
            return SendMode(controlMode);
        }

        public RobotCommandResult SendHome()
        {
            string json = BuildCommand("HOME", string.Empty);
            return SendRawCommand(json, "HOME");
        }

        public RobotCommandResult SendPresetA()
        {
            string json = BuildCommand("MODE_A", string.Empty);
            return SendRawCommand(json, "MODE_A");
        }

        public RobotCommandResult SendPresetB()
        {
            string extra = "\"target\":[0," + Num(yOffsetMm) + ",0,0,0,0]";
            string json = BuildCommand("MOVE_TCP", extra);
            return SendRawCommand(json, "MOVE_TCP", "REL Y+" + Num(yOffsetMm) + "mm");
        }

        public RobotCommandResult Halt()
        {
            string json = BuildCommand("HALT", string.Empty);
            return SendRawCommand(json, "HALT");
        }

        public RobotCommandResult MarkEvent()
        {
            string extra = "\"event_note\":\"" + Escape(eventNote) + "\"";
            RobotCommandResult result = SendRawCommand(BuildCommand("MARK_EVENT", extra), "MARK_EVENT");
            paperRecorder?.EnqueueEvent("DART_E_MARK", eventNote);
            return result;
        }

        private RobotCommandResult SendControlMove(float[] targetDeg, string action)
        {
            if (enterControlModeBeforeMove)
            {
                RobotCommandResult modeResult = EnterControlMode();
                if (!modeResult.Success)
                {
                    return modeResult;
                }
            }

            ResolveBindings();
            string json = BuildCommand("MOVE_JOINT", BuildMoveJointExtra(targetDeg));
            return SendRawCommand(json, action, FormatTarget(targetDeg));
        }

        private RobotCommandResult SendMode(string modeName)
        {
            string json = BuildCommand("SET_MODE", "\"mode\":\"" + Escape(modeName) + "\"");
            return SendRawCommand(json, "SET_MODE:" + modeName);
        }

        private RobotCommandResult SendRawCommand(string json, string action, string expectedJointAngles = "")
        {
            ResolveBindings();
            long sendWallMs = SystemClock.UtcUnixMs();
            long sendNs = SystemClock.NowNs();
            RobotCommandResult result;
            if (commandSender != null)
            {
                result = commandSender.SendRaw(json);
            }
            else if (dartBridge != null)
            {
                result = dartBridge.SendRawCommand(json);
            }
            else
            {
                result = new RobotCommandResult(false, false, "ERROR", "No Dart command sender or bridge is available.");
            }

            LogPaperCommand(json, action, result, sendWallMs, sendNs, expectedJointAngles);
            return Remember(action, result, expectedJointAngles, json);
        }

        private RobotCommandResult Blocked(string action, string reason)
        {
            return Remember(action, new RobotCommandResult(false, false, "BLOCKED", reason));
        }

        private RobotCommandResult Remember(string action, RobotCommandResult result, string expectedJointAngles = "", string json = "")
        {
            lastAction = string.IsNullOrEmpty(action) ? "none" : action;
            lastStatus = result.Status ?? string.Empty;
            lastError = result.ErrorMessage ?? string.Empty;
            lastJson = json ?? string.Empty;
            LogCommandEcho(lastAction, result, expectedJointAngles);
            return result;
        }

        private RobotCommandResult RememberDelegated(string action, RobotCommandResult result)
        {
            lastAction = string.IsNullOrEmpty(action) ? "none" : action;
            lastStatus = result.Status ?? string.Empty;
            lastError = result.ErrorMessage ?? string.Empty;
            lastJson = string.Empty;
            return result;
        }

        private void LogCommandEcho(string action, RobotCommandResult result, string expectedJointAngles)
        {
            if (paperRecorder == null || string.IsNullOrEmpty(ActiveSessionId))
            {
                return;
            }

            commandEchoSeq++;
            string notes = "{" +
                           "\"cmd_seq\":" + commandEchoSeq + "," +
                           "\"source\":\"Dart_E\"," +
                           "\"action\":\"" + Escape(action) + "\"," +
                           "\"send_wall_ms\":" + SystemClock.UtcUnixMs() + "," +
                           "\"status\":\"" + Escape(result.Status) + "\"," +
                           "\"dry_run\":" + Bool(result.DryRun) + "," +
                           "\"success\":" + Bool(result.Success) + "," +
                           "\"expected_joint_angles\":\"" + Escape(expectedJointAngles) + "\"," +
                           "\"error\":\"" + Escape(result.ErrorMessage) + "\"" +
                           "}";
            paperRecorder.EnqueueEvent(PaperExperimentDefaults.CommandEchoEvent, notes);
        }

        private void LogPaperCommand(string json, string action, RobotCommandResult result, long sendWallMs, long sendNs, string expectedJointAngles)
        {
            if (paperRecorder == null || string.IsNullOrEmpty(ActiveSessionId))
            {
                return;
            }

            paperRecorder.RecordCommand(
                action,
                "DartStudio",
                ExtractStringField(json, "id"),
                json,
                result,
                sendWallMs,
                sendNs,
                SystemClock.UtcUnixMs(),
                SystemClock.NowNs(),
                expectedJointAngles);
        }

        private static string ExtractStringField(string json, string fieldName)
        {
            string marker = "\"" + fieldName + "\":\"";
            int start = string.IsNullOrEmpty(json) ? -1 : json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return string.Empty;
            start += marker.Length;
            int end = json.IndexOf('"', start);
            return end > start ? json.Substring(start, end - start) : string.Empty;
        }

        private void ConfigurePaper(string eventType, string notes)
        {
            if (paperRecorder == null || string.IsNullOrEmpty(ActiveSessionId))
            {
                return;
            }

            paperRecorder.ConfigureSession(ActiveExperimentId, ActiveSessionId, 0, SegmentId, StreamEnabled, RecordEnabled, eventType, notes);
        }

        private string BuildCommand(string command, string extra)
        {
            StringBuilder builder = new StringBuilder(256);
            builder.Append("{\"id\":\"")
                .Append(Escape("unity-darte-" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)))
                .Append("\",\"cmd\":\"")
                .Append(Escape(command))
                .Append("\"");

            if (!string.IsNullOrEmpty(ActiveSessionId))
            {
                builder.Append(",\"session_id\":\"").Append(Escape(ActiveSessionId)).Append("\"");
            }

            if (!string.IsNullOrEmpty(ActiveExperimentId))
            {
                builder.Append(",\"experiment_id\":\"").Append(Escape(ActiveExperimentId)).Append("\"");
            }

            if (!string.IsNullOrEmpty(extra))
            {
                builder.Append(',').Append(extra);
            }

            builder.Append('}');
            return builder.ToString();
        }

        private string BuildChannelsJson()
        {
            return "{" +
                   "\"joint_position\":" + Bool(jointPosition) + "," +
                   "\"joint_velocity\":" + Bool(jointVelocity) + "," +
                   "\"joint_effort\":" + Bool(jointEffort) + "," +
                   "\"tool_force\":" + Bool(toolForce) + "," +
                   "\"tcp_pose\":" + Bool(tcpPose) + "," +
                   "\"extra_signals\":" + Bool(extraSignals) +
                   "}";
        }

        private string BuildMoveJointExtra(float[] targetDeg)
        {
            StringBuilder builder = new StringBuilder(128);
            builder.Append("\"target\":[");
            for (int i = 0; i < targetDeg.Length; i++)
            {
                if (i > 0) builder.Append(',');
                builder.Append(targetDeg[i].ToString("0.######", CultureInfo.InvariantCulture));
            }

            builder.Append("]");
            return builder.ToString();
        }

        private static void EnsureTargetLength(ref float[] target)
        {
            if (target != null && target.Length == 6)
            {
                return;
            }

            float[] fixedTarget = new float[6];
            if (target != null)
            {
                Array.Copy(target, fixedTarget, Math.Min(target.Length, fixedTarget.Length));
            }

            target = fixedTarget;
        }

        private static string FormatTarget(float[] target)
        {
            if (target == null || target.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(96);
            for (int i = 0; i < target.Length; i++)
            {
                if (i > 0) builder.Append(';');
                builder.Append(target[i].ToString("0.###", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string Num(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            return DartTcpCommandBuilder.EscapeJson(value ?? string.Empty);
        }
    }
}
