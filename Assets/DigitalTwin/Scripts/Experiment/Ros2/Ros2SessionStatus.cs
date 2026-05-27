using System;

namespace DigitalTwin
{
    /// <summary>Payload from WSL2 /dt/status/session (JSON).</summary>
    [Serializable]
    public sealed class Ros2SessionStatus
    {
        public bool ok;
        public bool stream_enabled;
        public bool record_enabled;
        public bool unity_record_requested;
        public bool wsl2_record_armed;
        public bool recording_active;
        public string session_id = string.Empty;
        public string experiment_id = string.Empty;
        public string action = string.Empty;
        public string error = string.Empty;
    }
}
