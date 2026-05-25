using System;
using System.Collections.Generic;
using UnityEngine;

namespace DigitalTwin
{
    public enum RuntimeSourceKind
    {
        None = 0,
        DartStudio = 1,
        Ros2 = 2,
        Mock = 3,
        Replay = 4
    }

    [DisallowMultipleComponent]
    public sealed class DigitalTwinRuntime : MonoBehaviour
    {
        [Header("Config / 配置")]
        [SerializeField, Tooltip("运行时总配置。控制数据源、UI、记录、安全开关等全局行为。")]
        private TwinRuntimeProfile profile;

        [SerializeField, Tooltip("机器人信号结构。用于把 DartStudio 发来的关节名/顺序映射到 Unity 内部关节顺序。")]
        private RobotSignalSchema schema;

        [Header("Scene Modules / 场景主模块")]
        [SerializeField, Tooltip("DartStudio 通信脚本。唯一负责 UDP 接收、心跳和 TCP 命令；建议挂在 Communication_System。")]
        private DartStudioBridge dartStudioBridge;

        [SerializeField, Tooltip("ROS2 通信脚本。负责 ROSConnection Topic 订阅/发布与状态帧转换。")]
        private Ros2Bridge ros2Bridge;

        [SerializeField, Tooltip("Recorded CSV replay source for reproducible experiments and demos.")]
        private ReplayStateSource replayStateSource;

        [SerializeField, Tooltip("机器人模型驱动器。Runtime 会把最新关节反馈推给它，用于 Live Robot 同步显示。")]
        private RobotModelController robotModel;

        [SerializeField, Tooltip("CSV/数据记录器。建议挂在 DataLogging_System；为空时不记录。")]
        private TwinRecorder recorder;

        [SerializeField, Tooltip("Independent paper-grade recorder for offline receive/apply metrics.")]
        private TwinPaperRecorder paperRecorder;

        [SerializeField, Tooltip("运行时 UI 控制器。建议挂在 UI_System；未绑定 UI 时会自动生成 Canvas 面板。")]
        private TwinUIController uiController;

        [SerializeField, Tooltip("双向控制命令控制器。建议挂在 Planning_Control_System；负责 Plan/Execute、安全门和真实命令出口。")]
        private TwinCommandController commandController;

        [Header("Runtime Sync / 实时同步")]
        [SerializeField, Tooltip("是否允许 DartStudio 最新反馈驱动 Live Robot。关闭后仍收包、记录、刷新 UI，但不更新模型姿态。")]
        private bool enableLiveRobotSync = true;

        [SerializeField, Tooltip("Live Robot 应用反馈帧的频率上限，单位 Hz。通常设为 60。")]
        private float robotApplyRateHz = 60f;

        [Header("Metrics / 指标")]
        [SerializeField, Tooltip("是否计算延迟、帧率、抖动、丢帧等运行指标。")]
        private bool enableMetrics = true;

        [SerializeField, Tooltip("指标刷新频率，单位 Hz。")]
        private float metricsRefreshRateHz = 5f;

        [Header("UI / 运行时界面")]
        [SerializeField, Tooltip("是否启用游戏运行时 UI 面板。")]
        private bool enableRuntimeUi = true;

        [SerializeField, Tooltip("UI 文本刷新频率，单位 Hz。")]
        private float uiRefreshRateHz = 10f;

        [SerializeField, Tooltip("关闭运行时 UI 时，是否周期打印源状态到 Console。")]
        private bool logStatusToConsoleWhenUiOff;

        [SerializeField, Tooltip("Console 状态日志频率，单位 Hz。建议低频，避免影响实时性能。")]
        private float consoleLogRateHz = 1f;

        [Header("Recording / 记录")]
        [SerializeField, Tooltip("是否把最新状态帧送入记录器。")]
        private bool enableRecording = true;
        [SerializeField, Tooltip("Record every received frame. Disable to record only frames that are applied to Live Robot.")]
        private bool recordAllReceivedFrames = true;
        private bool enablePaperRecorder;
        private bool enableExperimentTracking = true;
        private bool enableExperimentCsv = true;

