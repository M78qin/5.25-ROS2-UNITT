using UnityEngine;

namespace DigitalTwin
{
    [CreateAssetMenu(menuName = "Digital Twin/Runtime Profile", fileName = "TwinRuntimeProfile")]
    public sealed class TwinRuntimeProfile : ScriptableObject
    {
        [Header("模块化子配置 / Modular Sub Profiles")]
        [Tooltip("系统配置：数据源、机器人显示、控制安全和性能。主实验通常只需要 Dart + Live Robot + Metrics。")]
        [InspectorName("系统配置")]
        public TwinSystemProfile systemProfile;
        [Tooltip("实验配置：论文记录器、旧版记录器和主实验通道开关。当前默认 joint_position + tool_force。")]
        [InspectorName("实验配置")]
        public TwinExperimentProfile experimentProfile;
        [Tooltip("显示配置：Runtime UI、调试面板和 Editor 调试工具。主实验默认关闭 UI/Debug。")]
        [InspectorName("显示与调试配置")]
        public TwinPresentationProfile presentationProfile;

        [Header("Sources / 数据源")]
        [Tooltip("最高优先级数据源开关。即使子 SystemProfile 设置不同，运行时也以这里为准。关闭后 DartStudioBridge 不会绑定 UDP，也不会发心跳。")]
        public bool enableDartStudioSource = true;
        [Tooltip("When false, Play Mode only initializes DartStudioBridge. ExperimentSessionController.Connect starts UDP state, heartbeat, and TCP command lifecycle.")]
        public bool autoStartDartTransport;
        [Tooltip("最高优先级 ROS2 数据源开关。即使子 SystemProfile 设置不同，运行时也以这里为准。")]
        public bool enableRos2Source;
        [Tooltip("最高优先级 SQLite 回放数据源开关。即使子 SystemProfile 设置不同，运行时也以这里为准。")]
        public bool enableSqliteReplaySource;
        [Tooltip("最高优先级 CSV 回放源开关。This is a real experiment replay source, not a mock source.")]
        public bool enableReplayStateSource;
        [Tooltip("Path to unity_receive.csv or unity_apply.csv for ReplayStateSource.")]
        public string replayCsvPath = string.Empty;
        [Tooltip("Replay output frequency in Hz when CSV timestamps are not used as the playback clock.")]
        [Min(1f)] public float replayHz = 30f;
        [Tooltip("Loop replay when the end of the CSV is reached.")]
        public bool replayLoop;

        [Header("Robot View / 机器人显示")]
        [Tooltip("是否允许真实/仿真反馈驱动 Unity Live Robot 姿态。关闭后仍会收包、记录和显示数据。")]
        public bool enableLiveRobotSync = true;
        [Tooltip("Ghost 机器人预留开关，本轮暂不实现完整 Ghost/IK 执行流程。")]
        public bool enableGhostRobot;
        [Tooltip("Live Robot 应用反馈帧的频率上限，单位 Hz。")]
        [Min(1f)] public float robotApplyRateHz = 60f;
        [Tooltip("是否只用最新帧驱动模型。实时数字孪生建议开启，避免显示落后。")]
        public bool useLatestFrameOnlyForRobotView = true;

        [Header("Command / 双向控制")]
        [Tooltip("双向控制总开关。关闭时 UI 可进入观察/测试，但 MOVE_JOINT 会被阻止。")]
        public bool enableBidirectionalControl;
        [Tooltip("真实机械臂命令开关。关闭时不会向 DartStudio 发送真实 MOVE_JOINT。")]
        public bool enableRealRobotCommand;
        [Tooltip("干跑模式。开启时只打印命令，不控制真实机械臂；第一次联调建议开启。")]
        public bool enableDryRun = true;
        [Tooltip("是否启用命令合法性检查，例如关节数量、范围、模式等。")]
        public bool enableCommandValidation = true;
        [Tooltip("执行前是否要求当前真实起点与规划起点足够接近。后续 Ghost/Plan 流程使用。")]
        public bool requireStartStateMatchBeforeExecute = true;
        [Tooltip("起点匹配容差，单位 deg。")]
        [Min(0f)] public float startStateToleranceDeg = 1f;

