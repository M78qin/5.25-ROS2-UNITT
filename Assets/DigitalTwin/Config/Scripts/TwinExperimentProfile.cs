using UnityEngine;

namespace DigitalTwin
{
    [CreateAssetMenu(menuName = "Digital Twin/Profiles/Experiment Profile", fileName = "TwinExperimentProfile")]
    public sealed class TwinExperimentProfile : ScriptableObject
    {
        [Header("论文记录 / Paper Recorder")]
        [Tooltip("启用论文级记录器。主实验默认开启，输出 receive/apply/event CSV 和 session_manifest.json。")]
        [InspectorName("启用论文记录器")]
        public bool enablePaperRecorder = true;
        [Tooltip("记录 Unity 收到的原始状态帧到 unity_receive.csv。")]
        [InspectorName("记录接收帧")]
        public bool paperRecordReceiveFrames = true;
        [Tooltip("记录实际应用到 Live Robot 的帧到 unity_apply.csv。")]
        [InspectorName("记录应用帧")]
        public bool paperRecordApplyFrames = true;
        [Tooltip("启用实验指标追踪，例如 receive/apply/event 标记。")]
        [InspectorName("启用实验追踪")]
        public bool enableExperimentTracking = true;
        [Tooltip("启用实验 CSV 输出。关闭后仅保留运行态，不生成论文 CSV。")]
        [InspectorName("启用实验 CSV")]
        public bool enableExperimentCsv = true;
        [Tooltip("论文日志根目录。每次会话会自动生成日期、experiment_id 和 session_id 子目录。")]
        [InspectorName("论文日志根目录")]
        public string paperStorageRootDirectory = @"D:\Unity Projects\DART_R-data\PaperLogs";
        [Tooltip("论文记录器每批写入行数。数值过小会增加 IO，过大可能增加停止时 flush 时间。")]
        [InspectorName("论文批量写入行数")]
        [Min(1)] public int paperRecorderBatchSize = 500;
        [Tooltip("论文记录器强制 flush 间隔，单位 ms。")]
        [InspectorName("论文 Flush 间隔 ms")]
        [Min(1)] public int paperRecorderFlushIntervalMs = 500;
        [Tooltip("论文记录队列硬上限。超过后会触发保护逻辑，避免内存无限增长。")]
        [InspectorName("论文队列硬上限")]
        [Min(1)] public int paperQueueHardLimit = 100000;

        [Header("旧版记录 / Legacy Recorder")]
        [Tooltip("旧版 frames_*.csv 记录器总开关。当前论文主实验默认关闭，避免重复写盘。")]
        [InspectorName("启用旧版记录器")]
        public bool enableLegacyRecording;
        [Tooltip("是否写入 SQLite。当前默认关闭。")]
        [InspectorName("写入 SQLite")]
        public bool recordToSqlite;
        [Tooltip("旧版记录器是否写 CSV。仅在 enableLegacyRecording 开启时生效。")]
        [InspectorName("旧版写 CSV")]
        public bool recordToCsv = true;
        [Tooltip("Play Mode 时是否自动启动旧版 writer。当前建议关闭，由实验流程控制。")]
        [InspectorName("自动启动旧版 Writer")]
        public bool autoStartRecordingWriter;
        [Tooltip("旧版记录器是否记录所有收到的帧。关闭时可只记录应用帧。")]
        [InspectorName("旧版记录全部接收帧")]
        public bool recordAllReceivedFrames = true;
        [Tooltip("旧版记录器每批写入行数。")]
        [InspectorName("旧版批量写入行数")]
        [Min(1)] public int recorderBatchSize = 500;
        [Tooltip("旧版记录器强制 flush 间隔，单位 ms。")]
        [InspectorName("旧版 Flush 间隔 ms")]
        [Min(1)] public int recorderFlushIntervalMs = 500;
        [Tooltip("旧版记录队列软上限，用于告警。")]
        [InspectorName("旧版队列软上限")]
        [Min(1)] public int recordQueueSoftLimit = 20000;
        [Tooltip("旧版记录队列硬上限，用于保护内存。")]
        [InspectorName("旧版队列硬上限")]
        [Min(1)] public int recordQueueHardLimit = 100000;

