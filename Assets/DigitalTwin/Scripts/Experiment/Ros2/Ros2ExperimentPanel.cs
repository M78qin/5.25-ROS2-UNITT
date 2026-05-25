using System;
using System.Globalization;
using UnityEngine;

namespace DigitalTwin
{
    [DisallowMultipleComponent]
    public sealed class Ros2ExperimentPanel : MonoBehaviour
    {
        [Header("Bindings")]
        [SerializeField] private DigitalTwinRuntime runtime;
        [SerializeField] private Ros2Bridge ros2Bridge;
        [SerializeField] private TwinCommandController commandController;
        [SerializeField] private TwinPaperRecorder paperRecorder;

        [Header("ROS2 Topics")]
        [SerializeField] private string jointStateTopic = "/dsr01/joint_states";
        [SerializeField] private string moveJointTopic = "/dt/cmd/move_joint";
        [SerializeField] private string haltTopic = "/dt/cmd/halt";
        [SerializeField] private string setModeTopic = "/dt/cmd/set_mode";
        [SerializeField] private string modeStatusTopic = "/dt/status/mode";
        [SerializeField] private string motionStatusTopic = "/dt/status/motion";
        [SerializeField] private bool enableWrenchTopic;
        [SerializeField] private string wrenchTopic = "/wrench";
        [SerializeField] private bool enableTcpPoseTopic;
        [SerializeField] private string tcpPoseTopic = "/tcp_pose";

        [Header("Paper Session")]
        [SerializeField] private string experimentId = "ros2_moveit_unity_20260525";
        [SerializeField] private string sourceId = "unity_ros2_panel";
        [SerializeField] private int phaseId;
        [SerializeField] private int segmentId = 1;
        [SerializeField] private bool streamEnabled;
        [SerializeField] private bool recordEnabled;

        [Header("Channels / Rates")]
        [SerializeField, Tooltip("ROS2 bridge 主 CSV 目标频率；真实上限由 /joint_states 和 rt_timer_ms 决定。")]
        private float ros2MainHz = 200f;
        [SerializeField] private bool jointPosition = true;
        [SerializeField] private bool jointVelocity = true;
        [SerializeField] private bool jointEffort = true;
        [SerializeField] private bool toolForce = true;
        [SerializeField] private bool tcpPose = true;
        [SerializeField] private bool extraSignals;

        [Header("Preset Joint Commands")]
        [SerializeField] private float speedPercent = 10f;
        [SerializeField, Tooltip("开启后按钮直接发布到 Ros2Bridge 的 /dt/cmd/move_joint；安全 dry-run 由 ROS2 adapter 控制。关闭则走 TwinCommandController 本地安全门。")]
        private bool publishDirectlyToRos2 = true;
        [SerializeField] private float[] homeTargetDeg = new float[] { 0f, 0f, 0f, 0f, 0f, 0f };
        [SerializeField] private float[] presetTargetADeg = new float[] { 5f, 0f, 0f, 0f, 0f, 0f };
        [SerializeField] private float[] presetTargetBDeg = new float[] { -5f, 0f, 0f, 0f, 0f, 0f };

        [Header("Runtime Status")]
        [SerializeField] private string sessionId = string.Empty;
        [SerializeField] private string lastAction = "none";
        [SerializeField] private string lastStatus = "none";
        [SerializeField] private string lastError = string.Empty;

        public bool Ros2Running => ros2Bridge != null && ros2Bridge.IsRunning;
        public bool Ros2Connected => ros2Bridge != null && ros2Bridge.IsConnected;
        public string ActiveSessionId => sessionId;
        public bool StreamEnabled => streamEnabled;
        public bool RecordEnabled => recordEnabled;
        public string LastAction => lastAction;
        public string LastStatus => lastStatus;
        public string LastError => lastError;
        public float Ros2FrameRateHz => ros2Bridge == null ? 0f : ros2Bridge.FrameRateHz;
        public long Ros2DroppedFrames => ros2Bridge == null ? 0L : ros2Bridge.DroppedFrames;