        [Header("Performance / 性能")]
        [SerializeField, Tooltip("每个 Unity Update 最多从 DartStudioBridge 消费多少帧，防止主线程一次处理太久。")]
        private int maxSourceDrainPerFrame = 256;

        [SerializeField, Tooltip("模型显示是否只采用最新帧。开启后更适合实时数字孪生显示。")]
        private bool useLatestFrameOnly = true;

        [Header("Mock / 本地模拟")]
        [SerializeField, Tooltip("没有 DartStudio 时是否生成本地假关节数据。联调真实桥接时保持关闭。")]
        private bool enableMockSource;

        [SerializeField, Tooltip("Mock 数据频率，单位 Hz。")]
        private float mockFrequencyHz = 30f;

        [SerializeField, Tooltip("Mock 关节摆动幅度，单位 deg。")]
        private float mockAmplitudeDeg = 30f;

        private readonly RobotStateBus _bus = new RobotStateBus();
        private readonly Dictionary<long, RobotStateFrame> _pendingRecord = new Dictionary<long, RobotStateFrame>();
        private readonly List<IExperimentRecorder> _experimentRecorders = new List<IExperimentRecorder>();
        private readonly DigitalTwinModeService _modeService = new DigitalTwinModeService();
        private readonly FeatureSwitchService _featureSwitchService = new FeatureSwitchService();
        private TwinRuntimeSettings _settings = new TwinRuntimeSettings { UseDartStudio = true };
        private IRobotStateSource _activeSource;
        private TwinExperimentTracker _experimentTracker;
        private string _lastExperimentId = string.Empty;
        private long _mockSequenceId;
        private long _lastReceiveNs;
        private double _lastIntervalMs;
        private float _lastRobotApplyAt;
        private float _lastMetricsAt;
        private float _lastUiAt;
        private float _lastMockEmitAt;
        private float _lastConsoleLogAt;
        private MetricsSample _latestMetrics = new MetricsSample();
        private StateSnapshot _latestSnapshot = StateSnapshot.Empty();
        private RuntimeSourceKind _activeSourceKind = RuntimeSourceKind.None;
        private long _lastAppliedSequenceId = -1;
        private bool _dualSourceWarningLogged;
        private bool _sourceMissingWarningLogged;
        private bool _startupDebugLogged;

        public TwinRuntimeProfile Profile => profile;
        public TwinRuntimeSettings Settings => _settings;
        public RobotSignalSchema Schema => schema;
        public RobotModelController RobotModel => robotModel;
        public TwinCommandController CommandController => commandController;
        public DigitalTwinModeService ModeService => _modeService;
        public FeatureSwitchService FeatureSwitches => _featureSwitchService;
        public RobotStateFrame LatestFrame => _bus.LatestFrame;
        public MetricsSample LatestMetrics => _latestMetrics;
        public StateSnapshot LatestSnapshot => _latestSnapshot;
        public RuntimeSourceKind ActiveSourceKind => _activeSourceKind;
        public IRobotStateSource ActiveSource => _activeSource;
        public SourceHealth ActiveSourceHealth => _activeSource == null
            ? SourceHealth.Offline(_activeSourceKind.ToString(), "Active source is not ready.")
            : _activeSource.GetHealth();
        public RobotSourceStatus DartStudioStatus => dartStudioBridge != null
            ? dartStudioBridge.GetStatus()
            : new RobotSourceStatus("DartStudioBridge", false, "DartStudioBridge is not assigned.", 0, 0);
        public RobotSourceStatus Ros2Status => ros2Bridge != null
            ? ros2Bridge.GetStatus()
            : new RobotSourceStatus("ROS2Bridge", false, "ROS2Bridge is not assigned.", 0, 0);
        public DartStudioBridge DartBridge => dartStudioBridge;
        public Ros2Bridge Ros2Bridge => ros2Bridge;
        public ReplayStateSource ReplaySource => replayStateSource;
        public TwinRecorder Recorder => recorder;
        public TwinPaperRecorder PaperRecorder => paperRecorder;
        public bool HasLatestFrame => _bus.HasLatestFrame;

