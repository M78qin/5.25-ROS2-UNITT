using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace DigitalTwin
{
    [DisallowMultipleComponent]
    public sealed class DartStudioBridge : MonoBehaviour, IRobotStateSource, IRobotCommandSink
    {
        [Header("UDP Telemetry / UDP状态流")]
        [SerializeField, Tooltip("是否启用 DartStudio 通信桥。关闭后不绑定 UDP 9090，也不发送心跳。")]
        private bool enableBridge = true;

        [SerializeField, Tooltip("When true, Play Mode starts UDP state receiver and heartbeat automatically. Keep false for manual paper experiments.")]
        private bool autoStartTransport;

        [SerializeField, Tooltip("是否启用 UDP 状态接收。关闭后不绑定 stateUdpPort，只保留可选心跳/TCP 命令。")]
        private bool enableStateReceiver = true;

        [SerializeField, Tooltip("是否启用 Unity -> DartStudio 心跳。关闭后 DartStudio 日志会显示 Unity waiting/timeout。")]
        private bool enableHeartbeat = true;

        [SerializeField, Tooltip("DartStudio Bridge 所在 IP。本机联调通常为 127.0.0.1。")]
        private string dartIp = "127.0.0.1";

        [SerializeField, Tooltip("Unity 接收 robot_state 的 UDP 端口。必须与 DartStudio 端 Unity UDP state 一致，默认 9090。")]
        private int stateUdpPort = 9090;

        [SerializeField, Tooltip("Unity 发心跳到 DartStudio 的 UDP 端口。必须与 DartStudio Heartbeat UDP 一致，默认 9091。")]
        private int heartbeatUdpPort = 9091;

        [SerializeField, Tooltip("Unity 向 DartStudio 发送心跳的频率，单位 Hz。心跳用于 DartStudio 判断 Unity 是否在线。")]
        private float heartbeatHz = 1f;

        [SerializeField, Tooltip("单个 UDP 包最大字节数。超过该值会丢弃并显示错误，防止异常包撑爆队列。")]
        private int receiveBufferLimit = 8192;

        [SerializeField, Tooltip("Maximum queued UDP packets. Old packets are dropped first to prevent experiment backlog.")]
        private int maxRawQueueSize = 2048;

        [SerializeField, Tooltip("超过多少秒没收到 robot_state 就认为连接断开。只影响 UI 显示，不会自动停止 DartStudio。")]
        private float connectionTimeoutSec = 2f;

        [Header("TCP Command / TCP命令")]
        [SerializeField, Tooltip("是否允许 TCP 命令出口。关闭后 SET_MODE、MOVE_JOINT、HALT 都会被 Bridge 阻止。")]
        private bool enableTcpCommand = true;

        [SerializeField, Tooltip("Unity 向 DartStudio 发送 SET_MODE、MOVE_JOINT、HALT 的 TCP 端口，默认 9092。")]
        private int commandTcpPort = 9092;

        [SerializeField, Tooltip("TCP 连接超时时间，单位毫秒。DartStudio 未启动或端口不通时会显示命令失败。")]
        private int connectTimeoutMs = 1500;



        [Header("Debug / 调试")]
        [SerializeField, Tooltip("是否显示左上角 OnGUI 底层通信调试窗。正式运行 UI 由 TwinUIController 自动生成。")]
        private bool showDebugGUI = true;

        [SerializeField, Tooltip("是否在 OnGUI 调试窗显示 SET_MODE/HALT 命令按钮。默认关闭，避免绕过 TwinCommandController 的真实机械臂安全门。")]
        private bool showDebugCommandButtons;

        [SerializeField, Tooltip("OnGUI 调试窗左上角位置，单位像素。")]
        private Vector2 debugPanelPosition = new Vector2(12f, 12f);

        [SerializeField, Tooltip("OnGUI 调试窗尺寸，单位像素。")]
        private Vector2 debugPanelSize = new Vector2(330f, 220f);

        private readonly ConcurrentQueue<RawSourcePacket> _rawPackets = new ConcurrentQueue<RawSourcePacket>();
        private readonly StateFrameNormalizer _normalizer = new StateFrameNormalizer();
        private UdpClient _receiver;
        private UdpClient _heartbeatSender;
        private Thread _receiveThread;
        private CancellationTokenSource _cts;
        private TcpClient _tcpClient;
        private NetworkStream _tcpStream;
        private volatile bool _running;
        private string _lastError = string.Empty;
        private string _lastCommandStatus = string.Empty;
        private string _lastCommandError = string.Empty;
        private long _rawSequenceId;
        private long _lastReceiveNs;
        private float _lastHeartbeatAt;
        private float _lastFrameRateWindowAt;
        private int _framesInWindow;
        private float _frameRateHz;
        private long _lastSourceSeq = -1;
        private long _droppedFrames;
        private long _outOfOrderFrames;
        private float[] _latestForce = Array.Empty<float>();
        private string _lastRemoteEndpoint = string.Empty;
        private TwinExperimentTracker _experimentTracker;

        public bool IsRunning => _running;
        public bool IsTcpConnected => _tcpClient != null && _tcpClient.Connected && _tcpStream != null;
        public bool IsConnected => _running && _lastReceiveNs > 0 && SystemClock.ElapsedMs(_lastReceiveNs, SystemClock.NowNs()) <= connectionTimeoutSec * 1000.0;
        public string CurrentMode { get; private set; } = "unknown";
        public string MotionState { get; private set; } = "unknown";
        public string MotionError { get; private set; } = string.Empty;
        public long LastSeq => _lastSourceSeq;
        public double LatencyMs { get; private set; }
        public double LastReceiveAgeMs => _lastReceiveNs <= 0 ? -1.0 : SystemClock.ElapsedMs(_lastReceiveNs, SystemClock.NowNs());
        public long DroppedFrames => _droppedFrames;
        public long OutOfOrderFrames => _outOfOrderFrames;
        public float FrameRateHz => _frameRateHz;
        public string LastError => _lastError;
        public string LastCommandStatus => _lastCommandStatus;
        public string LastCommandError => _lastCommandError;
        public string LastRemoteEndpoint => _lastRemoteEndpoint;
        public RuntimeSourceKind Kind => RuntimeSourceKind.DartStudio;
        public int QueuedFrameCount => _rawPackets.Count;

        public void Initialize(TwinRuntimeProfile profile, RobotSignalSchema schema)
        {
            ApplyProfile(profile);
            _normalizer.Initialize(schema);
            if (enableBridge && autoStartTransport)
            {
                StartBridge();
            }
        }

        public bool ConnectTransport()
        {
            if (!enableBridge)
            {
                _lastError = "DartStudio bridge is disabled.";
                return false;
            }

            StartBridge();
            return _running;
        }

        public void DisconnectTransport()
        {
            StopBridge();
            CloseTcp();
            ResetSessionState(clearErrors: true);
        }

        public void SetExperimentTracker(TwinExperimentTracker tracker)
        {
            _experimentTracker = tracker;
        }

        private void OnValidate()
        {
            heartbeatHz = Mathf.Max(0.1f, heartbeatHz);
            receiveBufferLimit = Mathf.Max(256, receiveBufferLimit);
            maxRawQueueSize = Mathf.Max(64, maxRawQueueSize);
            connectionTimeoutSec = Mathf.Max(0.25f, connectionTimeoutSec);
            connectTimeoutMs = Mathf.Max(100, connectTimeoutMs);
            debugPanelSize.x = Mathf.Max(220f, debugPanelSize.x);
            debugPanelSize.y = Mathf.Max(120f, debugPanelSize.y);
        }

        private void OnDisable()
        {
            StopBridge();
            CloseTcp();
        }

        private void OnDestroy()
        {
            StopBridge();
            CloseTcp();
        }

        private void OnApplicationQuit()
        {
            StopBridge();
            CloseTcp();
        }

        private void Update()
        {
            SendHeartbeatIfNeeded();
        }

        public RobotSourceStatus GetStatus()
        {
            return new RobotSourceStatus("DartStudioBridge", IsConnected, _lastError, _lastReceiveNs, _rawPackets.Count);
        }

        public SourceHealth GetHealth()
        {
            return new SourceHealth(
                "DartStudioBridge",
                IsConnected,
                _lastReceiveNs,
                LastReceiveAgeMs,
                _rawSequenceId,
                _droppedFrames,
                _frameRateHz,
                _lastError);
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

        public bool TryDequeueFrame(out RobotStateFrame frame)
        {
            frame = null;
            while (_rawPackets.TryDequeue(out RawSourcePacket raw))
            {
                // Check for experiment PING before normal processing
                if (TryHandlePing(raw.Payload))
                {
                    continue;
                }

                if (_normalizer.TryFromDartJson(raw.Payload, "DartStudio", raw.SequenceId, raw.ReceiveTimestampNs, raw.ReceiveWallMs, out frame, out string error))
                {
                    UpdateStats(frame);
                    if (_experimentTracker != null && !string.IsNullOrEmpty(frame.ExperimentType))
                    {
                        _experimentTracker.OnFrameReceived(frame);
                    }

                    return true;
                }

                _lastError = error;
            }

            return false;
        }

        private bool TryHandlePing(string payload)
        {
            if (string.IsNullOrEmpty(payload) || !payload.Contains("\"exp\""))
            {
                return false;
            }

            try
            {
                DartStudioPacket pkt = JsonUtility.FromJson<DartStudioPacket>(payload);
                if (pkt?.exp == null || pkt.exp.type != "PING")
                {
                    return false;
                }

                // Send PONG back via heartbeat UDP
                if (_heartbeatSender != null)
                {
                    long recv_perf_ns = SystemClock.NowNs();
                    string pong = $"{{\"cmd\":\"PONG\",\"send_perf_ns\":{pkt.exp.send_perf_ns},\"recv_ns\":{recv_perf_ns},\"seq\":{pkt.seq}}}";
                    byte[] bytes = Encoding.UTF8.GetBytes(pong);
                    _heartbeatSender.Send(bytes, bytes.Length, dartIp, heartbeatUdpPort);
                    _experimentTracker?.OnPongSent(pkt.exp.send_perf_ns, recv_perf_ns);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public RobotCommandResult SetMode(string mode)
        {
            string json = DartTcpCommandBuilder.BuildSetMode(mode);
            if (string.IsNullOrEmpty(json))
            {
                return SetCommandResult(false, false, "BLOCKED", "Mode is empty.");
            }

            return SendRawCommand(json);
        }

        public RobotCommandResult SendMoveJoint(float[] targetDeg)
        {
            string json = DartTcpCommandBuilder.BuildMoveJoint(targetDeg);
            if (string.IsNullOrEmpty(json))
            {
                return SetCommandResult(false, false, "BLOCKED", "Target is empty.");
            }

            return SendRawCommand(json);
        }

        public RobotCommandResult SendHalt()
        {
            return SendRawCommand(DartTcpCommandBuilder.BuildHalt());
        }

        public RobotCommandResult SendGetState()
        {
            return SendRawCommand(DartTcpCommandBuilder.BuildGetState());
        }

        public RobotCommandResult SendRawCommand(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return SetCommandResult(false, false, "BLOCKED", "Command is empty.");
            }

            if (ContainsBlockedDartMoveSpeed(json))
            {
                return SetCommandResult(false, false, "BLOCKED", "DartStudio MOVE_JOINT cannot include speed/vel/acc. DRL uses fixed MOVE_VEL/MOVE_ACC.");
            }

            return SendJsonCommand(json);
        }

        private static bool ContainsBlockedDartMoveSpeed(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || !json.Contains("\"MOVE_JOINT\""))
            {
                return false;
            }

            return json.Contains("\"speed\"") || json.Contains("\"vel\"") || json.Contains("\"acc\"");
        }

        private void ApplyProfile(TwinRuntimeProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            TwinRuntimeSettings settings = profile.BuildRuntimeSettings();
            enableBridge = settings.UseDartStudio && enableBridge;
            autoStartTransport = settings.AutoStartDartTransport;
            // Profile should be able to fully close debug overlay for performance tests.
            showDebugGUI = settings.EnableDebugOverlay && settings.EnableRuntimeUi;
        }

        private void StartBridge()
        {
            if (_running)
            {
                return;
            }

            ResetSessionState(clearErrors: false);
            _running = true;
            _cts = new CancellationTokenSource();

            try
            {
                if (enableStateReceiver)
                {
                    _receiver = new UdpClient(stateUdpPort);
                    DrainStaleUdpPackets();
                    _receiveThread = new Thread(ReceiveLoop)
                    {
                        IsBackground = true,
                        Name = "DartStudioBridge UDP"
                    };
                    _receiveThread.Start();
                }

                if (enableHeartbeat)
                {
                    _heartbeatSender = new UdpClient();
                }

                _lastError = string.Empty;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _running = false;
                CleanupUdp();
            }
        }

        /// <summary>
        /// Drain any stale UDP packets left in the kernel receive buffer
        /// from a previous session. Prevents reading old seq/mode data on Play.
        /// </summary>
        private void DrainStaleUdpPackets()
        {
            if (_receiver == null) return;
            _receiver.Client.Blocking = false;
            byte[] dump = new byte[receiveBufferLimit];
            int drained = 0;
            try
            {
                EndPoint dummy = new IPEndPoint(IPAddress.Any, 0);
                while (_receiver.Client.Available > 0)
                {
                    _receiver.Client.ReceiveFrom(dump, ref dummy);
                    drained++;
                }
            }
            catch { }
            finally
            {
                _receiver.Client.Blocking = true;
            }
            if (drained > 0)
            {
                Debug.Log($"[DartStudioBridge] Drained {drained} stale UDP packets from port {stateUdpPort}.");
            }
        }

        private void StopBridge()
        {
            if (!_running && _receiver == null && _heartbeatSender == null)
            {
                return;
            }

            _running = false;
            try { _cts?.Cancel(); } catch { }

            // Close socket first to unblock the blocking Receive() call,
            // then wait for the thread to exit cleanly.
            CleanupUdp();

            try
            {
                if (_receiveThread != null && _receiveThread.IsAlive)
                {
                    _receiveThread.Join(400);
                }
            }
            catch { }

            _receiveThread = null;
            _cts?.Dispose();
            _cts = null;
        }

        private void ReceiveLoop()
        {
            IPEndPoint endpoint = new IPEndPoint(IPAddress.Any, 0);
            while (_running)
            {
                try
                {
                byte[] bytes = _receiver.Receive(ref endpoint);
                    if (bytes == null || bytes.Length == 0)
                    {
                        continue;
                    }

                    if (receiveBufferLimit > 0 && bytes.Length > receiveBufferLimit)
                    {
                        _lastError = $"UDP packet exceeded receiveBufferLimit ({bytes.Length}>{receiveBufferLimit}).";
                        continue;
                    }

                    long receiveNs = SystemClock.NowNs();
                    long receiveWallMs = SystemClock.UtcUnixMs();
                    _lastRemoteEndpoint = endpoint.ToString();
                    EnqueueRawPacket(new RawSourcePacket(Encoding.UTF8.GetString(bytes), receiveNs, receiveWallMs, Interlocked.Increment(ref _rawSequenceId)));
                    _lastReceiveNs = receiveNs;
                }
                catch (SocketException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                }
            }
        }

        private void SendHeartbeatIfNeeded()
        {
            if (!_running || !enableHeartbeat || _heartbeatSender == null)
            {
                return;
            }

            float interval = 1f / Mathf.Max(0.1f, heartbeatHz);
            if (Time.unscaledTime - _lastHeartbeatAt < interval)
            {
                return;
            }

            _lastHeartbeatAt = Time.unscaledTime;
            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(DartTcpCommandBuilder.BuildHeartbeat());
                _heartbeatSender.Send(bytes, bytes.Length, dartIp, heartbeatUdpPort);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
        }

        private void EnqueueRawPacket(RawSourcePacket packet)
        {
            if (maxRawQueueSize > 0)
            {
                while (_rawPackets.Count >= maxRawQueueSize && _rawPackets.TryDequeue(out _))
                {
                    Interlocked.Increment(ref _droppedFrames);
                }
            }

            _rawPackets.Enqueue(packet);
        }

        private RobotCommandResult SendJsonCommand(string json)
        {
            if (!enableBridge || !enableTcpCommand)
            {
                return SetCommandResult(false, false, "BLOCKED", "DartStudio TCP command is disabled.");
            }

            if (!_running)
            {
                return SetCommandResult(false, false, "BLOCKED", "DartStudio transport is disconnected. Click Connect first.");
            }

            if (!EnsureTcpConnected())
            {
                return SetCommandResult(false, false, "ERROR", "DartStudio TCP command connection failed.");
            }

            try
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json + "\n");
                _tcpStream.Write(bytes, 0, bytes.Length);
                _tcpStream.Flush();
                return SetCommandResult(true, false, "SENT", string.Empty);
            }
            catch (Exception ex)
            {
                CloseTcp();
                return SetCommandResult(false, false, "ERROR", ex.Message);
            }
        }

        private bool EnsureTcpConnected()
        {
            if (_tcpClient != null && _tcpClient.Connected && _tcpStream != null)
            {
                return true;
            }

            CloseTcp();
            try
            {
                _tcpClient = new TcpClient();
                IAsyncResult result = _tcpClient.BeginConnect(dartIp, commandTcpPort, null, null);
                if (!result.AsyncWaitHandle.WaitOne(Mathf.Max(100, connectTimeoutMs)))
                {
                    CloseTcp();
                    return false;
                }

                _tcpClient.EndConnect(result);
                _tcpClient.NoDelay = true;
                _tcpStream = _tcpClient.GetStream();
                return true;
            }
            catch (Exception ex)
            {
                _lastCommandError = ex.Message;
                CloseTcp();
                return false;
            }
        }

        private RobotCommandResult SetCommandResult(bool success, bool dryRun, string status, string error)
        {
            _lastCommandStatus = status;
            _lastCommandError = error ?? string.Empty;
            return new RobotCommandResult(success, dryRun, status, error);
        }

        private void UpdateStats(RobotStateFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            CurrentMode = string.IsNullOrEmpty(frame.Mode) ? CurrentMode : frame.Mode;
            MotionState = string.IsNullOrEmpty(frame.MotionState) ? MotionState : frame.MotionState;
            MotionError = frame.MotionError ?? string.Empty;

            if (frame.ClockSyncStatus == TwinClockSyncStatus.Synced && frame.SourceTimestampMs > 0d)
            {
                LatencyMs = Math.Max(0d, SystemClock.UtcUnixMs() - frame.SourceTimestampMs);
            }

            // Detect session reset: large backward jump means DartMock restarted.
            if (_lastSourceSeq >= 0 && frame.SequenceId < _lastSourceSeq - 1000)
            {
                _droppedFrames = 0;
                _outOfOrderFrames = 0;
            }
            else if (_lastSourceSeq >= 0 && frame.SequenceId <= _lastSourceSeq)
            {
                _outOfOrderFrames++;
            }
            else if (_lastSourceSeq >= 0 && frame.SequenceId > _lastSourceSeq + 1)
            {
                _droppedFrames += frame.SequenceId - _lastSourceSeq - 1;
            }

            _lastSourceSeq = frame.SequenceId;

            if (frame.ForceVector != null)
            {
                _latestForce = frame.ForceVector;
            }

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

        private void CleanupUdp()
        {
            try { _receiver?.Close(); } catch { }
            try { _heartbeatSender?.Close(); } catch { }
            _receiver = null;
            _heartbeatSender = null;
        }

        private void ResetSessionState(bool clearErrors)
        {
            while (_rawPackets.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _rawSequenceId, 0);
            _lastReceiveNs = 0;
            _lastHeartbeatAt = 0f;
            _lastFrameRateWindowAt = Time.unscaledTime;
            _framesInWindow = 0;
            _frameRateHz = 0f;
            _lastSourceSeq = -1;
            _droppedFrames = 0;
            _outOfOrderFrames = 0;
            _latestForce = Array.Empty<float>();
            _lastRemoteEndpoint = string.Empty;
            LatencyMs = 0d;
            CurrentMode = "unknown";
            MotionState = "unknown";
            MotionError = string.Empty;
            if (clearErrors)
            {
                _lastError = string.Empty;
                _lastCommandStatus = string.Empty;
                _lastCommandError = string.Empty;
            }
        }

        private void CloseTcp()
        {
            try { _tcpStream?.Close(); } catch { }
            try { _tcpClient?.Close(); } catch { }
            _tcpStream = null;
            _tcpClient = null;
        }

        private void OnGUI()
        {
            if (!showDebugGUI)
            {
                return;
            }

            Rect rect = new Rect(debugPanelPosition.x, debugPanelPosition.y, debugPanelSize.x, debugPanelSize.y);
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.Box(rect, GUIContent.none);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(rect.x + 10f, rect.y + 8f, rect.width - 20f, rect.height - 16f));
            GUILayout.Label("[DartStudio Bridge]");
            GUILayout.Label($"Connected : {(IsConnected ? "YES" : "NO")}");
            GUILayout.Label($"Mode      : {CurrentMode}");
            GUILayout.Label($"Motion    : {MotionState}");
            GUILayout.Label($"Frames Hz : {FrameRateHz:F1}");
            GUILayout.Label($"Seq       : {LastSeq}");
            GUILayout.Label($"Remote    : {(string.IsNullOrEmpty(LastRemoteEndpoint) ? "--" : LastRemoteEndpoint)}");
            GUILayout.Label($"Latency   : {LatencyMs:F1} ms");
            GUILayout.Label($"Dropped   : {DroppedFrames}  OutOfOrder: {OutOfOrderFrames}");
            GUILayout.Label($"Last recv : {LastReceiveAgeMs:F1} ms ago");
            if (_latestForce != null && _latestForce.Length >= 6)
            {
                GUILayout.Label($"Force[N]  : {_latestForce[0]:F2}, {_latestForce[1]:F2}, {_latestForce[2]:F2}");
                GUILayout.Label($"Torque[Nm]: {_latestForce[3]:F2}, {_latestForce[4]:F2}, {_latestForce[5]:F2}");
            }
            GUILayout.Label($"Cmd       : {LastCommandStatus} {LastCommandError}");
            if (!string.IsNullOrEmpty(LastError))
            {
                GUILayout.Label($"Error     : {LastError}");
            }

            if (showDebugCommandButtons)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(new GUIContent("Idle", "切到 idle_stream：只接收/发送数据，不让真实臂执行来回测试。")))
                {
                    SetMode("idle_stream");
                }
                if (GUILayout.Button(new GUIContent("Mode1", "切到 mode1_test：真实臂会在测试位姿 A/B 之间来回运动，请确认安全空间。")))
                {
                    SetMode("mode1_test");
                }
                if (GUILayout.Button(new GUIContent("Ctrl", "切到 mode2_ctrl：停止模式一，等待 Unity 发送 MOVE_JOINT。")))
                {
                    SetMode("mode2_ctrl");
                }
                if (GUILayout.Button(new GUIContent("Halt", "发送 HALT：请求 DartStudio 停止当前运动。")))
                {
                    SendHalt();
                }
                GUILayout.EndHorizontal();
                if (!string.IsNullOrEmpty(GUI.tooltip))
                {
                    GUILayout.Label(GUI.tooltip);
                }
            }
            GUILayout.EndArea();
        }

    }
}