        private void Reset()
        {
            ResolveBindings();
            ApplyRos2Defaults();
        }

        private void Awake()
        {
            ResolveBindings();
        }

        private void OnValidate()
        {
            ros2MainHz = Mathf.Clamp(ros2MainHz, 1f, 200f);
            speedPercent = Mathf.Clamp(speedPercent, 0.1f, 60f);
            EnsureTargetLength(ref homeTargetDeg);
            EnsureTargetLength(ref presetTargetADeg);
            EnsureTargetLength(ref presetTargetBDeg);
        }

        public void ResolveBindings()
        {
            if (runtime == null) runtime = GetComponent<DigitalTwinRuntime>();
            if (runtime == null) runtime = FindObjectOfType<DigitalTwinRuntime>();
            if (ros2Bridge == null) ros2Bridge = GetComponent<Ros2Bridge>();
            if (ros2Bridge == null && runtime != null) ros2Bridge = runtime.Ros2Bridge;
            if (ros2Bridge == null) ros2Bridge = FindObjectOfType<Ros2Bridge>();
            if (commandController == null) commandController = GetComponent<TwinCommandController>();
            if (commandController == null) commandController = FindObjectOfType<TwinCommandController>();
            if (paperRecorder == null && runtime != null) paperRecorder = runtime.PaperRecorder;
            if (paperRecorder == null) paperRecorder = GetComponent<TwinPaperRecorder>();
            if (paperRecorder == null) paperRecorder = FindObjectOfType<TwinPaperRecorder>();
        }

        public void ApplyRos2Defaults()
        {
            jointStateTopic = "/dsr01/joint_states";
            moveJointTopic = "/dt/cmd/move_joint";
            haltTopic = "/dt/cmd/halt";
            setModeTopic = "/dt/cmd/set_mode";
            modeStatusTopic = "/dt/status/mode";
            motionStatusTopic = "/dt/status/motion";
            enableWrenchTopic = false;
            enableTcpPoseTopic = false;
            ros2MainHz = 200f;
            jointPosition = true;
            jointVelocity = true;
            jointEffort = true;
            toolForce = true;
            tcpPose = true;
            extraSignals = false;
        }

        public RobotCommandResult ConnectRos2()
        {
            ResolveBindings();
            if (ros2Bridge == null)
            {
                return Remember("CONNECT_ROS2", false, "ERROR", "Ros2Bridge is unavailable.");
            }

            ApplyTopicSettings();
            bool ok = ros2Bridge.Connect();
            return Remember("CONNECT_ROS2", ok, ok ? "CONNECTED" : "ERROR", ros2Bridge.LastError);
        }

        public RobotCommandResult DisconnectRos2()
        {
            ResolveBindings();
            ros2Bridge?.Disconnect();
            return Remember("DISCONNECT_ROS2", true, "DISCONNECTED", string.Empty);
        }

        public RobotCommandResult ApplyTopicSettings()
        {
            ResolveBindings();
            if (ros2Bridge == null)
            {
                return Remember("APPLY_TOPICS", false, "ERROR", "Ros2Bridge is unavailable.");
            }

            ros2Bridge.ConfigureTopics(jointStateTopic, moveJointTopic, haltTopic, setModeTopic, modeStatusTopic, motionStatusTopic);
            ros2Bridge.ConfigureTelemetryTopics(enableWrenchTopic, wrenchTopic, enableTcpPoseTopic, tcpPoseTopic);
            return Remember("APPLY_TOPICS", true, "APPLIED", string.Empty);
        }

