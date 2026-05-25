using System;
using System.Diagnostics;
using UnityEngine;

namespace DigitalTwin
{
    [Flags]
    public enum RobotFrameFlags
    {
        None = 0,
        Valid = 1 << 0,
        HasJointPosition = 1 << 1,
        HasJointVelocity = 1 << 2,
        HasJointTorque = 1 << 3,
        HasForce = 1 << 4,
        HasExtraSignals = 1 << 5,
        OutOfOrder = 1 << 6,
        Interpolated = 1 << 7,
        DroppedBefore = 1 << 8,
        Clamped = 1 << 9,
        InvalidSchema = 1 << 10,
        InvalidTimestamp = 1 << 11
    }

    public enum TwinClockSyncStatus
    {
        Unknown = 0,
        Synced = 1,
        Unsynced = 2
    }

    public enum FrameQuality
    {
        Normal = 0,
        Delayed = 1,
        Duplicated = 2,
        Interpolated = 3,
        Lost = 4
    }

    [Serializable]
    public sealed class RobotStateFrame
    {
        public string SourceName = string.Empty;
        public long SequenceId;
        public double SourceTimestampSeconds;
        public double SourceTimestampMs;
        public long UnityReceiveTimestampNs;
        public long UnityReceiveWallMs;
        public long UnityPublishTimestampNs;
        public long UnityPublishWallMs;
        public long UnityApplyTimestampNs;
        public long UnityApplyWallMs;
        public TwinClockSyncStatus ClockSyncStatus = TwinClockSyncStatus.Unsynced;
        public FrameQuality Quality = FrameQuality.Normal;
        public RobotFrameFlags Flags;
        public float[] JointPositionRad;
        public float[] JointVelocityRad;
        public float[] JointTorqueNm;
        public float[] ForceVector;
        public float[] ExtraSignals;
        public Vector3 TcpPositionMeters;
        public Quaternion TcpRotation = Quaternion.identity;
        public bool HasTcpPose;
        public string Channel = string.Empty;
        public string Mode = string.Empty;
        public string MotionState = string.Empty;
        public string MotionError = string.Empty;
        public string RawPayload = string.Empty;
        public string SourceId = string.Empty;
        public string SourceSessionId = string.Empty;
        public string RunId = string.Empty;
        public string TrialId = string.Empty;
        public string RequestId = string.Empty;

        // 实验元数据（仅实验模式下有值）
        public string ExperimentId = string.Empty;
        public string ExperimentType = string.Empty;   // "STATE" / "PING"
        public long SendPerfNs;                        // Source-process monotonic timestamp; never subtract from Unity SystemClock.
        public double SendWallMs;                      // Source Unix wall-clock send time, used for same-machine end-to-end metrics.
        public int PhaseId;
        public int SegmentId;
        public bool StreamEnabled;
        public bool RecordEnabled;
        public string EventType = string.Empty;
        public string Notes = string.Empty;

        public int JointCount => JointPositionRad == null ? 0 : JointPositionRad.Length;
        public bool IsValid => (Flags & RobotFrameFlags.Valid) != 0;

        public RobotStateFrame Clone()
        {
            return new RobotStateFrame
            {
                SourceName = SourceName,
                SequenceId = SequenceId,
                SourceTimestampSeconds = SourceTimestampSeconds,
                SourceTimestampMs = SourceTimestampMs,
                UnityReceiveTimestampNs = UnityReceiveTimestampNs,
                UnityReceiveWallMs = UnityReceiveWallMs,
                UnityPublishTimestampNs = UnityPublishTimestampNs,
                UnityPublishWallMs = UnityPublishWallMs,
                UnityApplyTimestampNs = UnityApplyTimestampNs,
                UnityApplyWallMs = UnityApplyWallMs,
                ClockSyncStatus = ClockSyncStatus,
                Quality = Quality,
                Flags = Flags,
                JointPositionRad = CloneArray(JointPositionRad),
                JointVelocityRad = CloneArray(JointVelocityRad),
                JointTorqueNm = CloneArray(JointTorqueNm),
                ForceVector = CloneArray(ForceVector),
                ExtraSignals = CloneArray(ExtraSignals),
                TcpPositionMeters = TcpPositionMeters,
                TcpRotation = TcpRotation,
                HasTcpPose = HasTcpPose,
                Channel = Channel,
                Mode = Mode,
                MotionState = MotionState,
                MotionError = MotionError,
                RawPayload = RawPayload,
                SourceId = SourceId,
                SourceSessionId = SourceSessionId,
                RunId = RunId,
                TrialId = TrialId,
                RequestId = RequestId,
                ExperimentId = ExperimentId,
                ExperimentType = ExperimentType,
                SendPerfNs = SendPerfNs,
                SendWallMs = SendWallMs,
                PhaseId = PhaseId,
                SegmentId = SegmentId,
                StreamEnabled = StreamEnabled,
                RecordEnabled = RecordEnabled,
                EventType = EventType,
                Notes = Notes
            };
        }

