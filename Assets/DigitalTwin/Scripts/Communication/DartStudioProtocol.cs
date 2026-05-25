using System;
using UnityEngine;

namespace DigitalTwin
{
    internal sealed class StateFrameNormalizer
    {
        private RobotSignalSchema _schema;

        public void Initialize(RobotSignalSchema schema)
        {
            _schema = schema;
        }

        public bool TryFromDartJson(string json, string sourceName, long sequenceId, long receiveNs, long receiveWallMs, out RobotStateFrame frame, out string error)
        {
            frame = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Empty DartStudio packet.";
                return false;
            }

            DartStudioPacket packet;
            try
            {
                packet = JsonUtility.FromJson<DartStudioPacket>(json);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }

            if (packet == null)
            {
                error = "JsonUtility returned null.";
                return false;
            }

            double sourceTimestampMs = packet.ros_time_ns > 0
                ? packet.ros_time_ns / 1000000.0
                : (packet.ts_ms > 0d ? packet.ts_ms : packet.timestamp * 1000.0);
            double wallTimestampMs = packet.host_wall_ns > 0
                ? packet.host_wall_ns / 1000000.0
                : sourceTimestampMs;

            frame = new RobotStateFrame
            {
                SourceName = sourceName,
                SequenceId = json.Contains("\"seq\"") ? packet.seq : sequenceId,
                SourceTimestampSeconds = sourceTimestampMs > 0d ? sourceTimestampMs / 1000.0 : 0d,
                SourceTimestampMs = sourceTimestampMs,
                UnityReceiveTimestampNs = receiveNs,
                UnityReceiveWallMs = receiveWallMs,
                ClockSyncStatus = SystemClock.IsLikelySameWallClock(wallTimestampMs, receiveWallMs)
                    ? TwinClockSyncStatus.Synced
                    : TwinClockSyncStatus.Unsynced,
                Channel = packet.channel ?? string.Empty,
                Mode = packet.mode ?? string.Empty,
                MotionState = packet.motion ?? string.Empty,
                MotionError = packet.motion_error ?? string.Empty,
                RawPayload = json,
                SourceId = string.IsNullOrEmpty(packet.source_id) ? sourceName : packet.source_id,
                SourceSessionId = packet.session_id ?? string.Empty,
                RunId = packet.run_id ?? string.Empty,
                TrialId = packet.trial_id ?? string.Empty,
                RequestId = packet.req_id ?? string.Empty,
                ExperimentId = packet.experiment_id ?? string.Empty,
                PhaseId = packet.phase_id,
                SegmentId = packet.segment_id,
                StreamEnabled = packet.stream_enabled,
                RecordEnabled = packet.record_enabled,
                EventType = packet.event_type ?? string.Empty,
                Notes = packet.notes ?? string.Empty,
                Flags = RobotFrameFlags.Valid
            };

            if (frame.SourceTimestampSeconds <= 0d)
            {
                frame.Flags |= RobotFrameFlags.InvalidTimestamp;
            }

            ApplyJointStates(packet.joint_states, frame, packet.joint_units);
            ApplyTcpPose(packet.tcp_pose, frame);
            ApplyForce(packet.tool_force, frame);

            if (packet.host_wall_ns > 0)
            {
                frame.SendWallMs = packet.host_wall_ns / 1000000.0;
            }

            if (packet.monotonic_ns > 0)
            {
                frame.SendPerfNs = packet.monotonic_ns;
            }

            if (packet.exp != null)
            {
                if (string.IsNullOrEmpty(frame.ExperimentId))
                {
                    frame.ExperimentId = packet.session_id ?? string.Empty;
                }
                frame.ExperimentType = packet.exp.type ?? string.Empty;
                frame.SendPerfNs = packet.exp.send_perf_ns;
                frame.SendWallMs = packet.exp.send_wall_ms > 0d ? packet.exp.send_wall_ms : frame.SourceTimestampMs;
            }

            if ((frame.Flags & (RobotFrameFlags.HasJointPosition | RobotFrameFlags.HasForce | RobotFrameFlags.HasJointTorque)) == 0 && !frame.HasTcpPose)
            {
                frame.Flags |= RobotFrameFlags.InvalidSchema;
            }

            return true;
        }

        private void ApplyJointStates(DartJointStates jointStates, RobotStateFrame frame, string jointUnits)
        {
            if (jointStates == null)
            {
                return;
            }

            int count = ResolveJointCount(jointStates.position, jointStates.velocity, jointStates.effort);
            if (count <= 0)
            {
                return;
            }

            frame.JointPositionRad = new float[count];
            if (CopyJointArrayToRad(jointStates.position, jointStates.name, frame.JointPositionRad, frame, jointUnits))
            {
                frame.Flags |= RobotFrameFlags.HasJointPosition;
            }

            if (jointStates.velocity != null && jointStates.velocity.Length > 0)
            {
                frame.JointVelocityRad = new float[count];
                if (CopyJointArrayToRad(jointStates.velocity, jointStates.name, frame.JointVelocityRad, frame, jointUnits))
                {
                    frame.Flags |= RobotFrameFlags.HasJointVelocity;
                }
            }

            if (jointStates.effort != null && jointStates.effort.Length > 0)
            {
                frame.JointTorqueNm = new float[count];
                if (CopyJointArrayRaw(jointStates.effort, jointStates.name, frame.JointTorqueNm))
                {
                    frame.Flags |= RobotFrameFlags.HasJointTorque;
                }
            }
        }