        private void Reset()
        {
            dartStudioBridge = GetComponent<DartStudioBridge>();
            if (dartStudioBridge == null) dartStudioBridge = FindObjectOfType<DartStudioBridge>();
            ros2Bridge = GetComponent<Ros2Bridge>();
            if (ros2Bridge == null) ros2Bridge = FindObjectOfType<Ros2Bridge>();
            replayStateSource = GetComponent<ReplayStateSource>();
            if (replayStateSource == null) replayStateSource = FindObjectOfType<ReplayStateSource>();
            robotModel = FindObjectOfType<RobotModelController>();
            recorder = GetComponent<TwinRecorder>();
            if (recorder == null) recorder = FindObjectOfType<TwinRecorder>();
            paperRecorder = GetComponent<TwinPaperRecorder>();
            if (paperRecorder == null) paperRecorder = FindObjectOfType<TwinPaperRecorder>();
            uiController = GetComponent<TwinUIController>();
            if (uiController == null) uiController = FindObjectOfType<TwinUIController>();
            commandController = GetComponent<TwinCommandController>();
            if (commandController == null) commandController = FindObjectOfType<TwinCommandController>();
        }

        private void OnValidate()
        {
            robotApplyRateHz = Mathf.Max(1f, robotApplyRateHz);
            metricsRefreshRateHz = Mathf.Max(0.1f, metricsRefreshRateHz);
            uiRefreshRateHz = Mathf.Max(0.1f, uiRefreshRateHz);
            consoleLogRateHz = Mathf.Max(0.1f, consoleLogRateHz);
            maxSourceDrainPerFrame = Mathf.Max(1, maxSourceDrainPerFrame);
            mockFrequencyHz = Mathf.Max(0.1f, mockFrequencyHz);
        }

        private void OnEnable()
        {
            ResetRuntimeSessionState();
            _settings = TwinRuntimeSettings.FromProfile(profile);
            ResolveModules();
            robotModel?.SetSignalSchema(schema);
            robotModel?.PrepareRuntimeStartupPose();

            ApplyProfileOverrides();
            if (!enablePaperRecorder)
            {
                paperRecorder = null;
            }

            _featureSwitchService.Initialize(_settings);
            _modeService.Initialize(_settings);
            if (_settings.UseDartStudio)
            {
                dartStudioBridge?.Initialize(profile, schema);
            }

            if (_settings.UseRos2)
            {
                ros2Bridge?.Initialize(profile, schema);
            }

            if (_settings.UseReplay)
            {
                replayStateSource?.Initialize(profile, schema);
            }

            ResolveActiveSource(forceLog: true);
            recorder?.Initialize(profile, schema);
            ConfigureExperimentRecorders();

            commandController?.Initialize(this);

            TwinExperimentTracker experimentTracker = null;
            if (enableExperimentTracking)
            {
                experimentTracker = GetComponent<TwinExperimentTracker>();
                if (experimentTracker == null)
                {
                    experimentTracker = FindObjectOfType<TwinExperimentTracker>();
                }
            }

            _experimentTracker = experimentTracker;

            if (enableExperimentTracking && experimentTracker != null)
            {
                dartStudioBridge?.SetExperimentTracker(experimentTracker);
            }
            else
            {
                dartStudioBridge?.SetExperimentTracker(null);
            }

            if (enableRuntimeUi)
            {
                if (uiController != null)
                {
                    uiController.enabled = true;
                    uiController.Initialize(this, recorder, commandController);
                    if (experimentTracker != null)
                    {
                        uiController.SetExperimentTracker(experimentTracker);
                    }
                }
            }
            else if (uiController != null)
            {
                uiController.enabled = false;
            }
        }

