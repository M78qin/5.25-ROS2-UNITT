using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DigitalTwin
{
    public enum TwinExperimentSessionState
    {
        Disconnected = 0,
        Connected = 1,
        SessionCreated = 2,
        Streaming = 3,
        Recording = 4,
        PausedRecording = 5,
        Stopped = 6
    }

    [DisallowMultipleComponent]
    public sealed class ExperimentSessionController : MonoBehaviour
    {
        [Header("Bindings")]
        [SerializeField] private DartTcpCommandSender commandSender;
        [SerializeField] private DartStudioBridge dartBridge;
        [SerializeField] private TwinRecorder recorder;
        [SerializeField] private TwinPaperRecorder paperRecorder;

        [Header("Legacy Runtime Recorder")]
        [SerializeField] private bool enableLegacyFrameRecorder;

        [Header("Session")]
        [SerializeField] private string experimentId = PaperExperimentDefaults.DefaultExperimentId;
        [SerializeField] private string mode = PaperExperimentDefaults.DefaultMode;
        [SerializeField] private string sourceId = PaperExperimentDefaults.DefaultSourceId;
        [SerializeField] private int randomSeed = PaperExperimentDefaults.DefaultRandomSeed;

        [Header("Sources / Frequency")]
        [SerializeField] private bool dartSource = PaperExperimentDefaults.DartSource;
        [SerializeField] private bool ros2LikeSource = PaperExperimentDefaults.Ros2LikeSource;
        [SerializeField, Tooltip("下发给 Python/真实机器人数据源的请求频率。只有点击 Apply Channels + Hz 时才会改变对端频率，不代表 Unity 接收频率。")]
        private float dartHz = PaperExperimentDefaults.DefaultDartHz;
        [SerializeField] private float ros2Hz = PaperExperimentDefaults.DefaultRos2Hz;

        [Header("Channels")]
        [SerializeField] private bool jointPosition = PaperExperimentDefaults.JointPosition;
        [SerializeField] private bool jointVelocity = PaperExperimentDefaults.JointVelocity;
        [SerializeField] private bool jointEffort = PaperExperimentDefaults.JointEffort;
        [SerializeField] private bool toolForce = PaperExperimentDefaults.ToolForce;
        [SerializeField] private bool tcpPose = PaperExperimentDefaults.TcpPose;
        [SerializeField] private bool extraSignals = PaperExperimentDefaults.ExtraSignals;

        [Header("Markers")]
        [SerializeField] private string phaseNote = string.Empty;
        [SerializeField] private string eventNote = string.Empty;

        [Header("Motion Commands")]
        [SerializeField] private string idleMode = "idle_stream";
        [SerializeField] private string presetMotionMode = "mode1_test";
        [SerializeField] private string controlMode = "mode2_ctrl";

        [Header("Control Mode Preset")]
        [SerializeField] private float[] controlModePresetTarget = new float[] { 90f, 30f, 90f, 0f, 0f, 90f };

        [SerializeField, Tooltip("Ctrl + Move 前先进入 mode2_ctrl；DRL 端负责打断 mode1_test 并停止当前预设运动。")]
        private bool enterControlModeBeforeMove = true;

        [Header("Network Impairment")]
        [SerializeField] private float delayMs;
        [SerializeField] private float jitterMs;
        [SerializeField, Range(0f, 1f)] private float dropRate;
        [SerializeField, Range(0f, 1f)] private float duplicateRate;
        [SerializeField, Range(0f, 1f)] private float reorderRate;

        [Header("Runtime Status")]
        [SerializeField] private TwinExperimentSessionState state = TwinExperimentSessionState.Disconnected;
        [SerializeField] private string sessionId = string.Empty;
        [SerializeField] private int phaseId;
        [SerializeField] private int segmentId;
        [SerializeField] private bool streamEnabled;
        [SerializeField] private bool recordEnabled;
        [SerializeField] private string lastCommand = string.Empty;
        [SerializeField] private string lastStatus = string.Empty;
        [SerializeField] private string lastError = string.Empty;
        [SerializeField] private long lastCommandSeq;

        public TwinExperimentSessionState State => state;
        public string ExperimentId => experimentId;
        public string SessionId => sessionId;
        public int PhaseId => phaseId;
        public int SegmentId => segmentId;
        public bool StreamEnabled => streamEnabled;
        public bool RecordEnabled => recordEnabled;
        public string LastStatus => lastStatus;
        public string LastError => lastError;

        public void ApplyMainPaperExperimentDefaults()
        {
            experimentId = PaperExperimentDefaults.DefaultExperimentId;
            mode = PaperExperimentDefaults.DefaultMode;
            sourceId = PaperExperimentDefaults.DefaultSourceId;
            randomSeed = PaperExperimentDefaults.DefaultRandomSeed;
            dartSource = PaperExperimentDefaults.DartSource;
            ros2LikeSource = PaperExperimentDefaults.Ros2LikeSource;
            dartHz = PaperExperimentDefaults.DefaultDartHz;
            ros2Hz = PaperExperimentDefaults.DefaultRos2Hz;
            jointPosition = PaperExperimentDefaults.JointPosition;
            jointVelocity = PaperExperimentDefaults.JointVelocity;
            jointEffort = PaperExperimentDefaults.JointEffort;
            toolForce = PaperExperimentDefaults.ToolForce;
            tcpPose = PaperExperimentDefaults.TcpPose;
            extraSignals = PaperExperimentDefaults.ExtraSignals;
            enableLegacyFrameRecorder = false;
        }

        private void Reset()
        {
            ResolveBindings();
        }

        private void Awake()
        {
            ResolveBindings();
        }

        private void OnValidate()
        {
            dartHz = Mathf.Max(1f, dartHz);
            ros2Hz = Mathf.Max(1f, ros2Hz);
            delayMs = Mathf.Max(0f, delayMs);
            jitterMs = Mathf.Max(0f, jitterMs);
        }

        public void ResolveBindings()
        {
            if (commandSender == null) commandSender = GetComponent<DartTcpCommandSender>();
            if (commandSender == null) commandSender = FindObjectOfType<DartTcpCommandSender>();
            if (dartBridge == null) dartBridge = GetComponent<DartStudioBridge>();
            if (dartBridge == null) dartBridge = FindObjectOfType<DartStudioBridge>();
            if (dartBridge == null && commandSender != null) dartBridge = commandSender.Bridge;
            TwinRecorder runtimeRecorder = ResolveRuntimeRecorder();
            if (runtimeRecorder != null) recorder = runtimeRecorder;
            if (recorder == null) recorder = GetComponent<TwinRecorder>();
            if (recorder == null) recorder = FindObjectOfType<TwinRecorder>();
            TwinPaperRecorder runtimePaperRecorder = ResolveRuntimePaperRecorder();
            if (runtimePaperRecorder != null) paperRecorder = runtimePaperRecorder;
            if (paperRecorder == null) paperRecorder = GetComponent<TwinPaperRecorder>();
            if (paperRecorder == null) paperRecorder = FindObjectOfType<TwinPaperRecorder>();
        }

        private TwinRecorder ResolveRuntimeRecorder()
        {
            DigitalTwinRuntime[] runtimes = FindObjectsOfType<DigitalTwinRuntime>();
            DigitalTwinRuntime fallback = null;
            for (int i = 0; i < runtimes.Length; i++)
            {
                DigitalTwinRuntime runtime = runtimes[i];
                if (runtime == null || !runtime.isActiveAndEnabled || runtime.Recorder == null)
                {
                    continue;
                }

                if (dartBridge != null && runtime.DartBridge == dartBridge)
                {
                    return runtime.Recorder;
                }

                if (fallback == null)
                {
                    fallback = runtime;
                }
            }

            return fallback == null ? null : fallback.Recorder;
        }

        private TwinPaperRecorder ResolveRuntimePaperRecorder()
        {
            DigitalTwinRuntime[] runtimes = FindObjectsOfType<DigitalTwinRuntime>();
            DigitalTwinRuntime fallback = null;
            for (int i = 0; i < runtimes.Length; i++)
            {
                DigitalTwinRuntime runtime = runtimes[i];
                if (runtime == null || !runtime.isActiveAndEnabled || runtime.PaperRecorder == null)
                {
                    continue;
                }

                if (dartBridge != null && runtime.DartBridge == dartBridge)
                {
                    return runtime.PaperRecorder;
                }

                if (fallback == null)
                {
                    fallback = runtime;
                }
            }

            return fallback == null ? null : fallback.PaperRecorder;
        }

        public RobotCommandResult Connect()
        {
            ResolveBindings();
            if (state != TwinExperimentSessionState.Disconnected)
            {
                return Remember("CONNECT", new RobotCommandResult(true, false, "ALREADY_CONNECTED", string.Empty));
            }

            if (dartBridge == null)
            {
                return Blocked("CONNECT", "DartStudioBridge is unavailable.");
            }

            if (!dartBridge.ConnectTransport())
            {
                return Blocked("CONNECT", dartBridge.LastError);
            }

            RobotCommandResult result = SendCommand(BuildCommand("HELLO", "\"client\":\"unity\",\"protocol_version\":\"2.0\""));
            if (result.Success)
            {
                state = TwinExperimentSessionState.Connected;
                SendCommand(BuildCommand("GET_STATUS", string.Empty));
            }
            else
            {
                dartBridge.DisconnectTransport();
                state = TwinExperimentSessionState.Disconnected;
            }

            return result;
        }

        public RobotCommandResult Disconnect()
        {
            if (recordEnabled)
            {
                StopRecord();
            }

            if (streamEnabled)
            {
                StopStream();
            }

            if (!string.IsNullOrEmpty(sessionId))
            {
                CloseSession();
            }

            dartBridge?.DisconnectTransport();
            state = TwinExperimentSessionState.Disconnected;
            sessionId = string.Empty;
            phaseId = 0;
            segmentId = 0;
            streamEnabled = false;
            recordEnabled = false;
            ClearLegacyRecorderContext();
            paperRecorder?.ClearSession();
            return Remember("DISCONNECT", new RobotCommandResult(true, false, "LOCAL", string.Empty));
        }

        public RobotCommandResult GetStatus()
        {
            if (state == TwinExperimentSessionState.Disconnected)
            {
                return Blocked("GET_STATUS", "Connect first.");
            }

            return SendCommand(BuildCommand("GET_STATUS", string.Empty));
        }

        public RobotCommandResult CreateSession()
        {
            if (state == TwinExperimentSessionState.Disconnected)
            {
                return Blocked("CREATE_SESSION", "Connect first.");
            }

            if (string.IsNullOrWhiteSpace(experimentId))
            {
                experimentId = "exp_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            }

            sessionId = experimentId + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            phaseId = 0;
            segmentId = 0;
            streamEnabled = false;
            recordEnabled = false;
            string extra =
                "\"experiment_id\":\"" + EscapeJson(experimentId) + "\"," +
                "\"session_id\":\"" + EscapeJson(sessionId) + "\"," +
                "\"mode\":\"" + EscapeJson(mode) + "\"," +
                "\"source_id\":\"" + EscapeJson(sourceId) + "\"," +
                "\"random_seed\":" + randomSeed;

            RobotCommandResult result = SendCommand(BuildCommand("CREATE_SESSION", extra));
            if (result.Success)
            {
                state = TwinExperimentSessionState.SessionCreated;
                ConfigureRecorderContext();
            }

            return result;
        }

        public RobotCommandResult CloseSession()
        {
            if (state == TwinExperimentSessionState.Disconnected)
            {
                return Blocked("CLOSE_SESSION", "Connect first.");
            }

            RobotCommandResult result = SendCommand(BuildCommand("CLOSE_SESSION", string.Empty));
            streamEnabled = false;
            recordEnabled = false;
            state = TwinExperimentSessionState.Stopped;
            ClearLegacyRecorderContext();
            paperRecorder?.CloseSession();
            return result;
        }

        public RobotCommandResult ApplyChannels()
        {
            if (state == TwinExperimentSessionState.Disconnected)
            {
                return Blocked("SET_CHANNELS", "Connect first.");
            }

            string extra =
                "\"sources\":{\"dart\":" + Bool(dartSource) + ",\"ros2_like\":" + Bool(ros2LikeSource) + "}," +
                "\"channels\":" + BuildChannelsJson() + "," +
                "\"dart_hz\":" + Num(dartHz) + "," +
                "\"ros2_hz\":" + Num(ros2Hz);
            return SendCommand(BuildCommand("SET_CHANNELS", extra));
        }

        public RobotCommandResult GetChannels()
        {
            if (state == TwinExperimentSessionState.Disconnected)
            {
                return Blocked("GET_CHANNELS", "Connect first.");
            }

            return SendCommand(BuildCommand("GET_CHANNELS", string.Empty));
        }

        public RobotCommandResult StartStream()
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return Blocked("START_STREAM", "CreateSession first.");
            }

            RobotCommandResult result = SendCommand(BuildCommand("START_STREAM", string.Empty));
            if (result.Success)
            {
                streamEnabled = true;
                state = TwinExperimentSessionState.Streaming;
                SetLegacyRecorderStreamEnabled(true);
                paperRecorder?.SetSessionStreamEnabled(true);
                ConfigureRecorderContext();
            }

            return result;
        }

        public RobotCommandResult PauseStream()
        {
            if (!streamEnabled)
            {
                return Blocked("PAUSE_STREAM", "Stream is not running.");
            }

            RobotCommandResult result = SendCommand(BuildCommand("PAUSE_STREAM", string.Empty));
            if (result.Success)
            {
                streamEnabled = false;
                SetLegacyRecorderStreamEnabled(false);
                paperRecorder?.SetSessionStreamEnabled(false);
            }

            return result;
        }

        public RobotCommandResult ResumeStream()
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return Blocked("RESUME_STREAM", "CreateSession first.");
            }

            RobotCommandResult result = SendCommand(BuildCommand("RESUME_STREAM", string.Empty));
            if (result.Success)
            {
                streamEnabled = true;
                state = recordEnabled ? TwinExperimentSessionState.Recording : TwinExperimentSessionState.Streaming;
                SetLegacyRecorderStreamEnabled(true);
                paperRecorder?.SetSessionStreamEnabled(true);
            }

            return result;
        }

        public RobotCommandResult StopStream()
        {
            if (!streamEnabled)
            {
                return Blocked("STOP_STREAM", "Stream is not running.");
            }

            RobotCommandResult result = SendCommand(BuildCommand("STOP_STREAM", string.Empty));
            if (result.Success)
            {
                streamEnabled = false;
                state = TwinExperimentSessionState.SessionCreated;
                SetLegacyRecorderStreamEnabled(false);
                paperRecorder?.SetSessionStreamEnabled(false);
            }

            return result;
        }

        public RobotCommandResult StartRecord()
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return Blocked("START_RECORD", "CreateSession first.");
            }

            if (segmentId <= 0) segmentId = 1;
            if (!streamEnabled) StartStream();
            RobotCommandResult result = SendCommand(BuildCommand("START_RECORD", "\"segment_id\":" + segmentId));
            if (result.Success)
            {
                recordEnabled = true;
                state = TwinExperimentSessionState.Recording;
                ForceBeginLegacyRecorderSession();
                paperRecorder?.SetSessionRecordEnabled(true, segmentId);
                ConfigureRecorderContext();
            }

            return result;
        }

        public RobotCommandResult PauseRecord()
        {
            if (!recordEnabled)
            {
                return Blocked("PAUSE_RECORD", "Record is not running.");
            }

            RobotCommandResult result = SendCommand(BuildCommand("PAUSE_RECORD", string.Empty));
            if (result.Success)
            {
                recordEnabled = false;
                state = TwinExperimentSessionState.PausedRecording;
                SetLegacyRecorderRecordEnabled(false, segmentId);
                paperRecorder?.SetSessionRecordEnabled(false, segmentId);
            }

            return result;
        }

        public RobotCommandResult ResumeRecord()
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return Blocked("RESUME_RECORD", "CreateSession first.");
            }

            segmentId = Math.Max(1, segmentId + 1);
            RobotCommandResult result = SendCommand(BuildCommand("RESUME_RECORD", "\"segment_id\":" + segmentId));
            if (result.Success)
            {
                recordEnabled = true;
                state = TwinExperimentSessionState.Recording;
                paperRecorder?.SetSessionRecordEnabled(true, segmentId);
                ConfigureRecorderContext();
            }

            return result;
        }

        public RobotCommandResult StopRecord()
        {
            if (!recordEnabled)
            {
                return Blocked("STOP_RECORD", "Record is not running.");
            }

            RobotCommandResult result = SendCommand(BuildCommand("STOP_RECORD", string.Empty));
            if (result.Success)
            {
                recordEnabled = false;
                state = streamEnabled ? TwinExperimentSessionState.Streaming : TwinExperimentSessionState.SessionCreated;
                SetLegacyRecorderRecordEnabled(false, segmentId);
                paperRecorder?.SetSessionRecordEnabled(false, segmentId);
            }

            return result;
        }

        public RobotCommandResult NewPhase()
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return Blocked("NEW_PHASE", "CreateSession first.");
            }

            phaseId++;
            string extra = "\"phase_id\":" + phaseId + ",\"phase_note\":\"" + EscapeJson(phaseNote) + "\"";
            RobotCommandResult result = SendCommand(BuildCommand("NEW_PHASE", extra));
            ConfigureRecorderContext("PHASE", phaseNote);
            return result;
        }

        public RobotCommandResult MarkEvent()
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return Blocked("MARK_EVENT", "CreateSession first.");
            }

            string extra = "\"phase_id\":" + phaseId + ",\"segment_id\":" + segmentId + ",\"event_note\":\"" + EscapeJson(eventNote) + "\"";
            RobotCommandResult result = SendCommand(BuildCommand("MARK_EVENT", extra));
            ConfigureRecorderContext("EVENT", eventNote);
            return result;
        }

        public RobotCommandResult ApplyNetworkImpairment()
        {
            string extra =
                "\"delay_ms\":" + Num(delayMs) + "," +
                "\"jitter_ms\":" + Num(jitterMs) + "," +
                "\"drop_rate\":" + Num(dropRate) + "," +
                "\"duplicate_rate\":" + Num(duplicateRate) + "," +
                "\"reorder_rate\":" + Num(reorderRate);
            return SendCommand(BuildCommand("SET_NETWORK_IMPAIRMENT", extra));
        }

        public RobotCommandResult ResetNetworkImpairment()
        {
            return SendCommand(BuildCommand("RESET_NETWORK_IMPAIRMENT", string.Empty));
        }

        public RobotCommandResult EnterIdleMode()
        {
            return SendModeCommand(idleMode);
        }

        public RobotCommandResult StartPresetMotion()
        {
            if (!streamEnabled)
            {
                RobotCommandResult streamResult = StartStream();
                if (!streamResult.Success) return streamResult;
            }

            return SendModeCommand(presetMotionMode);
        }

        public RobotCommandResult EnterControlMode()
        {
            return SendModeCommand(controlMode);
        }

        public RobotCommandResult EnterControlModeWithPreset()
        {
            if (enterControlModeBeforeMove)
            {
                RobotCommandResult modeResult = SendModeCommand(controlMode);
                if (!modeResult.Success) return modeResult;
            }

            if (controlModePresetTarget == null || controlModePresetTarget.Length == 0)
                return Remember("MOVE_JOINT", new RobotCommandResult(false, false, "BLOCKED", "Preset target empty."));

            return SendCommand(BuildCommand("MOVE_JOINT", BuildMoveJointExtra(controlModePresetTarget)));
        }

        public RobotCommandResult HaltMotion()
        {
            if (state == TwinExperimentSessionState.Disconnected)
            {
                return Blocked("HALT", "Connect first.");
            }

            return SendCommand(BuildCommand("HALT", string.Empty));
        }

        private RobotCommandResult SendCommand(string json)
        {
            ResolveBindings();
            long sendWallMs = SystemClock.UtcUnixMs();
            long sendNs = SystemClock.NowNs();
            if (commandSender != null)
            {
                RobotCommandResult result = commandSender.SendRaw(json);
                LogPaperCommand(json, "DartStudio", result, sendWallMs, sendNs);
                return Remember(ExtractCommandName(json), result);
            }

            if (dartBridge != null)
            {
                RobotCommandResult result = dartBridge.SendRawCommand(json);
                LogPaperCommand(json, "DartStudio", result, sendWallMs, sendNs);
                return Remember(ExtractCommandName(json), result);
            }

            RobotCommandResult missing = new RobotCommandResult(false, false, "ERROR", "No Dart command sender or bridge is available.");
            LogPaperCommand(json, "DartStudio", missing, sendWallMs, sendNs);
            return Remember(ExtractCommandName(json), missing);
        }

        private RobotCommandResult Blocked(string action, string reason)
        {
            return Remember(action, new RobotCommandResult(false, false, "BLOCKED", string.IsNullOrEmpty(reason) ? "Command blocked by session state." : reason));
        }

        private void ConfigureRecorderContext(string eventType = "", string notes = "")
        {
            ConfigureLegacyRecorderContext(eventType, notes);
            paperRecorder?.ConfigureSession(experimentId, sessionId, phaseId, segmentId, streamEnabled, recordEnabled, eventType, notes);
        }

        private void LogPaperCommand(string json, string route, RobotCommandResult result, long sendWallMs, long sendNs)
        {
            if (paperRecorder == null || string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            paperRecorder.RecordCommand(
                ExtractCommandName(json),
                route,
                ExtractStringField(json, "id"),
                json,
                result,
                sendWallMs,
                sendNs,
                SystemClock.UtcUnixMs(),
                SystemClock.NowNs(),
                ExtractTargetSummary(json));
        }

        private void ClearLegacyRecorderContext()
        {
            if (enableLegacyFrameRecorder)
            {
                recorder?.ClearExperimentSession();
            }
        }

        private void SetLegacyRecorderStreamEnabled(bool enabled)
        {
            if (enableLegacyFrameRecorder)
            {
                recorder?.SetSessionStreamEnabled(enabled);
            }
        }

        private void SetLegacyRecorderRecordEnabled(bool enabled, int targetSegmentId)
        {
            if (enableLegacyFrameRecorder)
            {
                recorder?.SetSessionRecordEnabled(enabled, targetSegmentId);
            }
        }

        private void ForceBeginLegacyRecorderSession()
        {
            if (enableLegacyFrameRecorder)
            {
                recorder?.ForceBeginRecordingSession(sessionId);
            }
        }

        private void ConfigureLegacyRecorderContext(string eventType = "", string notes = "")
        {
            if (enableLegacyFrameRecorder)
            {
                recorder?.ConfigureExperimentSession(experimentId, sessionId, phaseId, segmentId, streamEnabled, recordEnabled, eventType, notes);
            }
        }

        private RobotCommandResult Remember(string action, RobotCommandResult result)
        {
            lastCommand = action ?? string.Empty;
            lastStatus = result.Status ?? string.Empty;
            lastError = result.ErrorMessage ?? string.Empty;
            LogCommandEcho(lastCommand, result, string.Empty);
            return result;
        }

        private void LogCommandEcho(string action, RobotCommandResult result, string expectedJointAngles)
        {
            if (paperRecorder == null || string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            lastCommandSeq++;
            string notes = "{" +
                           "\"cmd_seq\":" + lastCommandSeq + "," +
                           "\"action\":\"" + EscapeJson(action) + "\"," +
                           "\"send_wall_ms\":" + SystemClock.UtcUnixMs() + "," +
                           "\"status\":\"" + EscapeJson(result.Status) + "\"," +
                           "\"dry_run\":" + Bool(result.DryRun) + "," +
                           "\"success\":" + Bool(result.Success) + "," +
                           "\"expected_joint_angles\":\"" + EscapeJson(expectedJointAngles) + "\"," +
                           "\"error\":\"" + EscapeJson(result.ErrorMessage) + "\"" +
                           "}";
            paperRecorder.EnqueueEvent(PaperExperimentDefaults.CommandEchoEvent, notes);
        }

        private string BuildCommand(string command, string extra)
        {
            StringBuilder builder = new StringBuilder(256);
            builder.Append("{\"id\":\"")
                .Append(EscapeJson("unity-" + DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture)))
                .Append("\",\"cmd\":\"")
                .Append(EscapeJson(command))
                .Append("\"");
            if (!string.IsNullOrEmpty(sessionId))
            {
                builder.Append(",\"session_id\":\"").Append(EscapeJson(sessionId)).Append("\"");
            }

            if (!string.IsNullOrEmpty(experimentId))
            {
                builder.Append(",\"experiment_id\":\"").Append(EscapeJson(experimentId)).Append("\"");
            }

            if (!string.IsNullOrEmpty(extra))
            {
                builder.Append(',').Append(extra);
            }

            builder.Append('}');
            return builder.ToString();
        }

        private RobotCommandResult SendModeCommand(string modeName)
        {
            if (state == TwinExperimentSessionState.Disconnected)
            {
                return Blocked("SET_MODE", "Connect first.");
            }

            return SendCommand(BuildCommand("SET_MODE", "\"mode\":\"" + EscapeJson(modeName) + "\""));
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

        private static string ExtractCommandName(string json)
        {
            const string marker = "\"cmd\":\"";
            int start = string.IsNullOrEmpty(json) ? -1 : json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return "RAW";
            start += marker.Length;
            int end = json.IndexOf('"', start);
            return end > start ? json.Substring(start, end - start) : "RAW";
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

        private static string ExtractTargetSummary(string json)
        {
            const string marker = "\"target\":[";
            int start = string.IsNullOrEmpty(json) ? -1 : json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) return string.Empty;
            start += marker.Length;
            int end = json.IndexOf(']', start);
            return end > start ? json.Substring(start, end - start) : string.Empty;
        }

        private static string Bool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string Num(float value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string EscapeJson(string value)
        {
            return DartTcpCommandBuilder.EscapeJson(value ?? string.Empty);
        }
    }
}
