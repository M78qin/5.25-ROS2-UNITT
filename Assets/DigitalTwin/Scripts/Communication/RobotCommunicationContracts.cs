namespace DigitalTwin
{
    public interface IRobotStateSource
    {
        RuntimeSourceKind Kind { get; }
        int QueuedFrameCount { get; }

        bool TryDequeueFrame(out RobotStateFrame frame);
        RobotSourceStatus GetStatus();
        SourceHealth GetHealth();
    }

    public interface IRobotCommandSink
    {
        RobotCommandResult SetMode(string mode);
        RobotCommandResult SendMoveJoint(float[] targetDeg);
        RobotCommandResult SendHalt();
        RobotCommandResult SendRawCommand(string json);
    }
}