        private void ResetRuntimeSessionState()
        {
            _bus.Clear();
            _pendingRecord.Clear();
            _lastReceiveNs = 0;
            _lastIntervalMs = 0d;
            _lastRobotApplyAt = 0f;
            _lastMetricsAt = 0f;
            _lastUiAt = 0f;
            _lastMockEmitAt = 0f;
            _lastConsoleLogAt = 0f;
            _latestMetrics = new MetricsSample();
            _latestSnapshot = StateSnapshot.Empty();
            _activeSourceKind = RuntimeSourceKind.None;
            _activeSource = null;
            _lastAppliedSequenceId = -1;
            _dualSourceWarningLogged = false;
            _sourceMissingWarningLogged = false;
            _startupDebugLogged = false;
        }

        private void OnDisable()
        {
            paperRecorder?.Shutdown();
            _experimentRecorders.Clear();
            recorder?.Shutdown();
            _lastExperimentId = string.Empty;
        }

        private void OnDestroy()
        {
            paperRecorder?.Shutdown();
            _experimentRecorders.Clear();
            recorder?.Shutdown();
        }

        private void OnApplicationQuit()
        {
            paperRecorder?.Shutdown();
            _experimentRecorders.Clear();
            recorder?.Shutdown();
        }

        private void Update()
        {
            EmitMockFrameIfNeeded();
            DrainSourceFrames();
            _bus.Swap();

            if (enableLiveRobotSync && robotModel != null && ShouldTick(ref _lastRobotApplyAt, robotApplyRateHz))
            {
                ApplyLatestToRobot();
            }

            FlushStalePendingFrames();

            if (enableMetrics && ShouldTick(ref _lastMetricsAt, metricsRefreshRateHz))
            {
                UpdateMetrics();
            }

            if (enableRuntimeUi && uiController != null && ShouldTick(ref _lastUiAt, uiRefreshRateHz))
            {
                uiController.Refresh();
            }

            if (!enableRuntimeUi && logStatusToConsoleWhenUiOff && ShouldTick(ref _lastConsoleLogAt, consoleLogRateHz))
            {
                PrintConsoleStatus();
            }
        }

        public bool TryGetLatestFrame(out RobotStateFrame frame)
        {
            return _bus.TryGetLatest(out frame);
        }

        public void PublishFrame(RobotStateFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            _bus.Publish(frame);
        }

        private void ResolveModules()
        {
            if (dartStudioBridge == null)
            {
                bool wantDart = _settings == null || _settings.UseDartStudio;
                if (wantDart)
                {
                    dartStudioBridge = GetComponent<DartStudioBridge>();
                    if (dartStudioBridge == null) dartStudioBridge = FindObjectOfType<DartStudioBridge>();
                }
            }

            if (ros2Bridge == null)
            {
                bool wantRos2 = _settings != null && _settings.UseRos2;
                if (wantRos2)
                {
                    ros2Bridge = GetComponent<Ros2Bridge>();
                    if (ros2Bridge == null) ros2Bridge = FindObjectOfType<Ros2Bridge>();
                }
            }

            if (replayStateSource == null)
            {
                bool wantReplay = _settings != null && _settings.UseReplay;
                if (wantReplay)
                {
                    replayStateSource = GetComponent<ReplayStateSource>();
                    if (replayStateSource == null) replayStateSource = FindObjectOfType<ReplayStateSource>();
                    if (replayStateSource == null) replayStateSource = gameObject.AddComponent<ReplayStateSource>();
                }
            }

            if (robotModel == null)
            {
                robotModel = FindObjectOfType<RobotModelController>();
            }

            if (recorder == null)
            {
                bool wantRecorder = _settings == null || _settings.EnableRecording;
                if (wantRecorder)
                {
                    recorder = GetComponent<TwinRecorder>();
                    if (recorder == null) recorder = FindObjectOfType<TwinRecorder>();
                }
            }

            if (paperRecorder == null)
            {
                bool wantPaperRecorder = _settings != null && _settings.EnablePaperRecorder;
                if (wantPaperRecorder)
                {
                    paperRecorder = GetComponent<TwinPaperRecorder>();
                    if (paperRecorder == null) paperRecorder = FindObjectOfType<TwinPaperRecorder>();
                    if (paperRecorder == null) paperRecorder = gameObject.AddComponent<TwinPaperRecorder>();
                }
            }

            if (uiController == null)
            {
                bool wantUi = _settings == null || _settings.EnableRuntimeUi;
                if (wantUi)
                {
                    uiController = GetComponent<TwinUIController>();
                    if (uiController == null) uiController = FindObjectOfType<TwinUIController>();
                }
            }

            if (commandController == null)
            {
                commandController = GetComponent<TwinCommandController>();
                if (commandController == null) commandController = FindObjectOfType<TwinCommandController>();
            }
        }