        public RobotCommandResult CreateSession()
        {
            ResolveBindings();
            if (string.IsNullOrWhiteSpace(experimentId))
            {
                experimentId = "ros2_moveit_unity";
            }

            sessionId = experimentId + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            phaseId = 0;
            segmentId = Mathf.Max(1, segmentId);
            streamEnabled = false;
            recordEnabled = false;
            ConfigurePaper("SESSION_CREATED", BuildChannelNotes());
            return Remember("CREATE_SESSION", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult CloseSession()
        {
            if (recordEnabled) StopPaperRecord();
            if (streamEnabled) StopStream();
            paperRecorder?.CloseSession();
            sessionId = string.Empty;
            return Remember("CLOSE_SESSION", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult ApplyChannelsAndRates()
        {
            ConfigurePaper("CHANNELS_APPLIED", BuildChannelNotes());
            return Remember("APPLY_CHANNELS", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult StartStream()
        {
            EnsureSession();
            streamEnabled = true;
            paperRecorder?.SetSessionStreamEnabled(true);
            ConfigurePaper("STREAM_STARTED", BuildChannelNotes());
            return Remember("START_STREAM", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult StopStream()
        {
            streamEnabled = false;
            paperRecorder?.SetSessionStreamEnabled(false);
            ConfigurePaper("STREAM_STOPPED", string.Empty);
            return Remember("STOP_STREAM", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult StartPaperRecord()
        {
            EnsureSession();
            if (!streamEnabled)
            {
                StartStream();
            }

            segmentId = Mathf.Max(1, segmentId);
            recordEnabled = true;
            paperRecorder?.SetSessionRecordEnabled(true, segmentId);
            ConfigurePaper("RECORD_STARTED", BuildChannelNotes());
            return Remember("START_RECORD", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult StopPaperRecord()
        {
            recordEnabled = false;
            paperRecorder?.SetSessionRecordEnabled(false, segmentId);
            ConfigurePaper("RECORD_STOPPED", string.Empty);
            return Remember("STOP_RECORD", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult StopRecordAndStream()
        {
            StopPaperRecord();
            return StopStream();
        }

        public RobotCommandResult SendHome()
        {
            return SendJointTarget("MOVE_HOME", homeTargetDeg);
        }

        public RobotCommandResult SendPresetA()
        {
            return SendJointTarget("MOVE_PRESET_A", presetTargetADeg);
        }

        public RobotCommandResult SendPresetB()
        {
            return SendJointTarget("MOVE_PRESET_B", presetTargetBDeg);
        }

        public RobotCommandResult Halt()
        {
            ResolveBindings();
            long sendWallMs = SystemClock.UtcUnixMs();
            long sendNs = SystemClock.NowNs();
            RobotCommandResult result;
            if (publishDirectlyToRos2)
            {
                result = ros2Bridge == null
                    ? new RobotCommandResult(false, false, "ERROR", "Ros2Bridge is unavailable.")
                    : ros2Bridge.SendHalt();
            }
            else
            {
                result = commandController == null
                    ? new RobotCommandResult(false, false, "ERROR", "TwinCommandController is unavailable.")
                    : commandController.EmergencyStop();
            }

            LogPaperCommand("HALT", string.Empty, result, sendWallMs, sendNs, string.Empty);
            return Remember("HALT", result);
        }

        public RobotCommandResult MarkEvent(string eventType = "ROS2_PANEL_MARK")
        {
            ConfigurePaper(eventType, BuildChannelNotes());
            return Remember(eventType, true, "LOCAL", string.Empty);
        }

        private RobotCommandResult SendJointTarget(string action, float[] targetDeg)
        {
            ResolveBindings();
            long sendWallMs = SystemClock.UtcUnixMs();
            long sendNs = SystemClock.NowNs();
            if (publishDirectlyToRos2)
            {
                if (ros2Bridge == null)
                {
                    RobotCommandResult missingBridge = new RobotCommandResult(false, false, "ERROR", "Ros2Bridge is unavailable.");
                    LogPaperCommand(action, string.Empty, missingBridge, sendWallMs, sendNs, FormatTargetDeg(targetDeg));
                    return Remember(action, missingBridge);
                }

                RobotCommandResult rosResult = ros2Bridge.SendMoveJointRad(DegreesToRadians(targetDeg), speedPercent);
                LogPaperCommand(action, string.Empty, rosResult, sendWallMs, sendNs, FormatTargetDeg(targetDeg));
                return Remember(action, rosResult);
            }

            if (commandController == null)
            {
                RobotCommandResult missing = new RobotCommandResult(false, false, "ERROR", "TwinCommandController is unavailable.");
                LogPaperCommand(action, string.Empty, missing, sendWallMs, sendNs, FormatTargetDeg(targetDeg));
                return Remember(action, missing);
            }

            float[] targetRad = DegreesToRadians(targetDeg);
            RobotCommandResult result = commandController.SendMoveJoint(targetRad, speedPercent);
            LogPaperCommand(action, string.Empty, result, sendWallMs, sendNs, FormatTargetDeg(targetDeg));
            return Remember(action, result);
        }

        private void EnsureSession()
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                CreateSession();
            }
        }

        private void ConfigurePaper(string eventType, string notes)
        {
            if (paperRecorder == null || string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            paperRecorder.ConfigureSession(experimentId, sessionId, phaseId, segmentId, streamEnabled, recordEnabled, eventType, notes);
        }

        private void LogPaperCommand(string action, string reqId, RobotCommandResult result, long sendWallMs, long sendNs, string targetSummary)
        {
            if (paperRecorder == null || !paperRecorder.IsRecording)
            {
                return;
            }

            paperRecorder.RecordCommand(
                action,
                "ROS2",
                reqId,
                string.Empty,
                result,
                sendWallMs,
                sendNs,
                SystemClock.UtcUnixMs(),
                SystemClock.NowNs(),
                targetSummary);
        }

        private RobotCommandResult Remember(string action, RobotCommandResult result)
        {
            lastAction = string.IsNullOrEmpty(action) ? "none" : action;
            lastStatus = result.Status ?? string.Empty;
            lastError = result.ErrorMessage ?? string.Empty;
            return result;
        }

        private RobotCommandResult Remember(string action, bool success, string status, string error)
        {
            return Remember(action, new RobotCommandResult(success, false, status, error));
        }

        private string BuildChannelNotes()
        {
            return "{" +
                   "\"source\":\"" + Escape(sourceId) + "\"," +
                   "\"ros2_main_hz\":" + Num(ros2MainHz) + "," +
                   "\"joint_position\":" + Bool(jointPosition) + "," +
                   "\"joint_velocity\":" + Bool(jointVelocity) + "," +
                   "\"joint_effort\":" + Bool(jointEffort) + "," +
                   "\"tool_force\":" + Bool(toolForce) + "," +
                   "\"tcp_pose\":" + Bool(tcpPose) + "," +
                   "\"extra_signals\":" + Bool(extraSignals) +
                   "}";
        }

        private static float[] DegreesToRadians(float[] targetDeg)
        {
            if (targetDeg == null) return Array.Empty<float>();
            float[] targetRad = new float[targetDeg.Length];
            for (int i = 0; i < targetDeg.Length; i++)
            {
                targetRad[i] = targetDeg[i] * Mathf.Deg2Rad;
            }
            return targetRad;
        }

        private static string FormatTargetDeg(float[] targetDeg)
        {
            if (targetDeg == null || targetDeg.Length == 0) return string.Empty;
            string[] parts = new string[targetDeg.Length];
            for (int i = 0; i < targetDeg.Length; i++)
            {
                parts[i] = targetDeg[i].ToString("0.###", CultureInfo.InvariantCulture);
            }
            return string.Join(";", parts);
        }

        private static void EnsureTargetLength(ref float[] target)
        {
            if (target != null && target.Length == 6) return;
            float[] old = target;
            target = new float[6];
            if (old != null) Array.Copy(old, target, Mathf.Min(old.Length, target.Length));
        }

        private static string Bool(bool value) => value ? "true" : "false";
        private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        private static string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
