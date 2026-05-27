using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
using System.Collections.Generic;
using RosMessageTypes.BuiltinInterfaces;
using RosMessageTypes.Geometry;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using RosMessageTypes.Trajectory;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using UnityEngine;

namespace DigitalTwin
{
    [DisallowMultipleComponent]
    public sealed class Ros2Bridge : MonoBehaviour, IRobotStateSource
    {
        [Header("ROS2 Source")]
        [SerializeField, Tooltip("是否启用 ROS2 Source。关闭后不订阅状态 Topic，也不发布控制命令。")]
        private bool enableBridge = true;

        [SerializeField, Tooltip("When true, Play Mode starts ROS2 TCP/subscribe automatically. Keep false for manual Connect from Ros2ExperimentPanel.")]
        private bool autoStartRos2Transport;

        [SerializeField, Tooltip("可选：手动指定 ROSConnection。为空时自动使用 ROSConnection.GetOrCreateInstance()。")]
        private ROSConnection rosConnection;

        [SerializeField, Tooltip("是否在运行时自动查找/创建 ROSConnection。")]
        private bool autoResolveRosConnection = true;

        [SerializeField, Tooltip("Win11→WSL2 联调 TCP 地址。WSL2 endpoint 监听 0.0.0.0:10002 时 Unity 用 127.0.0.1。")]
        private string rosTcpHost = "127.0.0.1";

        [SerializeField] private int rosTcpPort = 10002;

        [SerializeField, Tooltip("超过多少秒没有收到 joint_states 视为连接中断。")]
        private float connectionTimeoutSec = 2f;

        [SerializeField, Tooltip("源队列上限。超过时会丢弃旧帧，防止主线程被积压压垮。")]
        private int maxQueueSize = 2048;

        [Header("State Topics")]
        [SerializeField] private string jointStateTopic = "/joint_states";
        [SerializeField] private bool enableWrenchTopic = true;
        [SerializeField] private string wrenchTopic = "/wrench";
        [SerializeField] private bool enableTcpPoseTopic = true;
        [SerializeField] private string tcpPoseTopic = "/tcp_pose";
        [SerializeField] private string modeStatusTopic = "/dt/status/mode";
        [SerializeField] private string motionStatusTopic = "/dt/status/motion";
        [SerializeField] private string sessionStatusTopic = "/dt/status/session";

        [Header("Command Topics")]
        [SerializeField] private string setModeTopic = "/dt/cmd/set_mode";
        [SerializeField] private string moveJointTopic = "/dt/cmd/move_joint";
        [SerializeField] private string haltTopic = "/dt/cmd/halt";
        [SerializeField] private string sessionTopic = "/dt/cmd/session";
        [SerializeField, Tooltip("Doosan namespace prefix for rt_topic subscriptions, e.g. dsr01")]
        private string robotNamespace = "dsr01";
        [SerializeField, Tooltip("Optional run_id copied into Unity paper logs when ROS2 state packets do not carry one.")]
        private string runId = string.Empty;
        [SerializeField, Tooltip("Optional trial_id copied into Unity paper logs when ROS2 state packets do not carry one.")]
        private string trialId = string.Empty;
        [SerializeField] private float minSpeedPercent = 1f;
        [SerializeField] private float maxSpeedPercent = 60f;
        [SerializeField] private float minDurationSec = 0.2f;
        [SerializeField] private float maxDurationSec = 4f;

        [Header("Debug")]
        [SerializeField] private bool verboseLog;

        private readonly ConcurrentQueue<RobotStateFrame> _frames = new ConcurrentQueue<RobotStateFrame>();
        private readonly object _statusLock = new object();
        private RobotSignalSchema _schema;
        private ROSConnection _ros;
        private bool _running;
        private string _lastError = string.Empty;
        private string _lastCommandStatus = string.Empty;
        private string _lastCommandError = string.Empty;
        private long _frameSequenceId;
        private long _lastReceiveNs;
        private long _lastSourceSeq = -1;
        private long _droppedFrames;
        private long _outOfOrderFrames;
        private float _lastFrameRateWindowAt;
        private int _framesInWindow;
        private float _frameRateHz;
        private float[] _latestForce = Array.Empty<float>();

        private PoseStampedMsg _latestPose;
        private bool _hasPose;
        private WrenchStampedMsg _latestWrench;
        private bool _hasWrench;
        private string _mode = "unknown";
        private string _motion = "unknown";
        private string _motionError = string.Empty;
        private bool _publishersRegistered;
        private bool _telemetryStreamEnabled;
        private readonly Dictionary<string, float[]> _rtCache = new Dictionary<string, float[]>();
        private readonly List<string> _activeRtTopics = new List<string>();
        private readonly object _sessionStatusLock = new object();
        private readonly object _jointSnapshotLock = new object();
        private Ros2SessionStatus _latestSessionStatus = new Ros2SessionStatus();
        private float[] _latestJointDeg = new float[6];
        private long _latestJointReceiveNs;

