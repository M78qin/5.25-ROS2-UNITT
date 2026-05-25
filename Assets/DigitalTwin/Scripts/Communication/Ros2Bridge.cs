using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Threading;
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

        [SerializeField, Tooltip("可选：手动指定 ROSConnection。为空时自动使用 ROSConnection.GetOrCreateInstance()。")]
        private ROSConnection rosConnection;

        [SerializeField, Tooltip("是否在运行时自动查找/创建 ROSConnection。")]
        private bool autoResolveRosConnection = true;

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

        [Header("Command Topics")]
        [SerializeField] private string setModeTopic = "/dt/cmd/set_mode";
        [SerializeField] private string moveJointTopic = "/dt/cmd/move_joint";
        [SerializeField] private string haltTopic = "/dt/cmd/halt";
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

        public bool IsRunning => _running;
        public bool IsConnected
        {
            get
            {
                bool hasRecentFrame = _lastReceiveNs > 0 &&
                                      SystemClock.ElapsedMs(_lastReceiveNs, SystemClock.NowNs()) <= connectionTimeoutSec * 1000.0;
                bool rosConnected = _ros != null && _ros.HasConnectionThread && !_ros.HasConnectionError;
                return _running && hasRecentFrame && rosConnected;
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
        public RuntimeSourceKind Kind => RuntimeSourceKind.Ros2;
        public int QueuedFrameCount => _frames.Count;

        public bool Connect()
        {
            enableBridge = true;
            StartBridge();
            return _running;
        }

        public void Disconnect()
        {
            StopBridge();
        }

        public void ConfigureTopics(
            string jointState,
            string moveJoint,
            string halt,
            string setMode,
            string modeStatus,
            string motionStatus)
        {
            bool wasRunning = _running;
            if (wasRunning)
            {
                StopBridge();
            }

            if (!string.IsNullOrWhiteSpace(jointState)) jointStateTopic = jointState.Trim();
            if (!string.IsNullOrWhiteSpace(moveJoint)) moveJointTopic = moveJoint.Trim();
            if (!string.IsNullOrWhiteSpace(halt)) haltTopic = halt.Trim();
            if (!string.IsNullOrWhiteSpace(setMode)) setModeTopic = setMode.Trim();
            if (!string.IsNullOrWhiteSpace(modeStatus)) modeStatusTopic = modeStatus.Trim();
            if (!string.IsNullOrWhiteSpace(motionStatus)) motionStatusTopic = motionStatus.Trim();
            _publishersRegistered = false;

            if (wasRunning)
            {
                StartBridge();
            }
        }

        public void ConfigureTelemetryTopics(bool enableWrench, string wrench, bool enableTcpPose, string tcpPose)
        {
            bool wasRunning = _running;
            if (wasRunning)
            {
                StopBridge();
            }

            enableWrenchTopic = enableWrench;
            enableTcpPoseTopic = enableTcpPose;
            if (!string.IsNullOrWhiteSpace(wrench)) wrenchTopic = wrench.Trim();
            if (!string.IsNullOrWhiteSpace(tcpPose)) tcpPoseTopic = tcpPose.Trim();

            if (wasRunning)
            {
                StartBridge();
            }
        }

        public void Initialize(TwinRuntimeProfile profile, RobotSignalSchema schema)
        {
            _schema = schema;
            ApplyProfile(profile);
            if (enableBridge)
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
        }

        private void StartBridge()
        {
            if (_running)
            {
                return;
            }

            try
            {
                EnsureRosConnection();
                _ros.Subscribe<JointStateMsg>(jointStateTopic, OnJointState);
                if (enableWrenchTopic)
                {
                    _ros.Subscribe<WrenchStampedMsg>(wrenchTopic, OnWrench);
                }

                if (enableTcpPoseTopic)
                {
                    _ros.Subscribe<PoseStampedMsg>(tcpPoseTopic, OnTcpPose);
                }

                _ros.Subscribe<StringMsg>(modeStatusTopic, OnModeStatus);
                _ros.Subscribe<StringMsg>(motionStatusTopic, OnMotionStatus);
                EnsureCommandPublishers();

                _running = true;
                _lastError = string.Empty;
            }
            catch (Exception ex)
            {
                _running = false;
                _lastError = ex.Message;
            }
        }

        private void StopBridge()
        {
            if (!_running && _ros == null)
            {
                return;
            }

            _running = false;
            if (_ros != null)
            {
                SafeUnsubscribe(jointStateTopic);
                if (enableWrenchTopic) SafeUnsubscribe(wrenchTopic);
                if (enableTcpPoseTopic) SafeUnsubscribe(tcpPoseTopic);
                SafeUnsubscribe(modeStatusTopic);
                SafeUnsubscribe(motionStatusTopic);
            }
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

        private void EnsureCommandPublishers()
        {
            if (_ros == null || _publishersRegistered)
            {
                return;
            }

            _ros.RegisterPublisher<StringMsg>(setModeTopic);
            _ros.RegisterPublisher<JointTrajectoryMsg>(moveJointTopic);
            _ros.RegisterPublisher<BoolMsg>(haltTopic);
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
            if (!_running || msg == null)
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
            ApplyTcpPose(pose, frame);
            ApplyWrench(wrench, frame);

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
