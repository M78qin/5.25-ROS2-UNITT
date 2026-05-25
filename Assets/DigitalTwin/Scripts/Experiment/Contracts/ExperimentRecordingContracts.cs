namespace DigitalTwin
{
    public interface IExperimentRecorder
    {
        bool IsRecording { get; }
        int PendingCount { get; }
        string LastError { get; }

        void Initialize(TwinRuntimeProfile profile, RobotSignalSchema schema);
        void ConfigureSession(
            string experimentId,
            string sessionId,
            int phaseId,
            int segmentId,
            bool streamEnabled,
            bool recordEnabled,
            string eventType = "",
            string notes = "");
        void SetSessionRecordEnabled(bool enabled, int segmentId);
        void SetSessionStreamEnabled(bool enabled);
        void OnFrameReceived(RobotStateFrame frame);
        void OnFrameApplied(RobotStateFrame frame);
        void CloseSession();
    }
}
