using System;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace DigitalTwin
{
    public enum Ros2PanelOperatingMode
    {
        Idle = 0,
        Control = 1,
    }

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
        [SerializeField] private string sessionStatusTopic = "/dt/status/session";
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
        [SerializeField] private bool recordPending;

        [Header("Channels / Rates")]
        [SerializeField, Tooltip("ROS2 bridge 主 CSV 目标频率；真实上限由 /joint_states 和 rt_timer_ms 决定。")]
        private float ros2MainHz = 200f;
        [SerializeField] private bool jointPosition = true;
        [SerializeField] private bool jointVelocity = true;
        [SerializeField] private bool jointEffort = true;
        [SerializeField] private bool actualTcpPose = true;
        [SerializeField] private bool actualFlangePose;
        [SerializeField] private bool externalTcpForce = true;
        [SerializeField] private bool externalJointTorque = true;
        [SerializeField] private bool actualJointTorque;
        [SerializeField] private bool actualMotorTorque;
        [SerializeField] private bool rawForceTorque;
        [SerializeField] private bool targetJointPosition;
        [SerializeField] private bool targetTcpPosition;
        [SerializeField] private bool robotMode = true;
        [SerializeField] private bool robotState = true;
        [SerializeField] private bool controlMode = true;
        [SerializeField] private bool jointTemperature;
        [SerializeField] private bool solutionSpace;
        [SerializeField] private bool operationSpeedRate;

        [Header("Operating Mode")]
        [SerializeField] private Ros2PanelOperatingMode operatingMode = Ros2PanelOperatingMode.Idle;

        [Header("Preset Joint Commands")]
        [SerializeField] private float speedPercent = 10f;
        [SerializeField, Tooltip("Capture 后 A = Home J1 + 此角度(deg)")]
        private float presetJ1DeltaADeg = 10f;
        [SerializeField, Tooltip("Capture 后 B = Home J2 + 此角度(deg)")]
        private float presetJ2DeltaBDeg = -10f;
        [SerializeField] private string capturedPoseSummary = string.Empty;
        [SerializeField, Tooltip("开启后按钮直接发布到 Ros2Bridge 的 /dt/cmd/move_joint；虚拟运动由 ROS2 adapter 执行。")]
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
        public bool Ros2LinkConnected => ros2Bridge != null && ros2Bridge.IsLinkConnected;
        public bool Ros2Connected => ros2Bridge != null && ros2Bridge.IsConnected;
        public string ActiveSessionId => sessionId;
        public bool StreamEnabled => streamEnabled;
        public bool RecordEnabled => recordEnabled;
        public bool RecordPending => recordPending;
        public bool Ros2RecordEnabled => ros2Bridge != null && ros2Bridge.Ros2RecordEnabled;
        public Ros2PanelOperatingMode OperatingMode => operatingMode;
        public bool IsControlMode => operatingMode == Ros2PanelOperatingMode.Control;
        public string LastAction => lastAction;
        public string LastStatus => lastStatus;
        public string LastError => lastError;
        public string CapturedPoseSummary => capturedPoseSummary;
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
            operatingMode = Ros2PanelOperatingMode.Idle;
        }

        private void Update()
        {
            if (!recordPending || ros2Bridge == null)
            {
                return;
            }

            if (!ros2Bridge.Ros2RecordEnabled)
            {
                return;
            }

            recordPending = false;
            recordEnabled = true;
            paperRecorder?.SetSessionRecordEnabled(true, segmentId);
            ConfigurePaper("RECORD_STARTED", BuildChannelNotes());
            Remember("START_RECORD", true, "RECORDING", string.Empty);
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
            actualTcpPose = true;
            externalTcpForce = true;
            externalJointTorque = true;
        }

        public Ros2TelemetryChannels BuildTelemetryChannels()
        {
            return new Ros2TelemetryChannels
            {
                jointPosition = jointPosition,
                jointVelocity = jointVelocity,
                jointEffort = jointEffort,
                actualTcpPose = actualTcpPose,
                actualFlangePose = actualFlangePose,
                externalTcpForce = externalTcpForce,
                externalJointTorque = externalJointTorque,
                actualJointTorque = actualJointTorque,
                actualMotorTorque = actualMotorTorque,
                rawForceTorque = rawForceTorque,
                targetJointPosition = targetJointPosition,
                targetTcpPosition = targetTcpPosition,
                robotMode = robotMode,
                robotState = robotState,
                controlMode = controlMode,
                jointTemperature = jointTemperature,
                solutionSpace = solutionSpace,
                operationSpeedRate = operationSpeedRate,
            };
        }

        public RobotCommandResult ConnectRos2()
        {
            ResolveBindings();
            if (ros2Bridge == null)
            {
                return Remember("CONNECT_ROS2", false, "ERROR", "Ros2Bridge is unavailable.");
            }

            ApplyTopicSettings();
            if (ros2Bridge.IsRunning && ros2Bridge.IsLinkConnected)
            {
                return Remember("CONNECT_ROS2", true, "LINKED", "TCP linked (no telemetry yet).");
            }

            bool ok = ros2Bridge.ConnectLink();
            return Remember("CONNECT_ROS2", ok, ok ? "LINKED" : "ERROR", ros2Bridge.LastError);
        }

        public RobotCommandResult DisconnectRos2()
        {
            ResolveBindings();
            SetIdleMode();
            ros2Bridge?.Disconnect();
            streamEnabled = false;
            recordEnabled = false;
            recordPending = false;
            return Remember("DISCONNECT_ROS2", true, "DISCONNECTED", string.Empty);
        }

        public RobotCommandResult SetIdleMode()
        {
            operatingMode = Ros2PanelOperatingMode.Idle;
            ros2Bridge?.SetMode("idle");
            ConfigurePaper("IDLE_MODE", string.Empty);
            return Remember("IDLE_MODE", true, "STANDBY",
                "Receive-only; channels/stream/record OK; motion blocked.");
        }

        public RobotCommandResult SetControlMode()
        {
            if (!Ros2LinkConnected && !(ros2Bridge != null && ros2Bridge.IsRunning))
            {
                return Remember("CONTROL_MODE", false, "BLOCKED", "Connect ROS2 first.");
            }

            operatingMode = Ros2PanelOperatingMode.Control;
            ros2Bridge?.SetMode("control");
            ConfigurePaper("CONTROL_MODE", string.Empty);
            return Remember("CONTROL_MODE", true, "CONTROL", "Motion commands enabled.");
        }

        public RobotCommandResult ApplyTopicSettings()
        {
            ResolveBindings();
            if (ros2Bridge == null)
            {
                return Remember("APPLY_TOPICS", false, "ERROR", "Ros2Bridge is unavailable.");
            }

            ros2Bridge.ApplyTopicConfiguration(
                jointStateTopic,
                moveJointTopic,
                haltTopic,
                setModeTopic,
                modeStatusTopic,
                motionStatusTopic,
                enableWrenchTopic,
                wrenchTopic,
                enableTcpPoseTopic,
                tcpPoseTopic);
            ros2Bridge.ApplySessionStatusTopic(sessionStatusTopic);
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
            recordPending = false;
            PublishSession("create_session");
            ConfigurePaper("SESSION_CREATED", BuildChannelNotes());
            return Remember("CREATE_SESSION", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult CloseSession()
        {
            if (recordEnabled || recordPending) StopPaperRecord();
            if (streamEnabled) StopStream();
            PublishSession("close_session");
            paperRecorder?.CloseSession();
            sessionId = string.Empty;
            return Remember("CLOSE_SESSION", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult ApplyChannelsAndRates()
        {
            PublishSession("set_channels", includeChannels: true);
            ConfigurePaper("CHANNELS_APPLIED", BuildChannelNotes());
            return Remember("APPLY_CHANNELS", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult StartStream()
        {
            EnsureSession();
            if (ros2Bridge == null)
            {
                return Remember("START_STREAM", false, "ERROR", "Ros2Bridge is unavailable.");
            }

            if (!ros2Bridge.IsRunning)
            {
                ConnectRos2();
            }

            ros2Bridge.StartTelemetryStream(BuildTelemetryChannels());
            PublishSession("start_stream", includeChannels: true);
            streamEnabled = true;
            paperRecorder?.SetSessionStreamEnabled(true);
            ConfigurePaper("STREAM_STARTED", BuildChannelNotes());
            return Remember("START_STREAM", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult StopStream()
        {
            streamEnabled = false;
            ros2Bridge?.StopTelemetryStream();
            PublishSession("stop_stream");
            paperRecorder?.SetSessionStreamEnabled(false);
            if (recordEnabled || recordPending) StopPaperRecord();
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
            recordEnabled = false;
            recordPending = true;
            PublishSession("start_record", includeChannels: true);

            if (ros2Bridge != null && ros2Bridge.Ros2RecordEnabled)
            {
                recordPending = false;
                recordEnabled = true;
                paperRecorder?.SetSessionRecordEnabled(true, segmentId);
                ConfigurePaper("RECORD_STARTED", BuildChannelNotes());
                return Remember("START_RECORD", true, "RECORDING", string.Empty);
            }

            ConfigurePaper("RECORD_REQUESTED", BuildChannelNotes());
            return Remember("START_RECORD", true, "PENDING", "Waiting for ROS2 record_enabled.");
        }

        public RobotCommandResult StopPaperRecord()
        {
            recordPending = false;
            recordEnabled = false;
            PublishSession("stop_record");
            paperRecorder?.SetSessionRecordEnabled(false, segmentId);
            ConfigurePaper("RECORD_STOPPED", string.Empty);
            return Remember("STOP_RECORD", true, "LOCAL", string.Empty);
        }

        public RobotCommandResult StopRecordAndStream()
        {
            StopPaperRecord();
            return StopStream();
        }

        public RobotCommandResult CaptureCurrentPoseAsPresets()
        {
            ResolveBindings();
            if (ros2Bridge == null)
            {
                return Remember("CAPTURE_POSE", false, "ERROR", "Ros2Bridge is unavailable.");
            }

            if (!streamEnabled && !ros2Bridge.TelemetryStreamEnabled)
            {
                return Remember("CAPTURE_POSE", false, "BLOCKED",
                    "Start data stream first so /joint_states is live.");
            }

            if (!ros2Bridge.TryGetLatestJointPositionsDeg(out float[] currentDeg, out double ageMs))
            {
                return Remember("CAPTURE_POSE", false, "ERROR", "No recent joint_states.");
            }

            if (ageMs > 500d)
            {
                return Remember("CAPTURE_POSE", false, "STALE",
                    $"joint_states age {ageMs:0}ms > 500ms.");
            }

            homeTargetDeg = CopyJointArray(currentDeg);
            presetTargetADeg = CopyJointArray(currentDeg);
            presetTargetADeg[0] += presetJ1DeltaADeg;
            presetTargetBDeg = CopyJointArray(currentDeg);
            presetTargetBDeg[1] += presetJ2DeltaBDeg;
            capturedPoseSummary = FormatTargetDeg(homeTargetDeg);
            Ros2CapturedPosePersistence.Save(homeTargetDeg, presetTargetADeg, presetTargetBDeg, capturedPoseSummary);
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
            ConfigurePaper("POSE_CAPTURED", BuildPresetNotesJson());
            return Remember("CAPTURE_POSE", true, "CAPTURED", capturedPoseSummary);
        }

        /// <summary>退出 Play 后把 Capture 写回 Inspector 序列化字段（由 Editor 调用）。</summary>
        public void ApplyPersistedCaptureToEditMode()
        {
            if (!Ros2CapturedPosePersistence.HasCapture)
            {
                return;
            }

            homeTargetDeg = CopyJointArray(Ros2CapturedPosePersistence.HomeDeg);
            presetTargetADeg = CopyJointArray(Ros2CapturedPosePersistence.PresetADeg);
            presetTargetBDeg = CopyJointArray(Ros2CapturedPosePersistence.PresetBDeg);
            capturedPoseSummary = Ros2CapturedPosePersistence.Summary ?? string.Empty;
        }

        public RobotCommandResult SendHome() => SendJointTarget("MOVE_HOME", homeTargetDeg);
        public RobotCommandResult SendPresetA() => SendJointTarget("MOVE_PRESET_A", presetTargetADeg);
        public RobotCommandResult SendPresetB() => SendJointTarget("MOVE_PRESET_B", presetTargetBDeg);

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

        private void PublishSession(string action, bool includeChannels = false)
        {
            if (ros2Bridge == null || string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            StringBuilder json = new StringBuilder(256);
            json.Append("{\"action\":\"").Append(Escape(action)).Append("\"");
            json.Append(",\"session_id\":\"").Append(Escape(sessionId)).Append("\"");
            json.Append(",\"experiment_id\":\"").Append(Escape(experimentId)).Append("\"");
            json.Append(",\"source_id\":\"").Append(Escape(sourceId)).Append("\"");
            if (includeChannels)
            {
                json.Append(",\"channels\":").Append(BuildChannelNotes());
            }

            json.Append('}');
            ros2Bridge.PublishSessionCommand(json.ToString());
        }

        private RobotCommandResult SendJointTarget(string action, float[] targetDeg)
        {
            if (!IsControlMode)
            {
                RobotCommandResult blocked = new RobotCommandResult(
                    false, false, "BLOCKED", "Idle mode: switch to Control mode to send motion.");
                LogPaperCommand(action, string.Empty, blocked, SystemClock.UtcUnixMs(), SystemClock.NowNs(),
                    FormatTargetDeg(targetDeg));
                return Remember(action, blocked);
            }

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

            bool effectiveStream = streamEnabled || paperRecorder.StreamEnabled;
            bool effectiveRecord = recordEnabled || paperRecorder.RecordEnabled;
            paperRecorder.ConfigureSession(
                experimentId, sessionId, phaseId, segmentId,
                effectiveStream, effectiveRecord, eventType, notes);
        }

        private void LogPaperCommand(string action, string reqId, RobotCommandResult result, long sendWallMs, long sendNs, string targetSummary)
        {
            if (paperRecorder == null || !paperRecorder.IsRecording)
            {
                return;
            }

            paperRecorder.RecordCommand(
                action, "ROS2", reqId, string.Empty, result,
                sendWallMs, sendNs, SystemClock.UtcUnixMs(), SystemClock.NowNs(), targetSummary);
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
            return BuildTelemetryChannels().ToJsonNotes();
        }

        private string BuildPresetNotesJson()
        {
            return "{\"home\":" + BuildTargetJson(homeTargetDeg)
                   + ",\"A\":" + BuildTargetJson(presetTargetADeg)
                   + ",\"B\":" + BuildTargetJson(presetTargetBDeg) + "}";
        }

        private static string BuildTargetJson(float[] targetDeg)
        {
            if (targetDeg == null || targetDeg.Length == 0)
            {
                return "[]";
            }

            string[] parts = new string[targetDeg.Length];
            for (int i = 0; i < targetDeg.Length; i++)
            {
                parts[i] = targetDeg[i].ToString("0.###", CultureInfo.InvariantCulture);
            }

            return "[" + string.Join(",", parts) + "]";
        }

        private static float[] CopyJointArray(float[] source)
        {
            float[] copy = new float[6];
            if (source == null)
            {
                return copy;
            }

            Array.Copy(source, copy, Math.Min(source.Length, copy.Length));
            return copy;
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

        private static string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
