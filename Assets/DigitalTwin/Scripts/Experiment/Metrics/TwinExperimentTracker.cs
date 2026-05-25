using UnityEngine;

namespace DigitalTwin
{
    [DisallowMultipleComponent]
    public sealed class TwinExperimentTracker : MonoBehaviour
    {
        public bool IsExperimentActive { get; private set; }
        public string ExperimentId { get; private set; } = string.Empty;
        public long PacketsReceived { get; private set; }
        public long PongsSent { get; private set; }
        public long RttSampleCount { get; private set; }
        public double LastRttMs { get; private set; }
        public double MeanRttMs => 0d;
        public double P95RttMs => 0d;
        public double MaxRttMs => 0d;
        public double LastOneWayMs => 0d;
        public double[] LastJointErrorDeg => null;
        public double MaxJointErrorDeg => 0d;
        public double RmsJointErrorDeg => 0d;
        public string LastEventTag { get; private set; } = string.Empty;
        public string CurrentPhase { get; private set; } = string.Empty;
        public long LastEventTimestampNs { get; private set; }

        public void OnFrameReceived(RobotStateFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(frame.ExperimentType))
            {
                if (!IsExperimentActive)
                {
                    IsExperimentActive = true;
                }

                if (string.IsNullOrEmpty(ExperimentId) && !string.IsNullOrEmpty(frame.ExperimentId))
                {
                    ExperimentId = frame.ExperimentId;
                }

                PacketsReceived++;
            }
        }

        public void OnPongSent(long sendPerfNs, long recvPerfNs)
        {
            PongsSent++;
        }

        public void OnPongAckReceived(double rttMs)
        {
            if (rttMs <= 0)
            {
                return;
            }

            // Runtime keeps only raw facts for UI/debug. Paper metrics are computed by analyze.py.
            LastRttMs = rttMs;
            RttSampleCount++;
        }

        public void SetExperimentId(string experimentId)
        {
            ExperimentId = experimentId ?? string.Empty;
            IsExperimentActive = !string.IsNullOrEmpty(experimentId);
        }

        public void MarkEvent(string tag, long timestamp)
        {
            LastEventTag = tag ?? string.Empty;
            LastEventTimestampNs = timestamp;
            IsExperimentActive = true;
        }

        public void MarkPhase(string phase)
        {
            CurrentPhase = phase ?? string.Empty;
            MarkEvent("PHASE:" + CurrentPhase, SystemClock.NowNs());
        }

        public void UpdateJointError(double[] expectedDeg, double[] actualDeg)
        {
            MarkEvent("JOINT_ERROR_SAMPLE", SystemClock.NowNs());
        }

        public void Reset()
        {
            IsExperimentActive = false;
            ExperimentId = string.Empty;
            PacketsReceived = 0;
            PongsSent = 0;
            LastRttMs = 0;
            RttSampleCount = 0;
            LastEventTag = string.Empty;
            CurrentPhase = string.Empty;
            LastEventTimestampNs = 0;
        }
    }
}