        public void MarkAppliedNow()
        {
            UnityApplyTimestampNs = SystemClock.NowNs();
            UnityApplyWallMs = SystemClock.UtcUnixMs();
        }

        private static float[] CloneArray(float[] source)
        {
            if (source == null)
            {
                return null;
            }

            float[] copy = new float[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }

    [Serializable]
    public sealed class MetricsSample
    {
        public string SourceName = string.Empty;
        public long SequenceId;
        public double ReceiveToApplyMs;
        public double UpdateIntervalMs;
        public double UpdateRateHz;
        public double JitterMs;
        public long DroppedFrameCount;
        public long OutOfOrderCount;
        public int RecordQueueLength;
    }

    public readonly struct RobotSourceStatus
    {
        public readonly string SourceName;
        public readonly bool IsConnected;
        public readonly string LastError;
        public readonly long LastReceiveTimestampNs;
        public readonly int QueuedFrameCount;

        public RobotSourceStatus(string sourceName, bool isConnected, string lastError, long lastReceiveTimestampNs, int queuedFrameCount)
        {
            SourceName = sourceName;
            IsConnected = isConnected;
            LastError = lastError;
            LastReceiveTimestampNs = lastReceiveTimestampNs;
            QueuedFrameCount = queuedFrameCount;
        }
    }

    public readonly struct SourceHealth
    {
        public readonly string SourceName;
        public readonly bool IsConnected;
        public readonly long LastReceiveTimestampNs;
        public readonly double LastFrameAgeMs;
        public readonly long ReceivedFrames;
        public readonly long DroppedFrames;
        public readonly double EstimatedHz;
        public readonly string LastError;

        public SourceHealth(
            string sourceName,
            bool isConnected,
            long lastReceiveTimestampNs,
            double lastFrameAgeMs,
            long receivedFrames,
            long droppedFrames,
            double estimatedHz,
            string lastError)
        {
            SourceName = sourceName ?? string.Empty;
            IsConnected = isConnected;
            LastReceiveTimestampNs = lastReceiveTimestampNs;
            LastFrameAgeMs = lastFrameAgeMs;
            ReceivedFrames = receivedFrames;
            DroppedFrames = droppedFrames;
            EstimatedHz = estimatedHz;
            LastError = lastError ?? string.Empty;
        }

        public static SourceHealth Offline(string sourceName, string lastError)
        {
            return new SourceHealth(sourceName, false, 0, -1d, 0, 0, 0d, lastError);
        }
    }

    public static class SystemClock
    {
        private static readonly double NanosecondsPerTick = 1000000000.0 / Stopwatch.Frequency;
        private static readonly long StartTimestamp = Stopwatch.GetTimestamp();

        public static long NowNs()
        {
            return (long)((Stopwatch.GetTimestamp() - StartTimestamp) * NanosecondsPerTick);
        }

        public static long UtcUnixMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

        public static long UtcUnixNs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * 1000000L;
        }

        public static double ElapsedMs(long startNs, long endNs)
        {
            return (endNs - startNs) / 1000000.0;
        }

        public static bool IsLikelySameWallClock(double sourceMs, long receiveWallMs, double maxAbsDeltaMs = 60000d)
        {
            if (sourceMs <= 0d || receiveWallMs <= 0)
            {
                return false;
            }

            return Math.Abs(receiveWallMs - sourceMs) <= maxAbsDeltaMs;
        }
    }

    internal readonly struct RawSourcePacket
    {
        public readonly string Payload;
        public readonly long ReceiveTimestampNs;
        public readonly long ReceiveWallMs;
        public readonly long SequenceId;

        public RawSourcePacket(string payload, long receiveTimestampNs, long receiveWallMs, long sequenceId)
        {
            Payload = payload;
            ReceiveTimestampNs = receiveTimestampNs;
            ReceiveWallMs = receiveWallMs;
            SequenceId = sequenceId;
        }
    }
}