        public bool IsRunning => _running;
        public Ros2SessionStatus LatestSessionStatus
        {
            get
            {
                lock (_sessionStatusLock)
                {
                    return _latestSessionStatus;
                }
            }
        }

        public bool Ros2RecordEnabled
        {
            get
            {
                lock (_sessionStatusLock)
                {
                    return _latestSessionStatus != null && _latestSessionStatus.record_enabled;
                }
            }
        }

        /// <summary>Latest /joint_states snapshot (deg). Requires telemetry stream.</summary>
        public bool TryGetLatestJointPositionsDeg(out float[] degrees, out double ageMs)
        {
            degrees = null;
            ageMs = -1d;
            lock (_jointSnapshotLock)
            {
                if (_latestJointReceiveNs <= 0)
                {
                    return false;
                }

                ageMs = SystemClock.ElapsedMs(_latestJointReceiveNs, SystemClock.NowNs());
                degrees = new float[_latestJointDeg.Length];
                Array.Copy(_latestJointDeg, degrees, _latestJointDeg.Length);
                return true;
            }
        }
        public bool TelemetryStreamEnabled => _telemetryStreamEnabled;
        public bool IsLinkConnected =>
            _running && _ros != null && _ros.HasConnectionThread && !_ros.HasConnectionError;

        public bool IsConnected
        {
            get
            {
                bool rosConnected = _ros != null && _ros.HasConnectionThread && !_ros.HasConnectionError;
                if (!_running || !rosConnected)
                {
                    return false;
                }

                if (!_telemetryStreamEnabled)
                {
                    return true;
                }

                bool hasRecentFrame = _lastReceiveNs > 0 &&
                                      SystemClock.ElapsedMs(_lastReceiveNs, SystemClock.NowNs()) <=
                                      connectionTimeoutSec * 1000.0;
                return hasRecentFrame;
            }
        }

        public string CurrentMode
        {
            get
            {
                lock (_statusLock) return _mode;
            }
        }

        public string MotionState
        {
            get
            {
                lock (_statusLock) return _motion;
            }
        }

        public string MotionError
        {
            get
            {
                lock (_statusLock) return _motionError;
            }
        }

        public long LastSeq => _lastSourceSeq;
        public double LatencyMs { get; private set; }
        public double LastReceiveAgeMs => _lastReceiveNs <= 0 ? -1.0 : SystemClock.ElapsedMs(_lastReceiveNs, SystemClock.NowNs());
        public long DroppedFrames => _droppedFrames;
        public long OutOfOrderFrames => _outOfOrderFrames;
        public float FrameRateHz => _frameRateHz;
        public string LastError => _lastError;
        public string LastCommandStatus => _lastCommandStatus;
        public string LastCommandError => _lastCommandError;
        public string JointStateTopic => jointStateTopic;
        public string MoveJointTopic => moveJointTopic;
        public string HaltTopic => haltTopic;
        public string SetModeTopic => setModeTopic;
        public string ModeStatusTopic => modeStatusTopic;
        public string MotionStatusTopic => motionStatusTopic;
        public string SessionStatusTopic => sessionStatusTopic;
        public RuntimeSourceKind Kind => RuntimeSourceKind.Ros2;
        public int QueuedFrameCount => _frames.Count;

        public bool Connect()
        {
            return ConnectLink();
        }

        public bool ConnectLink()
        {
            enableBridge = true;
            StartLinkOnly();
            return _running;
        }

        public bool StartTelemetryStream(Ros2TelemetryChannels channels)
        {
            if (channels == null)
            {
                channels = Ros2TelemetryChannels.FromPanel(true, true, true, true, true, false);
            }

            if (!_running)
            {
                ConnectLink();
            }

            StopTelemetrySubscriptions();
            EnsureRosConnection();
            EnsureCommandPublishers();
            _ros.Subscribe<JointStateMsg>(jointStateTopic, OnJointState);
            SubscribeRtTopics(channels);
            _telemetryStreamEnabled = true;
            _lastError = string.Empty;
            return true;
        }

        public void StopTelemetryStream()
        {
            StopTelemetrySubscriptions();
            _telemetryStreamEnabled = false;
            _lastReceiveNs = 0;
            _rtCache.Clear();
            while (_frames.TryDequeue(out _))
            {
            }
        }

