namespace DigitalTwin
{
    public static class PaperExperimentDefaults
    {
        public const string DefaultExperimentId = "exp_manual";
        public const string DefaultMode = "manual";
        public const string DefaultSourceId = "dart";
        public const int DefaultRandomSeed = 42;
        public const float DefaultDartHz = 30f;
        public const float DefaultRos2Hz = 60f;

        public const bool DartSource = true;
        public const bool Ros2LikeSource = false;
        public const bool JointPosition = true;
        public const bool JointVelocity = false;
        public const bool JointEffort = false;
        public const bool ToolForce = true;
        public const bool TcpPose = false;
        public const bool ExtraSignals = false;

        public const string CommandEchoEvent = "COMMAND_ECHO";
    }
}