        [Header("Recording / 记录")]
        [Tooltip("是否启用记录器。开启后状态帧会进入 CSV/数据库队列。")]
        public bool enableRecording;
        [Tooltip("是否写 SQLite，当前默认关闭。")]
        public bool recordToSqlite;
        [Tooltip("是否写 CSV。论文指标分析建议开启。")]
        public bool recordToCsv = true;
        [Tooltip("When false, frames CSV writer starts lazily from ExperimentSessionController.StartRecord instead of on Play.")]
        public bool autoStartRecordingWriter;
        [Tooltip("Record every received frame. Disable to record only frames applied to Live Robot; useful for 200Hz realtime display tests.")]
        public bool recordAllReceivedFrames = true;
        [Tooltip("记录器每批最多写入多少帧。")]
        [Min(1)] public int recorderBatchSize = 500;
        [Tooltip("记录器强制 flush 间隔，单位 ms。")]
        [Min(1)] public int recorderFlushIntervalMs = 500;
        [Tooltip("记录队列软上限，超过后可用于 UI 告警。")]
        [Min(1)] public int recordQueueSoftLimit = 20000;
        [Tooltip("记录队列硬上限，超过后丢弃或停止接收的保护阈值。")]
        [Min(1)] public int recordQueueHardLimit = 100000;

        [Header("Paper Recording / 论文数据")]
        [Tooltip("Enable the independent paper-grade recorder. When false, runtime does not resolve or call it.")]
        public bool enablePaperRecorder = true;
        [Tooltip("Record received frames to unity_receive.csv during START_RECORD segments.")]
        public bool paperRecordReceiveFrames = true;
        [Tooltip("Record applied frames to unity_apply.csv during START_RECORD segments.")]
        public bool paperRecordApplyFrames = true;
        [Tooltip("Root directory for paper logs. Sessions are written under <root>/<MMddHHmm>/<experiment_id>/<session_id>.")]
        public string paperStorageRootDirectory = @"D:\Unity Projects\DART_R-data\PaperLogs";
        [Tooltip("Paper recorder rows written per batch.")]
        [Min(1)] public int paperRecorderBatchSize = 500;
        [Tooltip("Paper recorder forced flush interval in milliseconds.")]
        [Min(1)] public int paperRecorderFlushIntervalMs = 500;
        [Tooltip("Hard limit for queued paper rows before oldest rows are dropped.")]
        [Min(1)] public int paperQueueHardLimit = 100000;

        [Header("UI / 运行时界面")]
        [Tooltip("是否启用游戏运行时 UI。开启后 TwinUIController 会自动生成或刷新面板。")]
        public bool enableRuntimeUI;
        [Tooltip("UI 刷新频率，单位 Hz。")]
        [Min(0.1f)] public float uiRefreshRateHz = 10f;
        [Tooltip("是否显示关节角面板。")]
        public bool showJointPanel = true;
        [Tooltip("是否显示六轴力/力矩面板。")]
        public bool showForcePanel = true;
        [Tooltip("是否显示延迟、频率、抖动、丢帧指标面板。")]
        public bool showMetricsPanel = true;
        [Tooltip("是否显示 CSV 记录状态面板。")]
        public bool showRecordPanel = true;
        [Tooltip("是否显示 DartStudio 连接状态面板。")]
        public bool showConnectionPanel = true;

        [Header("Metrics / 指标")]
        [Tooltip("是否计算运行指标。")]
        public bool enableMetrics = true;
        [Tooltip("指标刷新频率，单位 Hz。")]
        [Min(0.1f)] public float metricsRefreshRateHz = 5f;
        [Tooltip("是否启用延迟指标。")]
        public bool enableLatencyMetrics = true;
        [Tooltip("是否启用丢帧、乱序等数据质量指标。")]
        public bool enableDataQualityMetrics = true;