        private void ApplyProfileOverrides()
        {
            if (_settings == null)
            {
                return;
            }

            enableLiveRobotSync = _settings.EnableLiveRobotSync;
            robotApplyRateHz = _settings.RobotApplyRateHz;
            enableMetrics = _settings.EnableMetrics;
            metricsRefreshRateHz = _settings.MetricsRefreshRateHz;
            enableRuntimeUi = _settings.EnableRuntimeUi;
            uiRefreshRateHz = _settings.UiRefreshRateHz;
            enableRecording = _settings.EnableRecording;
            recordAllReceivedFrames = _settings.RecordAllReceivedFrames;
            enablePaperRecorder = _settings.EnablePaperRecorder;
            enableExperimentTracking = _settings.EnableExperimentTracking;
            enableExperimentCsv = _settings.EnableExperimentCsv;
            logStatusToConsoleWhenUiOff = _settings.EnableVerboseLog;
            maxSourceDrainPerFrame = _settings.MaxSourceDrainPerFrame;
            useLatestFrameOnly = _settings.UseLatestFrameOnlyForRobotView;
            LogStartupDebug();
        }

        private void ConfigureExperimentRecorders()
        {
            _experimentRecorders.Clear();

            if (!enablePaperRecorder || paperRecorder == null)
            {
                return;
            }

            paperRecorder.Initialize(profile, schema);
            _experimentRecorders.Add(paperRecorder);
        }

        private void PostFrameReceivedToRecorders(RobotStateFrame frame)
        {
            for (int i = 0; i < _experimentRecorders.Count; i++)
            {
                _experimentRecorders[i].OnFrameReceived(frame);
            }
        }

        private void PostFrameAppliedToRecorders(RobotStateFrame frame)
        {
            for (int i = 0; i < _experimentRecorders.Count; i++)
            {
                _experimentRecorders[i].OnFrameApplied(frame);
            }
        }

        private int GetActiveSourceQueueCount()
        {
            return _activeSource == null ? 0 : _activeSource.QueuedFrameCount;
        }

        private int GetRecordQueueCount()
        {
            int count = recorder == null ? 0 : recorder.PendingCount;
            for (int i = 0; i < _experimentRecorders.Count; i++)
            {
                count += _experimentRecorders[i].PendingCount;
            }

            return count;
        }

        private void PrintConsoleStatus()
        {
            if (!_bus.TryGetLatest(out RobotStateFrame frame))
            {
                Debug.Log(
                    $"[DigitalTwinRuntime] src={_activeSourceKind} frame=none " +
                    $"activeQ={GetActiveSourceQueueCount()} dart={FormatStatus(DartStudioStatus)} ros2={FormatStatus(Ros2Status)}",
                    this);
                return;
            }

            string source = string.IsNullOrEmpty(frame.SourceName) ? _activeSourceKind.ToString() : frame.SourceName;
            string mode = string.IsNullOrEmpty(frame.Mode) ? "--" : frame.Mode;
            string motion = string.IsNullOrEmpty(frame.MotionState) ? "--" : frame.MotionState;
            double ageMs = frame.UnityReceiveTimestampNs > 0 ? SystemClock.ElapsedMs(frame.UnityReceiveTimestampNs, SystemClock.NowNs()) : -1d;
            Debug.Log(
                $"[DigitalTwinRuntime] src={source} active={_activeSourceKind} seq={frame.SequenceId} " +
                $"mode={mode} motion={motion} age={ageMs:F1}ms activeQ={GetActiveSourceQueueCount()} " +
                $"recordQ={GetRecordQueueCount()} drop={_bus.DroppedFrameCount} ooo={_bus.OutOfOrderCount} " +
                $"dart={FormatStatus(DartStudioStatus)} ros2={FormatStatus(Ros2Status)}",
                this);
        }