        public bool PublishSessionCommand(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            try
            {
                EnsureRosConnection();
                EnsureCommandPublishers();
                _ros.Publish(sessionTopic, new StringMsg { data = json });
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }
        }

        public void Disconnect()
        {
            StopTelemetryStream();
            StopBridge();
            if (_ros != null)
            {
                try
                {
                    _ros.Disconnect();
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                }
            }
        }

        public void ConfigureTopics(
            string jointState,
            string moveJoint,
            string halt,
            string setMode,
            string modeStatus,
            string motionStatus)
        {
            ApplyTopicConfiguration(
                jointState,
                moveJoint,
                halt,
                setMode,
                modeStatus,
                motionStatus,
                enableWrenchTopic,
                wrenchTopic,
                enableTcpPoseTopic,
                tcpPoseTopic);
        }

        public void ConfigureTelemetryTopics(bool enableWrench, string wrench, bool enableTcpPose, string tcpPose)
        {
            ApplyTopicConfiguration(
                jointStateTopic,
                moveJointTopic,
                haltTopic,
                setModeTopic,
                modeStatusTopic,
                motionStatusTopic,
                enableWrench,
                wrench,
                enableTcpPose,
                tcpPose);
        }

        public void ApplyTopicConfiguration(
            string jointState,
            string moveJoint,
            string halt,
            string setMode,
            string modeStatus,
            string motionStatus,
            bool enableWrench,
            string wrench,
            bool enableTcpPose,
            string tcpPose)
        {
            string nextJointState = string.IsNullOrWhiteSpace(jointState) ? jointStateTopic : jointState.Trim();
            string nextMoveJoint = string.IsNullOrWhiteSpace(moveJoint) ? moveJointTopic : moveJoint.Trim();
            string nextHalt = string.IsNullOrWhiteSpace(halt) ? haltTopic : halt.Trim();
            string nextSetMode = string.IsNullOrWhiteSpace(setMode) ? setModeTopic : setMode.Trim();
            string nextModeStatus = string.IsNullOrWhiteSpace(modeStatus) ? modeStatusTopic : modeStatus.Trim();
            string nextMotionStatus = string.IsNullOrWhiteSpace(motionStatus) ? motionStatusTopic : motionStatus.Trim();
            string nextWrench = string.IsNullOrWhiteSpace(wrench) ? wrenchTopic : wrench.Trim();
            string nextTcpPose = string.IsNullOrWhiteSpace(tcpPose) ? tcpPoseTopic : tcpPose.Trim();

            bool configurationUnchanged =
                nextJointState == jointStateTopic &&
                nextMoveJoint == moveJointTopic &&
                nextHalt == haltTopic &&
                nextSetMode == setModeTopic &&
                nextModeStatus == modeStatusTopic &&
                nextMotionStatus == motionStatusTopic &&
                enableWrench == enableWrenchTopic &&
                enableTcpPose == enableTcpPoseTopic &&
                nextWrench == wrenchTopic &&
                nextTcpPose == tcpPoseTopic;

            if (configurationUnchanged)
            {
                return;
            }

            bool wasRunning = _running;
            if (wasRunning)
            {
                StopBridge();
            }

            jointStateTopic = nextJointState;
            moveJointTopic = nextMoveJoint;
            haltTopic = nextHalt;
            setModeTopic = nextSetMode;
            modeStatusTopic = nextModeStatus;
            motionStatusTopic = nextMotionStatus;
            enableWrenchTopic = enableWrench;
            enableTcpPoseTopic = enableTcpPose;
            wrenchTopic = nextWrench;
            tcpPoseTopic = nextTcpPose;
            _publishersRegistered = false;

            if (wasRunning)
            {
                if (_telemetryStreamEnabled)
                {
                    StartTelemetryStream(null);
                }
                else
                {
                    StartLinkOnly();
                }
            }
        }

        public void ApplySessionStatusTopic(string topic)
        {
            string next = string.IsNullOrWhiteSpace(topic) ? sessionStatusTopic : topic.Trim();
            if (next == sessionStatusTopic)
            {
                return;
            }

            if (_running && _ros != null)
            {
                SafeUnsubscribe(sessionStatusTopic);
            }

            sessionStatusTopic = next;

            if (_running && _ros != null)
            {
                _ros.Subscribe<StringMsg>(sessionStatusTopic, OnSessionStatus);
            }
        }

        public void Initialize(TwinRuntimeProfile profile, RobotSignalSchema schema)
        {
            _schema = schema;
            ApplyProfile(profile);
            if (enableBridge && autoStartRos2Transport)
            {
                StartBridge();
            }
        }

