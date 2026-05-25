using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace DigitalTwin
{
    [DisallowMultipleComponent]
    public sealed class TwinRecorder : MonoBehaviour
    {
        private const string DefaultCsvRootDirectory = @"D:\Unity Projects\DART_R-data";

        [Header("Recording")]
        [SerializeField] private bool enableRecording = true;
        [SerializeField] private bool enableCsv = true;
        [SerializeField] private bool enableExperimentCsv = true;
        [SerializeField] private bool enableSqlite;
        [SerializeField, Tooltip("When true, Play Mode starts frames CSV immediately. Keep false for manual experiment lifecycle.")]
        private bool autoStartWriter;
        [SerializeField] private string storageDirectory = DefaultCsvRootDirectory;
        [SerializeField] private string sessionName = "session";
        [SerializeField] private int batchSize = 500;
        [SerializeField] private int flushIntervalMs = 500;
        [SerializeField] private int queueSoftLimit = 20000;
        [SerializeField] private int queueHardLimit = 100000;

        private readonly ConcurrentQueue<RobotStateFrame> _queue = new ConcurrentQueue<RobotStateFrame>();
        private Thread _writerThread;
        private volatile bool _running;
        private volatile bool _writerReady;
        private RobotSignalSchema _schema;
        private ExperimentSession _session;
        private string _resolvedStorageDirectory;
        private string _lastError = string.Empty;
        private long _droppedFrames;
        private readonly object _contextLock = new object();
        private bool _experimentSessionActive;
        private bool _sessionRecordEnabled = true;
        private bool _sessionStreamEnabled = true;
        private string _contextExperimentId = string.Empty;
        private string _contextSessionId = string.Empty;
        private int _contextPhaseId;
        private int _contextSegmentId;
        private string _contextEventType = string.Empty;
        private string _contextNotes = string.Empty;

        // 实验专用日志
        private readonly ConcurrentQueue<string> _expQueue = new ConcurrentQueue<string>();
        private Thread _expWriterThread;
        private volatile bool _expRunning;
        private string _expCsvPath = string.Empty;

        public int PendingCount => _queue.Count;
        public long DroppedFrameCount => Interlocked.Read(ref _droppedFrames);
        public bool IsRecording => _running;
        public bool CsvReady => _writerReady;
        public string LastError => _lastError;
        public string SessionId => _session == null ? string.Empty : _session.SessionId;
        public bool SessionRecordEnabled => !_experimentSessionActive || _sessionRecordEnabled;

        private void OnValidate()
        {
            batchSize = Mathf.Max(1, batchSize);
            flushIntervalMs = Mathf.Max(1, flushIntervalMs);
            queueSoftLimit = Mathf.Max(1, queueSoftLimit);
            queueHardLimit = Mathf.Max(queueSoftLimit, queueHardLimit);
            if (string.IsNullOrWhiteSpace(storageDirectory))
            {
                storageDirectory = DefaultCsvRootDirectory;
            }
        }

        private void OnDisable()
        {
            Shutdown();
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void OnApplicationQuit()
        {
            Shutdown();
        }

        public void Initialize(TwinRuntimeProfile profile, RobotSignalSchema schema)
        {
            Shutdown();
            _schema = schema;
            ApplyProfile(profile);
            Interlocked.Exchange(ref _droppedFrames, 0);
            while (_queue.TryDequeue(out _)) { }

            if (!enableRecording || (!enableCsv && !enableSqlite))
            {
                return;
            }

            _resolvedStorageDirectory = ResolveStorageDirectory();
            if (enableSqlite)
            {
                _lastError = "SQLite recorder is a placeholder in this simplified V1. Use CSV for this round.";
            }

            if (!enableCsv || !autoStartWriter)
            {
                return;
            }

            BeginRecordingSession(sessionName);
        }

        public bool BeginRecordingSession(string label = "")
        {
            if (_running)
            {
                return true;
            }

            if (!enableRecording || !enableCsv)
            {
                return false;
            }

            _session = new ExperimentSession(string.IsNullOrWhiteSpace(label) ? sessionName : label);
            _resolvedStorageDirectory = ResolveStorageDirectory();
            _running = true;
            _writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "DigitalTwin CSV Recorder"
            };
            _writerThread.Start();
            return true;
        }

        public bool ForceBeginRecordingSession(string label = "")
        {
            if (_running) return true;
            enableRecording = true;
            enableCsv = true;
            return BeginRecordingSession(label);
        }

        public void EnqueueFrame(RobotStateFrame frame)
        {
            if (!_running || frame == null)
            {
                return;
            }

            RobotStateFrame clone = frame.Clone();
            lock (_contextLock)
            {
                if (_experimentSessionActive)
                {
                    if (!_sessionRecordEnabled)
                    {
                        return;
                    }

                    if (string.IsNullOrEmpty(clone.ExperimentId))
                    {
                        clone.ExperimentId = _contextExperimentId;
                    }

                    if (string.IsNullOrEmpty(clone.SourceSessionId))
                    {
                        clone.SourceSessionId = _contextSessionId;
                    }

                    clone.PhaseId = clone.PhaseId != 0 ? clone.PhaseId : _contextPhaseId;
                    clone.SegmentId = clone.SegmentId != 0 ? clone.SegmentId : _contextSegmentId;
                    clone.StreamEnabled = clone.StreamEnabled || _sessionStreamEnabled;
                    clone.RecordEnabled = _sessionRecordEnabled;
                    if (string.IsNullOrEmpty(clone.EventType))
                    {
                        clone.EventType = _contextEventType;
                    }

                    if (string.IsNullOrEmpty(clone.Notes))
                    {
                        clone.Notes = _contextNotes;
                    }
                }
            }

            while (_queue.Count >= queueHardLimit && _queue.TryDequeue(out _))
            {
                Interlocked.Increment(ref _droppedFrames);
            }

            _queue.Enqueue(clone);
        }

        public void ConfigureExperimentSession(string experimentId, string sessionId, int phaseId, int segmentId, bool streamEnabled, bool recordEnabled, string eventType = "", string notes = "")
        {
            lock (_contextLock)
            {
                _experimentSessionActive = !string.IsNullOrEmpty(experimentId) || !string.IsNullOrEmpty(sessionId);
                _contextExperimentId = experimentId ?? string.Empty;
                _contextSessionId = sessionId ?? string.Empty;
                _contextPhaseId = Math.Max(0, phaseId);
                _contextSegmentId = Math.Max(0, segmentId);
                _sessionStreamEnabled = streamEnabled;
                _sessionRecordEnabled = recordEnabled;
                _contextEventType = eventType ?? string.Empty;
                _contextNotes = notes ?? string.Empty;
            }

            if (recordEnabled)
            {
                BeginRecordingSession(!string.IsNullOrEmpty(sessionId) ? sessionId : experimentId);
            }
        }

        public void SetSessionRecordEnabled(bool enabled, int segmentId)
        {
            lock (_contextLock)
            {
                _sessionRecordEnabled = enabled;
                _contextSegmentId = Math.Max(0, segmentId);
            }

            if (enabled)
            {
                BeginRecordingSession(!string.IsNullOrEmpty(_contextSessionId) ? _contextSessionId : _contextExperimentId);
            }
        }

        public void SetSessionStreamEnabled(bool enabled)
        {
            lock (_contextLock)
            {
                _sessionStreamEnabled = enabled;
            }
        }

        public void ClearExperimentSession()
        {
            lock (_contextLock)
            {
                _experimentSessionActive = false;
                _sessionRecordEnabled = true;
                _sessionStreamEnabled = true;
                _contextExperimentId = string.Empty;
                _contextSessionId = string.Empty;
                _contextPhaseId = 0;
                _contextSegmentId = 0;
                _contextEventType = string.Empty;
                _contextNotes = string.Empty;
            }
        }

        public string GetStorageDirectory()
        {
            return _resolvedStorageDirectory ?? DefaultCsvRootDirectory;
        }

        public void StartExperimentLog(string experimentId, string logDirectory)
        {
            StopExperimentLog();
            if (!enableExperimentCsv || string.IsNullOrEmpty(experimentId))
            {
                return;
            }

            Directory.CreateDirectory(logDirectory);
            _expCsvPath = Path.Combine(logDirectory, $"exp_ack_{experimentId}.csv");
            _expRunning = true;
            _expWriterThread = new Thread(ExpWriterLoop)
            {
                IsBackground = true,
                Name = "DigitalTwin Experiment CSV Recorder"
            };
            _expWriterThread.Start();
        }

        public void EnqueueExperimentTiming(RobotStateFrame frame)
        {
            if (!_expRunning || frame == null)
            {
                return;
            }

            double unityProcessMs = frame.UnityApplyTimestampNs > 0 && frame.UnityReceiveTimestampNs > 0
                ? SystemClock.ElapsedMs(frame.UnityReceiveTimestampNs, frame.UnityApplyTimestampNs)
                : 0d;
            double sourceWallMs = frame.SendWallMs > 0d ? frame.SendWallMs : frame.SourceTimestampMs;
            string row = string.Join(",",
                frame.SequenceId,
                sourceWallMs.ToString("0.###", CultureInfo.InvariantCulture),
                frame.UnityReceiveWallMs,
                frame.UnityApplyWallMs,
                unityProcessMs.ToString("0.###", CultureInfo.InvariantCulture),
                frame.SendPerfNs,
                frame.UnityReceiveTimestampNs,
                frame.UnityApplyTimestampNs,
                frame.ClockSyncStatus.ToString(),
                Escape(frame.ExperimentId),
                Escape(frame.ExperimentType),
                frame.PhaseId,
                frame.SegmentId,
                frame.StreamEnabled ? 1 : 0,
                frame.RecordEnabled ? 1 : 0,
                Escape(frame.EventType),
                Escape(frame.Notes));
            _expQueue.Enqueue(row);
        }

        public void EnqueueJointSyncError(long seq, double[] expectedDeg, double[] actualDeg)
        {
            if (!_expRunning || expectedDeg == null || actualDeg == null)
            {
                return;
            }

            int count = Math.Min(expectedDeg.Length, actualDeg.Length);
            double maxErr = 0;
            double sumSq = 0;
            StringBuilder sb = new StringBuilder();
            sb.Append(seq);
            for (int i = 0; i < count; i++)
            {
                double err = expectedDeg[i] - actualDeg[i];
                sb.Append(',').Append(err.ToString("0.####", CultureInfo.InvariantCulture));
                maxErr = Math.Max(maxErr, Math.Abs(err));
                sumSq += err * err;
            }

            double rms = count > 0 ? Math.Sqrt(sumSq / count) : 0;
            sb.Append(',').Append(maxErr.ToString("0.####", CultureInfo.InvariantCulture));
            sb.Append(',').Append(rms.ToString("0.####", CultureInfo.InvariantCulture));
            _expQueue.Enqueue(sb.ToString());
        }

        public void StopExperimentLog()
        {
            _expRunning = false;
            try
            {
                if (_expWriterThread != null && _expWriterThread.IsAlive)
                {
                    _expWriterThread.Join(1500);
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }

            _expWriterThread = null;
        }

        public void Shutdown()
        {
            StopExperimentLog();

            if (!_running && _writerThread == null)
            {
                return;
            }

            _running = false;
            try
            {
                if (_writerThread != null && _writerThread.IsAlive)
                {
                    _writerThread.Join(1500);
                }
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }

            _writerThread = null;
            _writerReady = false;
            _session?.End();
        }

        private void ApplyProfile(TwinRuntimeProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            TwinRuntimeSettings settings = profile.BuildRuntimeSettings();
            enableRecording = settings.EnableRecording;
            enableCsv = settings.RecordToCsv;
            enableExperimentCsv = settings.EnableExperimentCsv;
            enableSqlite = settings.RecordToSqlite;
            autoStartWriter = settings.AutoStartRecordingWriter;
            batchSize = settings.RecorderBatchSize;
            flushIntervalMs = settings.RecorderFlushIntervalMs;
            queueSoftLimit = settings.RecordQueueSoftLimit;
            queueHardLimit = settings.RecordQueueHardLimit;
        }

        private void WriterLoop()
        {
            StreamWriter writer = null;
            try
            {
                string root = _resolvedStorageDirectory;
                Directory.CreateDirectory(root);
                string path = Path.Combine(root, $"frames_{_session.SessionId}.csv");
                writer = new StreamWriter(path, false, new UTF8Encoding(true));
                writer.WriteLine(BuildHeader());
                _writerReady = true;

                DateTime lastFlush = DateTime.UtcNow;
                while (_running || !_queue.IsEmpty)
                {
                    int written = DrainBatch(writer, batchSize);
                    bool flushDue = (DateTime.UtcNow - lastFlush).TotalMilliseconds >= flushIntervalMs;
                    if (flushDue || written > 0 && _queue.IsEmpty)
                    {
                        writer.Flush();
                        lastFlush = DateTime.UtcNow;
                    }

                    if (written == 0)
                    {
                        Thread.Sleep(25);
                    }
                }

                writer.Flush();
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
            finally
            {
                try { writer?.Close(); } catch { }
                _writerReady = false;
            }
        }

        private void ExpWriterLoop()
        {
            StreamWriter writer = null;
            try
            {
                writer = new StreamWriter(_expCsvPath, false, new UTF8Encoding(true));
            writer.WriteLine("seq,source_wall_ms,unity_receive_wall_ms,unity_apply_wall_ms,unity_process_ms,send_perf_ns,unity_receive_ns,unity_apply_ns,clock_sync_status,experiment_id,experiment_type,phase_id,segment_id,stream_enabled,record_enabled,event_type,notes");

                DateTime lastFlush = DateTime.UtcNow;
                while (_expRunning || !_expQueue.IsEmpty)
                {
                    int written = 0;
                    while (written < 500 && _expQueue.TryDequeue(out string row))
                    {
                        writer.WriteLine(row);
                        written++;
                    }

                    bool flushDue = (DateTime.UtcNow - lastFlush).TotalMilliseconds >= 500;
                    if (flushDue || written > 0 && _expQueue.IsEmpty)
                    {
                        writer.Flush();
                        lastFlush = DateTime.UtcNow;
                    }

                    if (written == 0)
                    {
                        Thread.Sleep(25);
                    }
                }

                writer.Flush();
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
            finally
            {
                try { writer?.Close(); } catch { }
            }
        }

        private int DrainBatch(StreamWriter writer, int maxCount)
        {
            int written = 0;
            while (written < maxCount && _queue.TryDequeue(out RobotStateFrame frame))
            {
                writer.WriteLine(BuildRow(frame));
                written++;
            }

            return written;
        }

        private string ResolveStorageDirectory()
        {
            if (string.IsNullOrWhiteSpace(storageDirectory))
            {
                return DefaultCsvRootDirectory;
            }

            if (Path.IsPathRooted(storageDirectory))
            {
                return storageDirectory;
            }

            return Path.Combine(DefaultCsvRootDirectory, storageDirectory);
        }

        private string BuildHeader()
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("session_id,session_name,source,sequence,source_time_s,source_time_ms");
            builder.Append(",clock_sync_status,unity_receive_ns,unity_receive_wall_ms,unity_apply_ns,unity_apply_wall_ms");
            builder.Append(",flags,mode,motion,motion_error");
            int jointCount = ResolveJointCount(null);
            for (int i = 0; i < jointCount; i++)
            {
                string name = _schema != null && _schema.JointNames != null && i < _schema.JointNames.Length
                    ? _schema.JointNames[i]
                    : $"joint_{i + 1}";
                builder.Append(',').Append(Escape(name)).Append("_rad");
            }

            for (int i = 0; i < jointCount; i++)
            {
                string name = _schema != null && _schema.JointNames != null && i < _schema.JointNames.Length
                    ? _schema.JointNames[i]
                    : $"joint_{i + 1}";
                builder.Append(',').Append(Escape(name)).Append("_vel_rad_s");
            }

            for (int i = 0; i < jointCount; i++)
            {
                string name = _schema != null && _schema.JointNames != null && i < _schema.JointNames.Length
                    ? _schema.JointNames[i]
                    : $"joint_{i + 1}";
                builder.Append(',').Append(Escape(name)).Append("_torque_nm");
            }

            builder.Append(",force_x,force_y,force_z,torque_x,torque_y,torque_z,tcp_x,tcp_y,tcp_z,tcp_qx,tcp_qy,tcp_qz,tcp_qw");
            builder.Append(",payload_bytes,source_session_id,experiment_id,source_id,phase_id,segment_id,stream_enabled,record_enabled,event_type,notes");
            return builder.ToString();
        }

        private string BuildRow(RobotStateFrame frame)
        {
            // Use Python session_id if available, otherwise local GUID
            string sessionId = !string.IsNullOrEmpty(frame.ExperimentId)
                ? frame.ExperimentId
                : (!string.IsNullOrEmpty(frame.SourceSessionId) ? frame.SourceSessionId : _session.SessionId);

            StringBuilder builder = new StringBuilder();
            builder.Append(sessionId).Append(',');
            builder.Append(Escape(_session.SessionName)).Append(',');
            builder.Append(Escape(frame.SourceName)).Append(',');
            builder.Append(frame.SequenceId).Append(',');
            builder.Append(frame.SourceTimestampSeconds.ToString("0.######", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(frame.SourceTimestampMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            builder.Append(frame.ClockSyncStatus).Append(',');
            builder.Append(frame.UnityReceiveTimestampNs).Append(',');
            builder.Append(frame.UnityReceiveWallMs).Append(',');
            builder.Append(frame.UnityApplyTimestampNs).Append(',');
            builder.Append(frame.UnityApplyWallMs).Append(',');
            builder.Append((int)frame.Flags).Append(',');
            builder.Append(Escape(frame.Mode)).Append(',');
            builder.Append(Escape(frame.MotionState)).Append(',');
            builder.Append(Escape(frame.MotionError));

            int jointCount = ResolveJointCount(frame);
            for (int i = 0; i < jointCount; i++)
            {
                builder.Append(',');
                if (frame.JointPositionRad != null && i < frame.JointPositionRad.Length)
                {
                    builder.Append(frame.JointPositionRad[i].ToString("0.########", CultureInfo.InvariantCulture));
                }
            }

            for (int i = 0; i < jointCount; i++)
            {
                builder.Append(',');
                if (frame.JointVelocityRad != null && i < frame.JointVelocityRad.Length)
                {
                    builder.Append(frame.JointVelocityRad[i].ToString("0.########", CultureInfo.InvariantCulture));
                }
            }

            for (int i = 0; i < jointCount; i++)
            {
                builder.Append(',');
                if (frame.JointTorqueNm != null && i < frame.JointTorqueNm.Length)
                {
                    builder.Append(frame.JointTorqueNm[i].ToString("0.########", CultureInfo.InvariantCulture));
                }
            }

            for (int i = 0; i < 6; i++)
            {
                builder.Append(',');
                if (frame.ForceVector != null && i < frame.ForceVector.Length)
                {
                    builder.Append(frame.ForceVector[i].ToString("0.########", CultureInfo.InvariantCulture));
                }
            }

            for (int i = 0; i < 7; i++)
            {
                builder.Append(',');
                if (!frame.HasTcpPose)
                {
                    continue;
                }

                switch (i)
                {
                    case 0: builder.Append(frame.TcpPositionMeters.x.ToString("0.########", CultureInfo.InvariantCulture)); break;
                    case 1: builder.Append(frame.TcpPositionMeters.y.ToString("0.########", CultureInfo.InvariantCulture)); break;
                    case 2: builder.Append(frame.TcpPositionMeters.z.ToString("0.########", CultureInfo.InvariantCulture)); break;
                    case 3: builder.Append(frame.TcpRotation.x.ToString("0.########", CultureInfo.InvariantCulture)); break;
                    case 4: builder.Append(frame.TcpRotation.y.ToString("0.########", CultureInfo.InvariantCulture)); break;
                    case 5: builder.Append(frame.TcpRotation.z.ToString("0.########", CultureInfo.InvariantCulture)); break;
                    case 6: builder.Append(frame.TcpRotation.w.ToString("0.########", CultureInfo.InvariantCulture)); break;
                }
            }
            builder.Append(',').Append(frame.RawPayload?.Length ?? 0);
            builder.Append(',').Append(Escape(frame.SourceSessionId));
            builder.Append(',').Append(Escape(frame.ExperimentId));
            builder.Append(',').Append(Escape(frame.SourceId));
            builder.Append(',').Append(frame.PhaseId);
            builder.Append(',').Append(frame.SegmentId);
            builder.Append(',').Append(frame.StreamEnabled ? 1 : 0);
            builder.Append(',').Append(frame.RecordEnabled ? 1 : 0);
            builder.Append(',').Append(Escape(frame.EventType));
            builder.Append(',').Append(Escape(frame.Notes));
            return builder.ToString();
        }

        private int ResolveJointCount(RobotStateFrame frame)
        {
            if (_schema != null && _schema.JointCount > 0)
            {
                return _schema.JointCount;
            }

            if (frame != null && frame.JointPositionRad != null && frame.JointPositionRad.Length > 0)
            {
                return frame.JointPositionRad.Length;
            }

            return 6;
        }

        private static string Escape(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace(",", "_");
        }
    }

    [Serializable]
    public sealed class ExperimentSession
    {
        public string SessionId = Guid.NewGuid().ToString("N");
        public string SessionName = "session";
        public long StartUtcMs = SystemClock.UtcUnixMs();
        public long EndUtcMs;

        public ExperimentSession(string sessionName)
        {
            SessionName = string.IsNullOrWhiteSpace(sessionName) ? "session" : sessionName;
        }

        public void End()
        {
            EndUtcMs = SystemClock.UtcUnixMs();
        }
    }
}
