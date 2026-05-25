using UnityEngine;

namespace DigitalTwin
{
    [CreateAssetMenu(menuName = "Digital Twin/Profiles/System Profile", fileName = "TwinSystemProfile")]
    public sealed class TwinSystemProfile : ScriptableObject
    {
        [Header("数据源 / Sources")]
        [Tooltip("启用 DartStudio 作为主实时数据源。主实验默认开启。")]
        [InspectorName("启用 DartStudio 数据源")]
        public bool enableDartStudioSource = true;
        [Tooltip("Play Mode 启动时是否自动打开 DartStudio UDP/TCP 生命周期。关闭时由实验面板 Connect 手动启动，便于真机联调。")]
        [InspectorName("启动时自动打开 Dart 传输")]
        public bool autoStartDartTransport;
        [Tooltip("ROS2 数据源预留开关。当前主实验关闭，避免额外 socket 和队列进入热路径。")]
        [InspectorName("启用 ROS2 数据源")]
        public bool enableRos2Source;
        [Tooltip("SQLite 回放源预留开关。当前主实验关闭。")]
        [InspectorName("启用 SQLite 回放源")]
        public bool enableSqliteReplaySource;
        [Tooltip("CSV ReplayStateSource 回放开关。当前主实时链路关闭，只作为离线复现实验入口。")]
        [InspectorName("启用 CSV 回放源")]
        public bool enableReplayStateSource;
        [Tooltip("ReplayStateSource 使用的 unity_receive.csv 或 unity_apply.csv 路径。关闭回放时无需填写。")]
        [InspectorName("CSV 回放路径")]
        public string replayCsvPath = string.Empty;
        [Tooltip("CSV 无可用时间戳时的回放频率，单位 Hz。")]
        [InspectorName("CSV 回放频率 Hz")]
        [Min(1f)] public float replayHz = 30f;
        [Tooltip("CSV 回放到末尾后是否循环。")]
        [InspectorName("CSV 循环回放")]
        public bool replayLoop;

        [Header("机器人显示 / Robot View")]
        [Tooltip("允许真实/仿真反馈驱动 Unity Live Robot 姿态。主实验默认开启，用于验证实时同步。")]
        [InspectorName("启用 Live Robot 同步")]
        public bool enableLiveRobotSync = true;
        [Tooltip("Ghost Robot 预留开关。当前主实验关闭，避免 IK/Ghost 逻辑进入热路径。")]
        [InspectorName("启用 Ghost Robot")]
        public bool enableGhostRobot;
        [Tooltip("Live Robot 应用反馈帧的频率上限，单位 Hz。")]
        [InspectorName("机器人应用频率 Hz")]
        [Min(1f)] public float robotApplyRateHz = 60f;
        [Tooltip("只用最新状态帧驱动模型，避免显示端积压。实时通讯验证建议开启。")]
        [InspectorName("只使用最新帧")]
        public bool useLatestFrameOnlyForRobotView = true;

        [Header("控制安全 / Command Safety")]
        [Tooltip("双向控制总开关。当前先跑实时通讯和记录，默认关闭。")]
        [InspectorName("启用双向控制")]
        public bool enableBidirectionalControl;
        [Tooltip("是否允许向真实机械臂发送命令。真机命令实验前保持关闭。")]
        [InspectorName("允许真实机械臂命令")]
        public bool enableRealRobotCommand;
        [Tooltip("干跑模式。开启时只记录/回显控制意图，不真正驱动机械臂。")]
        [InspectorName("启用干跑模式")]
        public bool enableDryRun = true;
        [Tooltip("启用命令合法性检查，例如关节数量、范围和模式。")]
        [InspectorName("启用命令校验")]
        public bool enableCommandValidation = true;
        [Tooltip("执行前要求规划起点和当前反馈起点一致，后续真实控制实验使用。")]
        [InspectorName("执行前检查起点一致")]
        public bool requireStartStateMatchBeforeExecute = true;
        [Tooltip("起点匹配容差，单位 degree。")]
        [InspectorName("起点容差 deg")]
        [Min(0f)] public float startStateToleranceDeg = 1f;

        [Header("性能 / Performance")]
        [Tooltip("启用运行指标统计，例如频率、延迟、丢帧和数据质量。")]
        [InspectorName("启用运行指标")]
        public bool enableMetrics = true;
        [Tooltip("指标刷新频率，单位 Hz。过高会增加 UI/统计压力。")]
        [InspectorName("指标刷新频率 Hz")]
        [Min(0.1f)] public float metricsRefreshRateHz = 5f;
        [Tooltip("启用延迟指标统计。论文数据建议开启。")]
        [InspectorName("启用延迟指标")]
        public bool enableLatencyMetrics = true;
        [Tooltip("启用数据质量统计，例如丢帧、重复帧和乱序。论文数据建议开启。")]
        [InspectorName("启用数据质量指标")]
        public bool enableDataQualityMetrics = true;
        [Tooltip("每帧最多从数据源消费的状态帧数量。数值越大越不易积压，但单帧压力更高。")]
        [InspectorName("每帧最多消费帧数")]
        [Min(1)] public int maxSourceDrainPerFrame = 256;
        [Tooltip("关闭热路径详细日志，避免高频收包时日志拖慢实时性。")]
        [InspectorName("关闭热路径详细日志")]
        public bool disableVerboseLogInHotPath = true;

        private void OnValidate()
        {
            replayHz = Mathf.Max(1f, replayHz);
            robotApplyRateHz = Mathf.Max(1f, robotApplyRateHz);
            startStateToleranceDeg = Mathf.Max(0f, startStateToleranceDeg);
            metricsRefreshRateHz = Mathf.Max(0.1f, metricsRefreshRateHz);
            maxSourceDrainPerFrame = Mathf.Max(1, maxSourceDrainPerFrame);
        }

        public void ApplyTo(TwinRuntimeSettings settings)
        {
            if (settings == null)
            {
                return;
            }

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
    }
}