        [Header("Performance / 性能")]
        [Tooltip("每帧最多消费 DartStudio 状态帧数量。")]
        [Min(1)] public int maxSourceDrainPerFrame = 256;
        [Tooltip("是否关闭热路径详细日志，避免影响实时性。")]
        public bool disableVerboseLogInHotPath = true;

        [Header("Debug / 调试")]
        [Tooltip("是否开启详细日志。频繁收包时不建议开启。")]
        public bool enableVerboseLog;
        [Tooltip("是否启用 Editor 关节调试工具。")]
        public bool enableEditorJointControl;
        [Tooltip("是否启用 Editor IK 调试工具。")]
        public bool enableEditorIkControl;
        [Tooltip("是否显示 DartStudioBridge 左上角 OnGUI 调试窗。")]
        public bool enableDebugOverlay;

        [Header("Experiment / 实验")]
        [Tooltip("是否启用实验指标追踪。关闭后不处理 exp 字段，不显示实验面板。")]
        public bool enableExperimentTracking = true;
        [Tooltip("是否启用实验专用 CSV 日志（ACK/RTT/关节误差）。")]
        public bool enableExperimentCsv = true;
        [Tooltip("是否显示实验状态面板。")]
        public bool showExperimentPanel = true;

        private void OnValidate()
        {
            robotApplyRateHz = Mathf.Max(1f, robotApplyRateHz);
            uiRefreshRateHz = Mathf.Max(0.1f, uiRefreshRateHz);
            metricsRefreshRateHz = Mathf.Max(0.1f, metricsRefreshRateHz);
            recorderBatchSize = Mathf.Max(1, recorderBatchSize);
            recorderFlushIntervalMs = Mathf.Max(1, recorderFlushIntervalMs);
            recordQueueSoftLimit = Mathf.Max(1, recordQueueSoftLimit);
            recordQueueHardLimit = Mathf.Max(recordQueueSoftLimit, recordQueueHardLimit);
            paperRecorderBatchSize = Mathf.Max(1, paperRecorderBatchSize);
            paperRecorderFlushIntervalMs = Mathf.Max(1, paperRecorderFlushIntervalMs);
            paperQueueHardLimit = Mathf.Max(1, paperQueueHardLimit);
            maxSourceDrainPerFrame = Mathf.Max(1, maxSourceDrainPerFrame);
            replayHz = Mathf.Max(1f, replayHz);
        }

        public TwinRuntimeSettings BuildRuntimeSettings()
        {
            TwinRuntimeSettings settings = new TwinRuntimeSettings();

            if (systemProfile == null) ApplyLegacySystemSettings(settings);
            else systemProfile.ApplyTo(settings);

            if (experimentProfile == null) ApplyLegacyExperimentSettings(settings);
            else experimentProfile.ApplyTo(settings);

            if (presentationProfile == null) ApplyLegacyPresentationSettings(settings);
            else presentationProfile.ApplyTo(settings);

            ApplyTopLevelSourceSettings(settings);
            return settings;
        }

        private void ApplyTopLevelSourceSettings(TwinRuntimeSettings settings)
        {
            settings.UseDartStudio = enableDartStudioSource;
            settings.AutoStartDartTransport = autoStartDartTransport;
            settings.UseRos2 = enableRos2Source;
            settings.UseSqliteReplay = enableSqliteReplaySource;
            settings.UseReplay = enableReplayStateSource;
            settings.ReplayCsvPath = replayCsvPath;
            settings.ReplayHz = Mathf.Max(1f, replayHz);
            settings.ReplayLoop = replayLoop;
        }