        private void LogStartupDebug()
        {
            if (_startupDebugLogged || _settings == null || !_settings.EnableVerboseLog)
            {
                return;
            }

            _startupDebugLogged = true;
            Debug.Log(
                $"[DigitalTwinRuntime] Profile sources: dart={_settings.UseDartStudio} " +
                $"ros2={_settings.UseRos2} replay={_settings.UseReplay} sqliteReplay={_settings.UseSqliteReplay} " +
                $"autoDart={_settings.AutoStartDartTransport} liveSync={_settings.EnableLiveRobotSync} " +
                $"recordAll={_settings.RecordAllReceivedFrames} maxDrain={_settings.MaxSourceDrainPerFrame} " +
                $"bindings: DartStudioBridge={(dartStudioBridge != null)} Ros2Bridge={(ros2Bridge != null)} Replay={(replayStateSource != null)}",
                this);
        }

        private static string FormatStatus(RobotSourceStatus status)
        {
            string error = string.IsNullOrEmpty(status.LastError) ? "" : $",err={status.LastError}";
            return $"{status.SourceName}:connected={status.IsConnected},q={status.QueuedFrameCount}{error}";
        }

        private void ResolveActiveSource(bool forceLog)
        {
            bool wantDart = _settings == null || _settings.UseDartStudio;
            bool wantRos2 = _settings != null && _settings.UseRos2;
            bool wantReplay = _settings != null && _settings.UseReplay;
            RuntimeSourceKind next = RuntimeSourceKind.None;
            IRobotStateSource nextSource = null;

            if (wantRos2 && wantDart && !_dualSourceWarningLogged)
            {
                _dualSourceWarningLogged = true;
                Debug.LogWarning("[DigitalTwinRuntime] Both DartStudio and ROS2 sources are enabled. Using ROS2 as active source.", this);
            }

            if (wantReplay && replayStateSource != null)
            {
                next = RuntimeSourceKind.Replay;
                nextSource = replayStateSource;
            }
            else if (wantRos2 && ros2Bridge != null)
            {
                next = RuntimeSourceKind.Ros2;
                nextSource = ros2Bridge;
            }
            else if (wantDart && dartStudioBridge != null)
            {
                next = RuntimeSourceKind.DartStudio;
                nextSource = dartStudioBridge;
            }
            else if (enableMockSource)
            {
                next = RuntimeSourceKind.Mock;
            }

            if (next == RuntimeSourceKind.None && !_sourceMissingWarningLogged && (wantDart || wantRos2))
            {
                _sourceMissingWarningLogged = true;
                Debug.LogWarning("[DigitalTwinRuntime] No active source bridge is assigned. Check DartStudioBridge/Ros2Bridge binding.", this);
            }

            if (next != _activeSourceKind || forceLog)
            {
                _activeSourceKind = next;
                _activeSource = nextSource;
                _latestSnapshot = StateSnapshot.Empty(_activeSourceKind);
                Debug.Log($"[DigitalTwinRuntime] Active source: {_activeSourceKind}", this);
            }
            else
            {
                _activeSource = nextSource;
            }
        }