        private void ApplyTcpPose(DartTcpPose tcpPose, RobotStateFrame frame)
        {
            if (tcpPose == null || tcpPose.position == null || tcpPose.orientation == null)
            {
                return;
            }

            frame.HasTcpPose = true;
            frame.TcpPositionMeters = new Vector3(
                (float)(tcpPose.position.x * 0.001),
                (float)(tcpPose.position.z * 0.001),
                (float)(tcpPose.position.y * 0.001));
            frame.TcpRotation = Quaternion.Euler(
                (float)tcpPose.orientation.rx,
                (float)(-tcpPose.orientation.rz),
                (float)tcpPose.orientation.ry);
        }

        private static void ApplyForce(DartToolForce toolForce, RobotStateFrame frame)
        {
            if (toolForce == null || toolForce.force == null || toolForce.torque == null)
            {
                return;
            }

            frame.ForceVector = new[]
            {
                (float)toolForce.force.x,
                (float)toolForce.force.y,
                (float)toolForce.force.z,
                (float)toolForce.torque.x,
                (float)toolForce.torque.y,
                (float)toolForce.torque.z
            };
            frame.Flags |= RobotFrameFlags.HasForce;
        }

        private int ResolveJointCount(params double[][] sources)
        {
            if (_schema != null && _schema.JointCount > 0)
            {
                return _schema.JointCount;
            }

            if (sources == null)
            {
                return 0;
            }

            for (int i = 0; i < sources.Length; i++)
            {
                if (sources[i] != null && sources[i].Length > 0)
                {
                    return sources[i].Length;
                }
            }

            return 0;
        }

        private bool CopyJointArrayToRad(double[] source, string[] names, float[] destination, RobotStateFrame frame, string jointUnits)
        {
            if (source == null || destination == null)
            {
                return false;
            }

            bool copied = false;
            int count = Math.Min(source.Length, destination.Length);
            for (int i = 0; i < count; i++)
            {
                int index = ResolveJointIndex(names, i, destination.Length);
                if (index < 0)
                {
                    frame.Flags |= RobotFrameFlags.InvalidSchema;
                    continue;
                }

                destination[index] = IsRadians(jointUnits) ? (float)source[i] : (float)source[i] * Mathf.Deg2Rad;
                copied = true;
            }

            return copied;
        }

        private static bool IsRadians(string units)
        {
            return !string.IsNullOrEmpty(units) &&
                   (string.Equals(units, "rad", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(units, "radian", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(units, "radians", StringComparison.OrdinalIgnoreCase));
        }

        private bool CopyJointArrayRaw(double[] source, string[] names, float[] destination)
        {
            if (source == null || destination == null)
            {
                return false;
            }

            bool copied = false;
            int count = Math.Min(source.Length, destination.Length);
            for (int i = 0; i < count; i++)
            {
                int index = ResolveJointIndex(names, i, destination.Length);
                if (index < 0)
                {
                    continue;
                }

                destination[index] = (float)source[i];
                copied = true;
            }

            return copied;
        }

        private int ResolveJointIndex(string[] names, int fallbackIndex, int maxCount)
        {
            string name = names != null && fallbackIndex < names.Length ? names[fallbackIndex] : string.Empty;
            if (_schema != null)
            {
                int resolved = _schema.ResolveJointIndex(name, fallbackIndex);
                return resolved >= 0 && resolved < maxCount ? resolved : -1;
            }

            return fallbackIndex >= 0 && fallbackIndex < maxCount ? fallbackIndex : -1;
        }
    }

    [Serializable] public sealed class DartVec3 { public double x, y, z; }
    [Serializable] public sealed class DartOrient { public double rx, ry, rz; }
    [Serializable] public sealed class DartJointStates { public string[] name; public double[] position; public double[] velocity; public double[] effort; public double[] external_effort; }
    [Serializable] public sealed class DartTcpPose { public string frame_id; public DartVec3 position; public DartOrient orientation; public int solution_space; }
    [Serializable] public sealed class DartToolForce { public string frame_id; public DartVec3 force; public DartVec3 torque; }
    [Serializable] public sealed class DartSampleTimestamps { public double joint; public double force; public double tcp; }
    [Serializable] public sealed class DartExperimentExp { public string type; public long send_perf_ns; public double send_wall_ms; }

    [Serializable]
    public sealed class DartStudioPacket
    {
        public string schema_version;
        public string experiment_id;
        public string session_id;
        public string run_id;
        public string trial_id;
        public string req_id;
        public string source_id;
        public string joint_units;
        public string tcp_units;
        public string force_units;
        public string stream_id;
        public long seq;
        public double ts_ms;
        public double timestamp;
        public long ros_time_ns;
        public long host_wall_ns;
        public long monotonic_ns;
        public string channel;
        public string mode;
        public string motion;
        public string motion_error;
        public int phase_id;
        public int segment_id;
        public bool stream_enabled;
        public bool record_enabled;
        public string event_type;
        public string notes;
        public DartSampleTimestamps sample_ts_ms;
        public DartJointStates joint_states;
        public DartTcpPose tcp_pose;
        public DartToolForce tool_force;
        public DartExperimentExp exp;
    }
}