        private void OnValidate()
        {
            connectionTimeoutSec = Mathf.Max(0.25f, connectionTimeoutSec);
            maxQueueSize = Mathf.Max(64, maxQueueSize);
            minSpeedPercent = Mathf.Max(0.1f, minSpeedPercent);
            maxSpeedPercent = Mathf.Max(minSpeedPercent, maxSpeedPercent);
            minDurationSec = Mathf.Max(0.01f, minDurationSec);
            maxDurationSec = Mathf.Max(minDurationSec, maxDurationSec);
        }

        private void OnDisable()
        {
            StopBridge();
        }

        private void OnDestroy()
        {
            StopBridge();
        }

        public RobotSourceStatus GetStatus()
        {
            return new RobotSourceStatus("ROS2Bridge", IsConnected, _lastError, _lastReceiveNs, _frames.Count);
        }

        public SourceHealth GetHealth()
        {
            return new SourceHealth(
                "ROS2Bridge",
                IsConnected,
                _lastReceiveNs,
                LastReceiveAgeMs,
                _frameSequenceId,
                _droppedFrames,
                _frameRateHz,
                _lastError);
        }

        public bool TryDequeueFrame(out RobotStateFrame frame)
        {
            return _frames.TryDequeue(out frame);
        }

        public float[] GetLatestForceCopy()
        {
            float[] copy = new float[_latestForce == null ? 0 : _latestForce.Length];
            if (_latestForce != null)
            {
                Array.Copy(_latestForce, copy, copy.Length);
            }

            return copy;
        }

        public RobotCommandResult SetMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return SetCommandResult(false, false, "BLOCKED", "Mode is empty.");
            }

            if (!enableBridge)
            {
                return SetCommandResult(false, false, "BLOCKED", "ROS2 command publishing is disabled.");
            }