        private void DrainSourceFrames()
        {
            if (_activeSource == null)
            {
                return;
            }

            int drained = 0;
            while (drained < maxSourceDrainPerFrame)
            {
                bool hasFrame = _activeSource.TryDequeueFrame(out RobotStateFrame frame);

                if (!hasFrame || frame == null)
                {
                    break;
                }

                FillFrameContext(frame);
                PublishFrame(frame);
                PostFrameReceivedToRecorders(frame);

                // Store for delayed recorder enqueue (apply timestamp set in ApplyLatestToRobot)
                if (recordAllReceivedFrames && (enableRecording || (recorder != null && recorder.IsRecording)) && recorder != null)
                {
                    _pendingRecord[frame.SequenceId] = frame;
                }

                drained++;
            }
        }

        private void ApplyLatestToRobot()
        {
            if (!_bus.TryGetLatest(out RobotStateFrame frame))
            {
                return;
            }

            if (frame.SequenceId == _lastAppliedSequenceId)
            {
                return;
            }

            if (!useLatestFrameOnly && frame.UnityApplyTimestampNs > 0)
            {
                return;
            }

            if (!robotModel.CanAcceptPoseSource(RobotModelController.PoseSource.RuntimeLive))
            {
                return;
            }

            if (robotModel.ApplyStateFrame(frame))
            {
                _lastAppliedSequenceId = frame.SequenceId;
                _bus.MarkApplied(frame.SequenceId, frame.UnityApplyTimestampNs);
                PostFrameAppliedToRecorders(frame);
                SwapSnapshot(frame, _latestMetrics);

                // Sync apply timestamp to pending recorder frame and enqueue
                if (recordAllReceivedFrames && _pendingRecord.TryGetValue(frame.SequenceId, out RobotStateFrame recFrame))
                {
                    recFrame.UnityApplyTimestampNs = frame.UnityApplyTimestampNs;
                    recFrame.UnityApplyWallMs = frame.UnityApplyWallMs;
                    recorder?.EnqueueFrame(recFrame);
                    EnqueueExperimentTimingIfNeeded(recFrame);
                    _pendingRecord.Remove(frame.SequenceId);
                }
                else if (!recordAllReceivedFrames && enableRecording && recorder != null)
                {
                    recorder.EnqueueFrame(frame);
                    EnqueueExperimentTimingIfNeeded(frame);
                }
            }
        }

        private void FlushStalePendingFrames()
        {
            if (!recordAllReceivedFrames || _pendingRecord.Count == 0)
            {
                return;
            }

            // Enqueue frames that will never be applied (superseded by newer frames)
            if (_bus.TryGetLatest(out RobotStateFrame latest))
            {
                List<long> stale = null;
                foreach (var kvp in _pendingRecord)
                {
                    if (kvp.Key < latest.SequenceId)
                    {
                        if (stale == null) stale = new List<long>();
                        stale.Add(kvp.Key);
                    }
                }

                if (stale != null)
                {
                    for (int i = 0; i < stale.Count; i++)
                    {
                        if (_pendingRecord.TryGetValue(stale[i], out RobotStateFrame f))
                        {
                            recorder?.EnqueueFrame(f);
                            EnqueueExperimentTimingIfNeeded(f);
                            _pendingRecord.Remove(stale[i]);
                        }
                    }
                }
            }
        }

        private void UpdateMetrics()
        {
            if (!_bus.TryGetLatest(out RobotStateFrame frame))
            {
                return;
            }

            MetricsSample sample = new MetricsSample
            {
                SourceName = frame.SourceName,
                SequenceId = frame.SequenceId,
                DroppedFrameCount = _bus.DroppedFrameCount,
                OutOfOrderCount = _bus.OutOfOrderCount,
                RecordQueueLength = GetRecordQueueCount()
            };

            if (frame.UnityApplyTimestampNs > 0)
            {
                sample.ReceiveToApplyMs = SystemClock.ElapsedMs(frame.UnityReceiveTimestampNs, frame.UnityApplyTimestampNs);
            }

            if (_lastReceiveNs > 0)
            {
                sample.UpdateIntervalMs = SystemClock.ElapsedMs(_lastReceiveNs, frame.UnityReceiveTimestampNs);
                sample.UpdateRateHz = sample.UpdateIntervalMs > 0.0001d ? 1000.0 / sample.UpdateIntervalMs : 0d;
                sample.JitterMs = _lastIntervalMs > 0d ? Math.Abs(sample.UpdateIntervalMs - _lastIntervalMs) : 0d;
                _lastIntervalMs = sample.UpdateIntervalMs;
            }

            _lastReceiveNs = frame.UnityReceiveTimestampNs;
            _latestMetrics = sample;
            SwapSnapshot(frame, sample);
        }