        private void ApplyLegacySystemSettings(TwinRuntimeSettings settings)
        {
            settings.UseDartStudio = enableDartStudioSource;
            settings.AutoStartDartTransport = autoStartDartTransport;
            settings.UseRos2 = enableRos2Source;
            settings.UseSqliteReplay = enableSqliteReplaySource;
            settings.UseReplay = enableReplayStateSource;
            settings.ReplayCsvPath = replayCsvPath;
            settings.ReplayHz = Mathf.Max(1f, replayHz);
            settings.ReplayLoop = replayLoop;
            settings.EnableLiveRobotSync = enableLiveRobotSync;
            settings.EnableGhostRobot = enableGhostRobot;
            settings.RobotApplyRateHz = Mathf.Max(1f, robotApplyRateHz);
            settings.UseLatestFrameOnlyForRobotView = useLatestFrameOnlyForRobotView;
            settings.EnableBidirectionalControl = enableBidirectionalControl;
            settings.EnableRealRobotCommand = enableRealRobotCommand;
            settings.EnableDryRun = enableDryRun;
            settings.EnableCommandValidation = enableCommandValidation;
            settings.RequireStartStateMatchBeforeExecute = requireStartStateMatchBeforeExecute;
            settings.StartStateToleranceDeg = Mathf.Max(0f, startStateToleranceDeg);
            settings.EnableMetrics = enableMetrics;
            settings.MetricsRefreshRateHz = Mathf.Max(0.1f, metricsRefreshRateHz);
            settings.EnableLatencyMetrics = enableLatencyMetrics;
            settings.EnableDataQualityMetrics = enableDataQualityMetrics;
            settings.MaxSourceDrainPerFrame = Mathf.Max(1, maxSourceDrainPerFrame);
            settings.DisableVerboseLogInHotPath = disableVerboseLogInHotPath;
        }

        private void ApplyLegacyExperimentSettings(TwinRuntimeSettings settings)
        {
            settings.EnableRecording = enableRecording;
            settings.RecordToSqlite = recordToSqlite;
            settings.RecordToCsv = recordToCsv;
            settings.AutoStartRecordingWriter = autoStartRecordingWriter;
            settings.RecordAllReceivedFrames = recordAllReceivedFrames;
            settings.RecorderBatchSize = Mathf.Max(1, recorderBatchSize);
            settings.RecorderFlushIntervalMs = Mathf.Max(1, recorderFlushIntervalMs);
            settings.RecordQueueSoftLimit = Mathf.Max(1, recordQueueSoftLimit);
            settings.RecordQueueHardLimit = Mathf.Max(settings.RecordQueueSoftLimit, recordQueueHardLimit);
            settings.EnablePaperRecorder = enablePaperRecorder;
            settings.PaperRecordReceiveFrames = paperRecordReceiveFrames;
            settings.PaperRecordApplyFrames = paperRecordApplyFrames;
            settings.EnableExperimentTracking = enableExperimentTracking;
            settings.EnableExperimentCsv = enableExperimentCsv;
            settings.PaperStorageRootDirectory = paperStorageRootDirectory;
            settings.PaperRecorderBatchSize = Mathf.Max(1, paperRecorderBatchSize);
            settings.PaperRecorderFlushIntervalMs = Mathf.Max(1, paperRecorderFlushIntervalMs);
            settings.PaperQueueHardLimit = Mathf.Max(1, paperQueueHardLimit);
        }

        private void ApplyLegacyPresentationSettings(TwinRuntimeSettings settings)
        {
            settings.EnableRuntimeUi = enableRuntimeUI;
            settings.UiRefreshRateHz = Mathf.Max(0.1f, uiRefreshRateHz);
            settings.ShowJointPanel = showJointPanel;
            settings.ShowForcePanel = showForcePanel;
            settings.ShowMetricsPanel = showMetricsPanel;
            settings.ShowRecordPanel = showRecordPanel;
            settings.ShowConnectionPanel = showConnectionPanel;
            settings.ShowExperimentPanel = showExperimentPanel;
            settings.EnableVerboseLog = enableVerboseLog;
            settings.EnableEditorJointControl = enableEditorJointControl;
            settings.EnableEditorIkControl = enableEditorIkControl;
            settings.EnableDebugOverlay = enableDebugOverlay;
        }