            try
            {
                EnsureRosConnection();
                EnsureCommandPublishers();
                _ros.Publish(setModeTopic, new StringMsg(mode));
                LogVerbose($"Publish set_mode topic={setModeTopic} mode={mode}");
                return SetCommandResult(true, false, "SENT", string.Empty);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return SetCommandResult(false, false, "ERROR", ex.Message);
            }
        }

        public RobotCommandResult SendMoveJointRad(float[] targetJointRad, float speedPercent)
        {
            if (targetJointRad == null || targetJointRad.Length == 0)
            {
                return SetCommandResult(false, false, "BLOCKED", "Target is empty.");
            }

            if (!enableBridge)
            {
                return SetCommandResult(false, false, "BLOCKED", "ROS2 command publishing is disabled.");
            }

            try
            {
                EnsureRosConnection();
                EnsureCommandPublishers();
                int jointCount = ResolveJointCount(targetJointRad);
                string[] jointNames = BuildJointNames(jointCount);
                double[] positions = new double[jointCount];
                double[] speedHint = new double[jointCount];
                for (int i = 0; i < jointCount; i++)
                {
                    positions[i] = i < targetJointRad.Length ? targetJointRad[i] : 0.0;
                    speedHint[i] = Mathf.Clamp(speedPercent, minSpeedPercent, maxSpeedPercent);
                }

                float durationSec = ResolveDurationSeconds(speedPercent);
                JointTrajectoryPointMsg point = new JointTrajectoryPointMsg
                {
                    positions = positions,
                    velocities = speedHint,
                    accelerations = Array.Empty<double>(),
                    effort = Array.Empty<double>(),
                    time_from_start = ToDuration(durationSec)
                };

                JointTrajectoryMsg msg = new JointTrajectoryMsg
                {
                    header = new HeaderMsg(),
                    joint_names = jointNames,
                    points = new[] { point }
                };

                _ros.Publish(moveJointTopic, msg);
                LogVerbose($"Publish move_joint topic={moveJointTopic} joints={jointCount} duration={durationSec:0.###}s");
                return SetCommandResult(true, false, "SENT", string.Empty);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return SetCommandResult(false, false, "ERROR", ex.Message);
            }
        }

        public RobotCommandResult SendHalt()
        {
            if (!enableBridge)
            {
                return SetCommandResult(false, false, "BLOCKED", "ROS2 command publishing is disabled.");
            }

            try
            {
                EnsureRosConnection();
                EnsureCommandPublishers();
                _ros.Publish(haltTopic, new BoolMsg(true));
                LogVerbose($"Publish halt topic={haltTopic}");
                return SetCommandResult(true, false, "SENT", string.Empty);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return SetCommandResult(false, false, "ERROR", ex.Message);
            }
        }

        private void ApplyProfile(TwinRuntimeProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            TwinRuntimeSettings settings = profile.BuildRuntimeSettings();
            enableBridge = settings.UseRos2 && enableBridge;
            autoStartRos2Transport = settings.AutoStartRos2Transport;
        }

        private void StartLinkOnly()
        {
            if (_running && IsLinkConnected)
            {
                return;
            }

            if (_running && !IsLinkConnected)
            {
                StopBridge();
            }

            try
            {
                EnsureRosConnection();
                EnsureTcpConnected();
                _ros.Subscribe<StringMsg>(modeStatusTopic, OnModeStatus);
                _ros.Subscribe<StringMsg>(motionStatusTopic, OnMotionStatus);
                _ros.Subscribe<StringMsg>(sessionStatusTopic, OnSessionStatus);
                EnsureCommandPublishers();

                _running = true;
                _telemetryStreamEnabled = false;
                _lastError = string.Empty;
            }
            catch (Exception ex)
            {
                _running = false;
                _telemetryStreamEnabled = false;
                _lastError = ex.Message;
                _publishersRegistered = false;
                SafeUnsubscribe(modeStatusTopic);
                SafeUnsubscribe(motionStatusTopic);
                SafeUnsubscribe(sessionStatusTopic);
            }
        }

        private void StartBridge()
        {
            ConnectLink();
            StartTelemetryStream(Ros2TelemetryChannels.FromPanel(true, true, true, true, true, false));
        }

        private void StopBridge()
        {
            if (!_running && _ros == null)
            {
                return;
            }

            StopTelemetrySubscriptions();
            _running = false;
            _telemetryStreamEnabled = false;
            _publishersRegistered = false;
            if (_ros != null)
            {
                SafeUnsubscribe(modeStatusTopic);
                SafeUnsubscribe(motionStatusTopic);
                SafeUnsubscribe(sessionStatusTopic);
            }
        }

        private void StopTelemetrySubscriptions()
        {
            if (_ros == null)
            {
                _activeRtTopics.Clear();
                return;
            }

            SafeUnsubscribe(jointStateTopic);
            if (enableWrenchTopic)
            {
                SafeUnsubscribe(wrenchTopic);
            }

            if (enableTcpPoseTopic)
            {
                SafeUnsubscribe(tcpPoseTopic);
            }

            foreach (string topic in _activeRtTopics)
            {
                SafeUnsubscribe(topic);
            }

            _activeRtTopics.Clear();
        }

        private void SubscribeRtTopics(Ros2TelemetryChannels channels)
        {
            if (_ros == null || channels == null)
            {
                return;
            }

            foreach (KeyValuePair<string, bool> entry in channels.ToRosChannelMap())
            {
                if (!entry.Value)
                {
                    continue;
                }

                string key = entry.Key;
                if (key.StartsWith("joint_", StringComparison.Ordinal))
                {
                    continue;
                }

                string topic = $"/{robotNamespace}/rt_topic/{key}";
                _ros.Subscribe<Float32MultiArrayMsg>(topic, msg => OnRtArray(key, msg));
                _activeRtTopics.Add(topic);
            }

            if (enableWrenchTopic && channels.externalTcpForce)
            {
                _ros.Subscribe<WrenchStampedMsg>(wrenchTopic, OnWrench);
            }

            if (enableTcpPoseTopic && channels.actualTcpPose)
            {
                _ros.Subscribe<PoseStampedMsg>(tcpPoseTopic, OnTcpPose);
            }
        }

        private void OnRtArray(string key, Float32MultiArrayMsg msg)
        {
            if (!_telemetryStreamEnabled || msg?.data == null)
            {
                return;
            }

            float[] copy = new float[msg.data.Length];
            Array.Copy(msg.data, copy, msg.data.Length);
            _rtCache[key] = copy;
        }

        private void EnsureRosConnection()
        {
            if (rosConnection != null)
            {
                _ros = rosConnection;
                return;
            }

            if (_ros != null)
            {
                return;
            }

            if (!autoResolveRosConnection)
            {
                throw new InvalidOperationException("ROSConnection is not assigned.");
            }

            _ros = ROSConnection.GetOrCreateInstance();
            if (_ros == null)
            {
                throw new InvalidOperationException("ROSConnection.GetOrCreateInstance() returned null.");
            }
        }

        private void EnsureTcpConnected()
        {
            EnsureRosConnection();
            if (_ros.HasConnectionThread && !_ros.HasConnectionError)
            {
                return;
            }

            if (_ros.HasConnectionThread)
            {
                _ros.Disconnect();
            }

            _ros.Connect(rosTcpHost, rosTcpPort);
        }

        private void WaitForLinkHandshake(int timeoutMs)
        {
            int waited = 0;
            while (waited < timeoutMs)
            {
                if (IsLinkConnected)
                {
                    return;
                }

                Thread.Sleep(20);
                waited += 20;
            }
        }

        private void EnsureCommandPublishers()
        {
            if (_ros == null || _publishersRegistered)
            {
                return;
            }

            _ros.RegisterPublisher<StringMsg>(setModeTopic);
            _ros.RegisterPublisher<JointTrajectoryMsg>(moveJointTopic);
            _ros.RegisterPublisher<BoolMsg>(haltTopic);
            _ros.RegisterPublisher<StringMsg>(sessionTopic);
            _publishersRegistered = true;
        }

        private void SafeUnsubscribe(string topic)
        {
            try
            {
                _ros?.Unsubscribe(topic);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
        }

        private void OnModeStatus(StringMsg msg)
        {
            lock (_statusLock)
            {
                _mode = msg == null || string.IsNullOrWhiteSpace(msg.data) ? _mode : msg.data.Trim();
            }
        }

        private void OnSessionStatus(StringMsg msg)
        {
            if (msg == null || string.IsNullOrWhiteSpace(msg.data))
            {
                return;
            }

            try
            {
                Ros2SessionStatus parsed = JsonUtility.FromJson<Ros2SessionStatus>(msg.data);
                if (parsed == null)
                {
                    return;
                }

                lock (_sessionStatusLock)
                {
                    _latestSessionStatus = parsed;
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
        }

        private void OnMotionStatus(StringMsg msg)
        {
            if (msg == null)
            {
                return;
            }

            string text = msg.data ?? string.Empty;
            string motion = text;
            string error = string.Empty;
            int split = text.IndexOf('|');
            if (split >= 0)
            {
                motion = text.Substring(0, split).Trim();
                error = text.Substring(split + 1).Trim();
            }

            lock (_statusLock)
            {
                _motion = string.IsNullOrWhiteSpace(motion) ? _motion : motion;
                _motionError = error;
            }
        }

        private void OnWrench(WrenchStampedMsg msg)
        {
            lock (_statusLock)
            {
                _latestWrench = msg;
                _hasWrench = msg != null;
            }
        }

        private void OnTcpPose(PoseStampedMsg msg)
        {
            lock (_statusLock)
            {
                _latestPose = msg;
                _hasPose = msg != null;
            }
        }

        private void OnJointState(JointStateMsg msg)
        {
            if (!_running || !_telemetryStreamEnabled || msg == null)
            {
                return;
            }

            long receiveNs = SystemClock.NowNs();
            long receiveWallMs = SystemClock.UtcUnixMs();
            long sequenceId = Interlocked.Increment(ref _frameSequenceId);
            PoseStampedMsg pose;
            WrenchStampedMsg wrench;
            string mode;
            string motion;
            string motionError;
            lock (_statusLock)
            {
                pose = _hasPose ? _latestPose : null;
                wrench = _hasWrench ? _latestWrench : null;
                mode = _mode;
                motion = _motion;
                motionError = _motionError;
            }

            RobotStateFrame frame = new RobotStateFrame
            {
                SourceName = "ROS2",
                SequenceId = sequenceId,
                UnityReceiveTimestampNs = receiveNs,
                UnityReceiveWallMs = receiveWallMs,
                Channel = "joint_states",
                RunId = runId ?? string.Empty,
                TrialId = trialId ?? string.Empty,
                Mode = mode ?? string.Empty,
                MotionState = motion ?? string.Empty,
                MotionError = motionError ?? string.Empty,
                RawPayload = BuildRawBrief(msg),
                Flags = RobotFrameFlags.Valid
            };

            double sourceMs = ExtractHeaderTimestampMs(msg.header);
            frame.SourceTimestampMs = sourceMs;
            frame.SourceTimestampSeconds = sourceMs > 0d ? sourceMs / 1000.0 : 0d;
            frame.ClockSyncStatus = SystemClock.IsLikelySameWallClock(sourceMs, receiveWallMs)
                ? TwinClockSyncStatus.Synced
                : TwinClockSyncStatus.Unsynced;
            if (frame.SourceTimestampMs <= 0d)
            {
                frame.Flags |= RobotFrameFlags.InvalidTimestamp;
            }

            ApplyJointState(msg, frame);
            UpdateLatestJointSnapshot(msg);
            ApplyTcpPose(pose, frame);
            ApplyWrench(wrench, frame);
            ApplyRtCache(frame);

            if ((frame.Flags & (RobotFrameFlags.HasJointPosition | RobotFrameFlags.HasForce | RobotFrameFlags.HasJointTorque)) == 0 && !frame.HasTcpPose)
            {
                frame.Flags |= RobotFrameFlags.InvalidSchema;
            }

            if (maxQueueSize > 0)
            {
                while (_frames.Count >= maxQueueSize && _frames.TryDequeue(out _))
                {
                    _droppedFrames++;
                }
            }

            _frames.Enqueue(frame);
            _lastReceiveNs = receiveNs;
            UpdateStats(frame);
        }

        private void ApplyJointState(JointStateMsg joint, RobotStateFrame frame)
        {
            if (joint == null)
            {
                return;
            }

            int count = ResolveJointCount(joint.position, joint.velocity, joint.effort);
            if (count <= 0)
            {
                return;
            }

            frame.JointPositionRad = new float[count];
            if (CopyJointArrayRaw(joint.position, joint.name, frame.JointPositionRad, frame))
            {
                frame.Flags |= RobotFrameFlags.HasJointPosition;
            }

            if (joint.velocity != null && joint.velocity.Length > 0)
            {
                frame.JointVelocityRad = new float[count];
                if (CopyJointArrayRaw(joint.velocity, joint.name, frame.JointVelocityRad, frame))
                {
                    frame.Flags |= RobotFrameFlags.HasJointVelocity;
                }
            }

            if (joint.effort != null && joint.effort.Length > 0)
            {
                frame.JointTorqueNm = new float[count];
                if (CopyJointArrayRaw(joint.effort, joint.name, frame.JointTorqueNm, frame))
                {
                    frame.Flags |= RobotFrameFlags.HasJointTorque;
                }
            }
        }

        private void UpdateLatestJointSnapshot(JointStateMsg msg)
        {
            if (msg?.position == null || msg.position.Length == 0)
            {
                return;
            }

            int count = Math.Min(6, msg.position.Length);
            lock (_jointSnapshotLock)
            {
                for (int i = 0; i < count; i++)
                {
                    int index = ResolveJointIndex(msg.name, i, 6);
                    if (index >= 0 && index < _latestJointDeg.Length)
                    {
                        _latestJointDeg[index] = (float)(msg.position[i] * Mathf.Rad2Deg);
                    }
                }

                _latestJointReceiveNs = SystemClock.NowNs();
            }
        }

        private void ApplyTcpPose(PoseStampedMsg pose, RobotStateFrame frame)
        {
            if (pose == null || pose.pose == null || pose.pose.position == null || pose.pose.orientation == null)
            {
                return;
            }

            frame.HasTcpPose = true;
            frame.TcpPositionMeters = pose.pose.position.From<FLU>();
            frame.TcpRotation = pose.pose.orientation.From<FLU>();
        }

        private void ApplyWrench(WrenchStampedMsg wrench, RobotStateFrame frame)
        {
            if (wrench == null || wrench.wrench == null || wrench.wrench.force == null || wrench.wrench.torque == null)
            {
                return;
            }

            frame.ForceVector = new[]
            {
                (float)wrench.wrench.force.x,
                (float)wrench.wrench.force.y,
                (float)wrench.wrench.force.z,
                (float)wrench.wrench.torque.x,
                (float)wrench.wrench.torque.y,
                (float)wrench.wrench.torque.z
            };
            frame.Flags |= RobotFrameFlags.HasForce;
            _latestForce = frame.ForceVector;
        }

        private void ApplyRtCache(RobotStateFrame frame)
        {
            if (_rtCache.TryGetValue("external_tcp_force", out float[] force) && force.Length >= 6)
            {
                frame.ForceVector = force;
                frame.Flags |= RobotFrameFlags.HasForce;
                _latestForce = force;
            }
            else if (_rtCache.TryGetValue("raw_force_torque", out float[] raw) && raw.Length >= 6)
            {
                frame.ForceVector = raw;
                frame.Flags |= RobotFrameFlags.HasForce;
                _latestForce = raw;
            }

            if (_rtCache.TryGetValue("external_joint_torque", out float[] jt) && jt.Length > 0)
            {
                frame.JointTorqueNm = jt;
                frame.Flags |= RobotFrameFlags.HasJointTorque;
            }

            if (_rtCache.TryGetValue("actual_tcp_position", out float[] tcp) && tcp.Length >= 6)
            {
                frame.HasTcpPose = true;
                frame.TcpPositionMeters = new Vector3(tcp[0], tcp[1], tcp[2]);
                frame.TcpRotation = Quaternion.Euler(tcp[3] * Mathf.Rad2Deg, tcp[4] * Mathf.Rad2Deg, tcp[5] * Mathf.Rad2Deg);
            }
        }

        private void UpdateStats(RobotStateFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            if (frame.ClockSyncStatus == TwinClockSyncStatus.Synced && frame.SourceTimestampMs > 0d)
            {
                LatencyMs = Math.Max(0d, SystemClock.UtcUnixMs() - frame.SourceTimestampMs);
            }

            if (_lastSourceSeq >= 0 && frame.SequenceId <= _lastSourceSeq)
            {
                _outOfOrderFrames++;
            }
            else if (_lastSourceSeq >= 0 && frame.SequenceId > _lastSourceSeq + 1)
            {
                _droppedFrames += frame.SequenceId - _lastSourceSeq - 1;
            }

            _lastSourceSeq = frame.SequenceId;
            _framesInWindow++;
            float now = Time.unscaledTime;
            float elapsed = now - _lastFrameRateWindowAt;
            if (elapsed >= 1f)
            {
                _frameRateHz = _framesInWindow / elapsed;
                _framesInWindow = 0;
                _lastFrameRateWindowAt = now;
            }
        }

        private int ResolveJointCount(params double[][] sources)
        {
            if (_schema != null && _schema.JointCount > 0)
            {
                return _schema.JointCount;
            }

            if (sources == null)
            {
                return 0;
            }

            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] != null && sources[i].Length > 0)
                {
                    return sources[i].Length;
                }
            }

            return 0;
        }

        private int ResolveJointCount(float[] source)
        {
            if (_schema != null && _schema.JointCount > 0)
            {
                return _schema.JointCount;
            }

            return source == null ? 0 : source.Length;
        }

        private bool CopyJointArrayRaw(double[] source, string[] names, float[] destination, RobotStateFrame frame)
        {
            if (source == null || destination == null)
            {
                return false;
            }

            bool copied = false;
            int count = Math.Min(source.Length, destination.Length);
            for (int i = 0; i < count; i++)
            {
                int index = ResolveJointIndex(names, i, destination.Length);
                if (index < 0)
                {
                    frame.Flags |= RobotFrameFlags.InvalidSchema;
                    continue;
                }

                destination[index] = (float)source[i];
                copied = true;
            }

            return copied;
        }

        private int ResolveJointIndex(string[] names, int fallbackIndex, int maxCount)
        {
            string name = names != null && fallbackIndex < names.Length ? names[fallbackIndex] : string.Empty;
            if (_schema != null)
            {
                int resolved = _schema.ResolveJointIndex(name, fallbackIndex);
                return resolved >= 0 && resolved < maxCount ? resolved : -1;
            }

            return fallbackIndex >= 0 && fallbackIndex < maxCount ? fallbackIndex : -1;
        }

        private string[] BuildJointNames(int count)
        {
            string[] names = new string[count];
            for (int i = 0; i < count; i++)
            {
                if (_schema != null && _schema.JointNames != null && i < _schema.JointNames.Length)
                {
                    names[i] = string.IsNullOrWhiteSpace(_schema.JointNames[i]) ? $"joint_{i + 1}" : _schema.JointNames[i];
                }
                else
                {
                    names[i] = $"joint_{i + 1}";
                }
            }

            return names;
        }

        private static DurationMsg ToDuration(float seconds)
        {
            float safe = Mathf.Max(0.0f, seconds);
            int sec = Mathf.FloorToInt(safe);
            float sub = safe - sec;
            uint nano = (uint)Mathf.Clamp(Mathf.RoundToInt(sub * 1000000000.0f), 0, 999999999);
#if ROS2
            return new DurationMsg(sec, nano);
#else
            return new DurationMsg(sec, (int)nano);
#endif
        }

        private float ResolveDurationSeconds(float speedPercent)
        {
            float speed = Mathf.Clamp(speedPercent, minSpeedPercent, maxSpeedPercent);
            float t = Mathf.InverseLerp(minSpeedPercent, maxSpeedPercent, speed);
            return Mathf.Lerp(maxDurationSec, minDurationSec, t);
        }

        private RobotCommandResult SetCommandResult(bool success, bool dryRun, string status, string error)
        {
            _lastCommandStatus = status;
            _lastCommandError = error ?? string.Empty;
            return new RobotCommandResult(success, dryRun, status, error);
        }

        private static double ExtractHeaderTimestampMs(HeaderMsg header)
        {
            if (header == null || header.stamp == null)
            {
                return 0d;
            }

            double sec = Convert.ToDouble(header.stamp.sec, CultureInfo.InvariantCulture);
            double nano = Convert.ToDouble(header.stamp.nanosec, CultureInfo.InvariantCulture);
            return sec * 1000.0 + nano / 1000000.0;
        }

        private static string BuildRawBrief(JointStateMsg msg)
        {
            int names = msg.name == null ? 0 : msg.name.Length;
            int pos = msg.position == null ? 0 : msg.position.Length;
            return $"joint_states names={names} pos={pos}";
        }

        private void LogVerbose(string text)
        {
            if (verboseLog)
            {
                Debug.Log($"[Ros2Bridge] {text}", this);
            }
        }
    }
}
