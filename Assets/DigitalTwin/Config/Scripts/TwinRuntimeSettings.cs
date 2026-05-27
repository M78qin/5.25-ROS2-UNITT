namespace DigitalTwin
{
    public sealed class TwinRuntimeSettings
    {
        public bool UseDartStudio;
        public bool AutoStartDartTransport;
        public bool AutoStartRos2Transport;
        public bool UseRos2;
        public bool UseSqliteReplay;
        public bool UseReplay;
        public string ReplayCsvPath = string.Empty;
        public float ReplayHz = 30f;
        public bool ReplayLoop;

        public bool EnableLiveRobotSync = true;
        public bool EnableGhostRobot;
        public float RobotApplyRateHz = 60f;
        public bool UseLatestFrameOnlyForRobotView = true;

        public bool EnableBidirectionalControl;
        public bool EnableRealRobotCommand;
        public bool EnableDryRun = true;
        public bool EnableCommandValidation = true;
        public bool RequireStartStateMatchBeforeExecute = true;
        public float StartStateToleranceDeg = 1f;

        public bool EnableRecording;
        public bool RecordToSqlite;
        public bool RecordToCsv = true;
        public bool AutoStartRecordingWriter;
        public bool RecordAllReceivedFrames = true;
        public int RecorderBatchSize = 500;
        public int RecorderFlushIntervalMs = 500;
        public int RecordQueueSoftLimit = 20000;
        public int RecordQueueHardLimit = 100000;

        public bool EnablePaperRecorder = true;
        public bool PaperRecordReceiveFrames = true;
        public bool PaperRecordApplyFrames = true;
        public bool EnableExperimentTracking = true;
        public bool EnableExperimentCsv = true;
        public string PaperStorageRootDirectory = @"D:\Unity Projects\DART_R-data\PaperLogs";
        public int PaperRecorderBatchSize = 500;
        public int PaperRecorderFlushIntervalMs = 500;
        public int PaperQueueHardLimit = 100000;

        public bool EnableRuntimeUi;
        public float UiRefreshRateHz = 10f;
        public bool ShowJointPanel = true;
        public bool ShowForcePanel = true;
        public bool ShowMetricsPanel = true;
        public bool ShowRecordPanel = true;
        public bool ShowConnectionPanel = true;
        public bool ShowExperimentPanel = true;
        public bool EnableVerboseLog;
        public bool EnableEditorJointControl;
        public bool EnableEditorIkControl;
        public bool EnableDebugOverlay;

        public bool EnableMetrics = true;
        public float MetricsRefreshRateHz = 5f;
        public bool EnableLatencyMetrics = true;
        public bool EnableDataQualityMetrics = true;
        public int MaxSourceDrainPerFrame = 256;
        public bool DisableVerboseLogInHotPath = true;

        public static TwinRuntimeSettings FromProfile(TwinRuntimeProfile profile)
        {
            return profile == null
                ? new TwinRuntimeSettings
                {
                    UseDartStudio = true,
                    EnableRuntimeUi = true,
                    EnableRecording = true,
                    EnablePaperRecorder = false
                }
                : profile.BuildRuntimeSettings();
        }
    }
}