        private void SwapSnapshot(RobotStateFrame frame, MetricsSample sample)
        {
            _latestSnapshot = StateSnapshot.FromFrame(
                frame,
                _activeSourceKind,
                sample,
                GetActiveSourceQueueCount(),
                GetRecordQueueCount());
        }

        private void EnqueueExperimentTimingIfNeeded(RobotStateFrame frame)
        {
            if (!enableRecording || !enableExperimentCsv || recorder == null || frame == null ||
                string.IsNullOrEmpty(frame.ExperimentType) || string.IsNullOrEmpty(frame.ExperimentId))
            {
                return;
            }

            if (!frame.RecordEnabled && frame.SegmentId > 0)
            {
                return;
            }

            if (frame.ExperimentId != _lastExperimentId)
            {
                _lastExperimentId = frame.ExperimentId;
                recorder.StartExperimentLog(frame.ExperimentId, recorder.GetStorageDirectory());
            }

            recorder.EnqueueExperimentTiming(frame);
        }

        private void EmitMockFrameIfNeeded()
        {
            if (!enableMockSource)
            {
                return;
            }

            float interval = 1f / Mathf.Max(0.1f, mockFrequencyHz);
            if (Time.unscaledTime - _lastMockEmitAt < interval)
            {
                return;
            }

            _lastMockEmitAt = Time.unscaledTime;
            int count = ResolveJointCount();
            RobotStateFrame frame = new RobotStateFrame
            {
                SourceName = "Mock",
                SequenceId = ++_mockSequenceId,
                SourceTimestampSeconds = Time.realtimeSinceStartup,
                SourceTimestampMs = Time.realtimeSinceStartup * 1000.0,
                UnityReceiveTimestampNs = SystemClock.NowNs(),
                UnityReceiveWallMs = SystemClock.UtcUnixMs(),
                Flags = RobotFrameFlags.Valid | RobotFrameFlags.HasJointPosition,
                JointPositionRad = new float[count]
            };

            for (int i = 0; i < count; i++)
            {
                frame.JointPositionRad[i] = mockAmplitudeDeg * Mathf.Sin(Time.unscaledTime + i) * Mathf.Deg2Rad;
            }

            PublishFrame(frame);
            _bus.Swap();
            if (enableRecording)
            {
                recorder?.EnqueueFrame(frame);
            }
        }

        private void FillFrameContext(RobotStateFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(frame.RunId))
            {
                frame.RunId = string.IsNullOrEmpty(frame.ExperimentId) ? frame.SourceSessionId : frame.ExperimentId;
            }

            if (string.IsNullOrEmpty(frame.TrialId))
            {
                frame.TrialId = frame.SourceSessionId;
            }
        }

        private int ResolveJointCount()
        {
            if (schema != null && schema.JointCount > 0)
            {
                return schema.JointCount;
            }

            if (robotModel != null && robotModel.JointCount > 0)
            {
                return robotModel.JointCount;
            }

            return 6;
        }

        private static bool ShouldTick(ref float lastTickAt, float hz)
        {
            float interval = 1f / Mathf.Max(0.1f, hz);
            if (Time.unscaledTime - lastTickAt < interval)
            {
                return false;
            }

            lastTickAt = Time.unscaledTime;
            return true;
        }
    }
}
