using System;
using UnityEngine;

namespace DigitalTwin
{
    internal sealed class RobotStateBus
    {
        private readonly object _bufferLock = new object();
        private RobotStateFrame _writeBuffer;
        private RobotStateFrame _readBuffer;
        private long _lastSequenceId = -1;
        private long _droppedFrameCount;
        private long _outOfOrderCount;

        public bool HasLatestFrame => _readBuffer != null;
        public RobotStateFrame LatestFrame
        {
            get
            {
                TryGetLatest(out RobotStateFrame frame);
                return frame;
            }
        }

        public long DroppedFrameCount => _droppedFrameCount;
        public long OutOfOrderCount => _outOfOrderCount;

        public void Publish(RobotStateFrame frame)
        {
            if (frame == null)
            {
                return;
            }

            // Detect session reset: large backward jump means source restarted.
            if (_lastSequenceId >= 0 && frame.SequenceId < _lastSequenceId - 1000)
            {
                _droppedFrameCount = 0;
                _outOfOrderCount = 0;
            }
            else if (_lastSequenceId >= 0 && frame.SequenceId <= _lastSequenceId)
            {
                frame.Flags |= RobotFrameFlags.OutOfOrder;
                frame.Quality = FrameQuality.Duplicated;
                _outOfOrderCount++;
            }
            else if (_lastSequenceId >= 0 && frame.SequenceId > _lastSequenceId + 1)
            {
                frame.Flags |= RobotFrameFlags.DroppedBefore;
                frame.Quality = FrameQuality.Delayed;
                _droppedFrameCount += frame.SequenceId - _lastSequenceId - 1;
            }

            if ((frame.Flags & RobotFrameFlags.Interpolated) != 0)
            {
                frame.Quality = FrameQuality.Interpolated;
            }

            _lastSequenceId = frame.SequenceId;
            frame.UnityPublishTimestampNs = SystemClock.NowNs();
            frame.UnityPublishWallMs = SystemClock.UtcUnixMs();
            lock (_bufferLock)
            {
                _writeBuffer = frame.Clone();
                if (_readBuffer == null)
                {
                    _readBuffer = _writeBuffer.Clone();
                }
            }
        }

        public bool Swap()
        {
            lock (_bufferLock)
            {
                if (_writeBuffer == null)
                {
                    return false;
                }

                if (_readBuffer != null && _readBuffer.SequenceId == _writeBuffer.SequenceId &&
                    _readBuffer.UnityPublishTimestampNs == _writeBuffer.UnityPublishTimestampNs)
                {
                    return false;
                }

                _readBuffer = _writeBuffer.Clone();
                return true;
            }
        }

        public bool TryGetLatest(out RobotStateFrame frame)
        {
            lock (_bufferLock)
            {
                frame = _readBuffer?.Clone();
            }

            return frame != null;
        }

        public void Clear()
        {
            lock (_bufferLock)
            {
                _writeBuffer = null;
                _readBuffer = null;
                _lastSequenceId = -1;
                _droppedFrameCount = 0;
                _outOfOrderCount = 0;
            }
        }

        public void MarkApplied(long sequenceId, long applyTimestampNs)
        {
            lock (_bufferLock)
            {
                if (_writeBuffer != null && _writeBuffer.SequenceId == sequenceId)
                {
                    _writeBuffer.UnityApplyTimestampNs = applyTimestampNs;
                    _writeBuffer.UnityApplyWallMs = SystemClock.UtcUnixMs();
                }

                if (_readBuffer != null && _readBuffer.SequenceId == sequenceId)
                {
                    _readBuffer.UnityApplyTimestampNs = applyTimestampNs;
                    _readBuffer.UnityApplyWallMs = SystemClock.UtcUnixMs();
                }
            }
        }
    }