        public void RefreshLegacyFieldsFromSubProfiles(TwinRuntimeSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            enableDartStudioSource = settings.UseDartStudio;
            autoStartDartTransport = settings.AutoStartDartTransport;
            enableRos2Source = settings.UseRos2;
            enableSqliteReplaySource = settings.UseSqliteReplay;
            enableReplayStateSource = settings.UseReplay;
            replayCsvPath = settings.ReplayCsvPath;
            replayHz = settings.ReplayHz;
            replayLoop = settings.ReplayLoop;

            enableLiveRobotSync = settings.EnableLiveRobotSync;
            enableGhostRobot = settings.EnableGhostRobot;
            robotApplyRateHz = settings.RobotApplyRateHz;
            useLatestFrameOnlyForRobotView = settings.UseLatestFrameOnlyForRobotView;

            enableBidirectionalControl = settings.EnableBidirectionalControl;
            enableRealRobotCommand = settings.EnableRealRobotCommand;
            enableDryRun = settings.EnableDryRun;
            enableCommandValidation = settings.EnableCommandValidation;
            requireStartStateMatchBeforeExecute = settings.RequireStartStateMatchBeforeExecute;
            startStateToleranceDeg = settings.StartStateToleranceDeg;

            enableRecording = settings.EnableRecording;
            recordToSqlite = settings.RecordToSqlite;
            recordToCsv = settings.RecordToCsv;
            autoStartRecordingWriter = settings.AutoStartRecordingWriter;
            recordAllReceivedFrames = settings.RecordAllReceivedFrames;
            recorderBatchSize = settings.RecorderBatchSize;
            recorderFlushIntervalMs = settings.RecorderFlushIntervalMs;
            recordQueueSoftLimit = settings.RecordQueueSoftLimit;
            recordQueueHardLimit = settings.RecordQueueHardLimit;

            enablePaperRecorder = settings.EnablePaperRecorder;
            paperRecordReceiveFrames = settings.PaperRecordReceiveFrames;
            paperRecordApplyFrames = settings.PaperRecordApplyFrames;
            enableExperimentTracking = settings.EnableExperimentTracking;
            enableExperimentCsv = settings.EnableExperimentCsv;
            paperStorageRootDirectory = settings.PaperStorageRootDirectory;
            paperRecorderBatchSize = settings.PaperRecorderBatchSize;
            paperRecorderFlushIntervalMs = settings.PaperRecorderFlushIntervalMs;
            paperQueueHardLimit = settings.PaperQueueHardLimit;

            enableRuntimeUI = settings.EnableRuntimeUi;
            uiRefreshRateHz = settings.UiRefreshRateHz;
            showJointPanel = settings.ShowJointPanel;
            showForcePanel = settings.ShowForcePanel;
            showMetricsPanel = settings.ShowMetricsPanel;
            showRecordPanel = settings.ShowRecordPanel;
            showConnectionPanel = settings.ShowConnectionPanel;
            showExperimentPanel = settings.ShowExperimentPanel;
            enableVerboseLog = settings.EnableVerboseLog;
            enableEditorJointControl = settings.EnableEditorJointControl;
            enableEditorIkControl = settings.EnableEditorIkControl;
            enableDebugOverlay = settings.EnableDebugOverlay;

            enableMetrics = settings.EnableMetrics;
            metricsRefreshRateHz = settings.MetricsRefreshRateHz;
            enableLatencyMetrics = settings.EnableLatencyMetrics;
            enableDataQualityMetrics = settings.EnableDataQualityMetrics;
            maxSourceDrainPerFrame = settings.MaxSourceDrainPerFrame;
            disableVerboseLogInHotPath = settings.DisableVerboseLogInHotPath;
        }
    }
}