        [Header("主实验热路径 / Main Paper Hot Path")]
        [Tooltip("主实验使用 DartStudio 数据源。默认开启。")]
        [InspectorName("Dart 数据源")]
        public bool dartSource = PaperExperimentDefaults.DartSource;
        [Tooltip("ROS2-like 双源实验预留开关。当前关闭。")]
        [InspectorName("ROS2-like 数据源")]
        public bool ros2LikeSource = PaperExperimentDefaults.Ros2LikeSource;
        [Tooltip("记录并应用关节角 joint_position。主实验默认开启。")]
        [InspectorName("关节角 joint_position")]
        public bool jointPosition = PaperExperimentDefaults.JointPosition;
        [Tooltip("关节速度通道。当前关闭，后续需要速度指标时再开启。")]
        [InspectorName("关节速度 joint_velocity")]
        public bool jointVelocity = PaperExperimentDefaults.JointVelocity;
        [Tooltip("关节力矩/effort 通道。当前关闭。")]
        [InspectorName("关节力矩 joint_effort")]
        public bool jointEffort = PaperExperimentDefaults.JointEffort;
        [Tooltip("六轴力/力矩 tool_force 通道。主实验默认开启。")]
        [InspectorName("六轴力 tool_force")]
        public bool toolForce = PaperExperimentDefaults.ToolForce;
        [Tooltip("TCP 位姿通道。当前关闭。")]
        [InspectorName("TCP 位姿 tcp_pose")]
        public bool tcpPose = PaperExperimentDefaults.TcpPose;
        [Tooltip("额外信号通道。当前关闭，避免无关字段增加热路径负载。")]
        [InspectorName("额外信号 extra_signals")]
        public bool extraSignals = PaperExperimentDefaults.ExtraSignals;

        private void OnValidate()
        {
            paperRecorderBatchSize = Mathf.Max(1, paperRecorderBatchSize);
            paperRecorderFlushIntervalMs = Mathf.Max(1, paperRecorderFlushIntervalMs);
            paperQueueHardLimit = Mathf.Max(1, paperQueueHardLimit);
            recorderBatchSize = Mathf.Max(1, recorderBatchSize);
            recorderFlushIntervalMs = Mathf.Max(1, recorderFlushIntervalMs);
            recordQueueSoftLimit = Mathf.Max(1, recordQueueSoftLimit);
            recordQueueHardLimit = Mathf.Max(recordQueueSoftLimit, recordQueueHardLimit);
        }

        public void ApplyTo(TwinRuntimeSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.EnablePaperRecorder = enablePaperRecorder;
            settings.PaperRecordReceiveFrames = paperRecordReceiveFrames;
            settings.PaperRecordApplyFrames = paperRecordApplyFrames;
            settings.EnableExperimentTracking = enableExperimentTracking;
            settings.EnableExperimentCsv = enableExperimentCsv;
            settings.PaperStorageRootDirectory = paperStorageRootDirectory;
            settings.PaperRecorderBatchSize = Mathf.Max(1, paperRecorderBatchSize);
            settings.PaperRecorderFlushIntervalMs = Mathf.Max(1, paperRecorderFlushIntervalMs);
            settings.PaperQueueHardLimit = Mathf.Max(1, paperQueueHardLimit);

            settings.EnableRecording = enableLegacyRecording;
            settings.RecordToSqlite = recordToSqlite;
            settings.RecordToCsv = recordToCsv;
            settings.AutoStartRecordingWriter = autoStartRecordingWriter;
            settings.RecordAllReceivedFrames = recordAllReceivedFrames;
            settings.RecorderBatchSize = Mathf.Max(1, recorderBatchSize);
            settings.RecorderFlushIntervalMs = Mathf.Max(1, recorderFlushIntervalMs);
            settings.RecordQueueSoftLimit = Mathf.Max(1, recordQueueSoftLimit);
            settings.RecordQueueHardLimit = Mathf.Max(settings.RecordQueueSoftLimit, recordQueueHardLimit);
        }
    }
}