    [Serializable]
    public sealed class StateSnapshot
    {
        public RuntimeSourceKind SourceKind;
        public string SourceName = string.Empty;
        public long SequenceId;
        public string Mode = string.Empty;
        public string MotionState = string.Empty;
        public string MotionError = string.Empty;
        public double ReceiveToApplyMs;
        public double ReceiveAgeMs;
        public double UpdateIntervalMs;
        public double UpdateRateHz;
        public double JitterMs;
        public long DroppedFrameCount;
        public long OutOfOrderCount;
        public int SourceQueueLength;
        public int RecordQueueLength;
        public long UnityReceiveTimestampNs;
        public long UnityApplyTimestampNs;
        public long UnityPublishTimestampNs;
        public long UnityReceiveWallMs;
        public long UnityApplyWallMs;
        public long UnityPublishWallMs;
        public TwinClockSyncStatus ClockSyncStatus;
        public FrameQuality Quality;
        public float[] JointPositionRad;
        public float[] JointPositionDeg;
        public float[] ForceVector;
        public Vector3 TcpPositionMeters;
        public Quaternion TcpRotation = Quaternion.identity;
        public bool HasTcpPose;
        public bool HasFrame;

        public static StateSnapshot Empty(RuntimeSourceKind sourceKind = RuntimeSourceKind.None)
        {
            return new StateSnapshot { SourceKind = sourceKind };
        }

        public static StateSnapshot FromFrame(
            RobotStateFrame frame,
            RuntimeSourceKind sourceKind,
            MetricsSample metrics,
            int sourceQueueLength,
            int recordQueueLength)
        {
            if (frame == null)
            {
                return Empty(sourceKind);
            }

            float[] jointRad = CloneArray(frame.JointPositionRad);
            float[] jointDeg = null;
            if (jointRad != null)
            {
                jointDeg = new float[jointRad.Length];
                for (int i = 0; i < jointRad.Length; i++)
                {
                    jointDeg[i] = jointRad[i] * Mathf.Rad2Deg;
                }
            }

            double receiveAgeMs = frame.UnityReceiveTimestampNs > 0
                ? SystemClock.ElapsedMs(frame.UnityReceiveTimestampNs, SystemClock.NowNs())
                : 0d;

            return new StateSnapshot
            {
                SourceKind = sourceKind,
                SourceName = frame.SourceName,
                SequenceId = frame.SequenceId,
                Mode = frame.Mode,
                MotionState = frame.MotionState,
                MotionError = frame.MotionError,
                ReceiveToApplyMs = metrics == null ? 0d : metrics.ReceiveToApplyMs,
                ReceiveAgeMs = receiveAgeMs,
                UpdateIntervalMs = metrics == null ? 0d : metrics.UpdateIntervalMs,
                UpdateRateHz = metrics == null ? 0d : metrics.UpdateRateHz,
                JitterMs = metrics == null ? 0d : metrics.JitterMs,
                DroppedFrameCount = metrics == null ? 0 : metrics.DroppedFrameCount,
                OutOfOrderCount = metrics == null ? 0 : metrics.OutOfOrderCount,
                SourceQueueLength = sourceQueueLength,
                RecordQueueLength = recordQueueLength,
                UnityReceiveTimestampNs = frame.UnityReceiveTimestampNs,
                UnityApplyTimestampNs = frame.UnityApplyTimestampNs,
                UnityPublishTimestampNs = frame.UnityPublishTimestampNs,
                UnityReceiveWallMs = frame.UnityReceiveWallMs,
                UnityApplyWallMs = frame.UnityApplyWallMs,
                UnityPublishWallMs = frame.UnityPublishWallMs,
                ClockSyncStatus = frame.ClockSyncStatus,
                Quality = frame.Quality,
                JointPositionRad = jointRad,
                JointPositionDeg = jointDeg,
                ForceVector = CloneArray(frame.ForceVector),
                TcpPositionMeters = frame.TcpPositionMeters,
                TcpRotation = frame.TcpRotation,
                HasTcpPose = frame.HasTcpPose,
                HasFrame = true
            };
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
}
