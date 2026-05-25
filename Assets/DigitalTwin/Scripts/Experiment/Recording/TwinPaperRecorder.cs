using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DigitalTwin
{
    [DisallowMultipleComponent]
    public sealed class TwinPaperRecorder : MonoBehaviour, IExperimentRecorder
    {
        private const string DefaultRootDirectory = @"D:\Unity Projects\DART_R-data\PaperLogs";
        public const string SchemaVersion = "unity_paper_v2";

        [Header("Paper Recording")]
        [SerializeField] private bool enablePaperRecorder = true;
        [SerializeField] private bool recordReceiveFrames = true;
        [SerializeField] private bool recordApplyFrames = true;
        [SerializeField] private string storageRootDirectory = DefaultRootDirectory;
        [SerializeField] private int batchSize = 500;
        [SerializeField] private int flushIntervalMs = 500;
        [SerializeField] private int queueHardLimit = 100000;

        private readonly ConcurrentQueue<string> _receiveQueue = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _applyQueue = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _frameQueue = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _commandQueue = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _eventQueue = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _eventJsonQueue = new ConcurrentQueue<string>();
        private readonly object _contextLock = new object();

        private Thread _writerThread;
        private volatile bool _running;
        private volatile bool _ready;
        private TwinRuntimeProfile _profile;
        private RobotSignalSchema _schema;
        private string _sessionDirectory = string.Empty;
        private string _runStamp = string.Empty;
        private string _experimentId = string.Empty;
        private string _sessionId = string.Empty;
        private string _runId = string.Empty;
        private string _trialId = string.Empty;
        private int _phaseId;
        private int _segmentId;
        private bool _streamEnabled;
        private bool _recordEnabled;
        private string _lastError = string.Empty;
        private long _droppedRows;
        private long _pendingRows;
        private long _commandSeq;

        public bool IsReady => _ready;
        public bool IsRecording => _running;
        public bool RecordEnabled => _recordEnabled;
        public int PendingCount => (int)Math.Min(int.MaxValue, Math.Max(0, Interlocked.Read(ref _pendingRows)));
        public long DroppedRowCount => Interlocked.Read(ref _droppedRows);
        public string LastError => _lastError;
        public string SessionDirectory => _sessionDirectory;

        private void OnValidate()
        {
            batchSize = Mathf.Max(1, batchSize);
            flushIntervalMs = Mathf.Max(1, flushIntervalMs);
            queueHardLimit = Mathf.Max(1, queueHardLimit);
            if (string.IsNullOrWhiteSpace(storageRootDirectory))
            {
                storageRootDirectory = DefaultRootDirectory;
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
            _profile = profile;
            _schema = schema;
            ApplyProfile(profile);
            _runStamp = DateTime.Now.ToString("MMddHHmm", CultureInfo.InvariantCulture);
            Interlocked.Exchange(ref _droppedRows, 0);
            Interlocked.Exchange(ref _pendingRows, 0);
            Interlocked.Exchange(ref _commandSeq, 0);
            DrainAllQueues();
        }

        public void ConfigureSession(string experimentId, string sessionId, int phaseId, int segmentId, bool streamEnabled, bool recordEnabled, string eventType = "", string notes = "")
        {
            if (!enablePaperRecorder || string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            bool shouldBegin;
            lock (_contextLock)
            {
                shouldBegin = !_running || _sessionId != sessionId || _experimentId != experimentId;
                _experimentId = experimentId ?? string.Empty;
                _sessionId = sessionId ?? string.Empty;
                _runId = string.IsNullOrEmpty(_experimentId) ? _runStamp : _experimentId;
                _trialId = _sessionId;
                _phaseId = Math.Max(0, phaseId);
                _segmentId = Math.Max(0, segmentId);
                _streamEnabled = streamEnabled;
                _recordEnabled = recordEnabled;
            }

            if (shouldBegin)
            {
                BeginSession(experimentId, sessionId);
            }

            if (!string.IsNullOrEmpty(eventType) || !string.IsNullOrEmpty(notes))
            {
                EnqueueEvent(string.IsNullOrEmpty(eventType) ? "CONTEXT" : eventType, notes);
            }
        }

        public void SetSessionStreamEnabled(bool enabled)
        {
            lock (_contextLock)
            {
                _streamEnabled = enabled;
            }

            EnqueueEvent(enabled ? "STREAM_STARTED" : "STREAM_STOPPED", string.Empty);
        }

        public void SetSessionRecordEnabled(bool enabled, int segmentId)
        {
            lock (_contextLock)
            {
                _recordEnabled = enabled;
                _segmentId = Math.Max(0, segmentId);
            }

            EnqueueEvent(enabled ? "RECORD_STARTED" : "RECORD_STOPPED", string.Empty);
        }

        public void ClearSession()
        {
            EnqueueEvent("SESSION_CLEARED", string.Empty);
            lock (_contextLock)
            {
                _experimentId = string.Empty;
                _sessionId = string.Empty;
                _runId = string.Empty;
                _trialId = string.Empty;
                _phaseId = 0;
                _segmentId = 0;
                _streamEnabled = false;
                _recordEnabled = false;
            }
        }

        public void CloseSession()
        {
            EnqueueEvent("SESSION_CLOSED", string.Empty);
            Shutdown();
            lock (_contextLock)
            {
                _experimentId = string.Empty;
                _sessionId = string.Empty;
                _runId = string.Empty;
                _trialId = string.Empty;
                _phaseId = 0;
                _segmentId = 0;
                _streamEnabled = false;
                _recordEnabled = false;
            }
        }

        public void OnFrameReceived(RobotStateFrame frame)
        {
            if (!enablePaperRecorder || !_running || !recordReceiveFrames || frame == null)
            {
                return;
            }

            if (!CanWriteHighFrequency())
            {
                return;
            }

            EnqueueBounded(_receiveQueue, BuildFrameRow(frame, includeApply: false));
            EnqueueBounded(_frameQueue, BuildUnifiedFrameRow(frame, "receive"));
        }

        public void OnFrameApplied(RobotStateFrame frame)
        {
            if (!enablePaperRecorder || !_running || !recordApplyFrames || frame == null)
            {
                return;
            }

            if (!CanWriteHighFrequency())
            {
                return;
            }

            EnqueueBounded(_applyQueue, BuildFrameRow(frame, includeApply: true));
            EnqueueBounded(_frameQueue, BuildUnifiedFrameRow(frame, "apply"));
        }

        public void RecordCommand(string action, string route, string reqId, string commandJson, RobotCommandResult result, long sendWallMs, long sendNs, long resultWallMs, long resultNs, string targetSummary = "")
        {
            if (!enablePaperRecorder || !_running)
            {
                return;
            }

            string experimentId;
            string sessionId;
            string runId;
            string trialId;
            int phaseId;
            int segmentId;
            bool streamEnabled;
            bool recordEnabled;
            lock (_contextLock)
            {
                experimentId = _experimentId;
                sessionId = _sessionId;
                runId = _runId;
                trialId = _trialId;
                phaseId = _phaseId;
                segmentId = _segmentId;
                streamEnabled = _streamEnabled;
                recordEnabled = _recordEnabled;
            }

            if (string.IsNullOrEmpty(reqId))
            {
                reqId = ExtractJsonString(commandJson, "req_id");
                if (string.IsNullOrEmpty(reqId))
                {
                    reqId = ExtractJsonString(commandJson, "id");
                }
            }

            long seq = Interlocked.Increment(ref _commandSeq);
            if (string.IsNullOrEmpty(reqId))
            {
                reqId = "unity-cmd-" + seq.ToString(CultureInfo.InvariantCulture);
            }

            string commandType = string.IsNullOrEmpty(action) ? ExtractJsonString(commandJson, "cmd") : action;
            StringBuilder b = new StringBuilder(512);
            b.Append(Csv(runId)).Append(',');
            b.Append(Csv(trialId)).Append(',');
            b.Append(Csv(experimentId)).Append(',');
            b.Append(Csv(sessionId)).Append(',');
            b.Append(Csv(reqId)).Append(',');
            b.Append(seq).Append(',');
            b.Append(sendWallMs).Append(',');
            b.Append(sendWallMs * 1000000L).Append(',');
            b.Append(sendNs).Append(',');
            b.Append(resultWallMs).Append(',');
            b.Append(resultWallMs * 1000000L).Append(',');
            b.Append(resultNs).Append(',');
            b.Append(UnityEngine.Time.realtimeSinceStartup.ToString("0.######", CultureInfo.InvariantCulture)).Append(',');
            b.Append(phaseId).Append(',');
            b.Append(segmentId).Append(',');
            b.Append(streamEnabled ? 1 : 0).Append(',');
            b.Append(recordEnabled ? 1 : 0).Append(',');
            b.Append(Csv(route)).Append(',');
            b.Append(Csv(commandType)).Append(',');
            b.Append(Csv(result.Status)).Append(',');
            b.Append(result.Success ? 1 : 0).Append(',');
            b.Append(result.DryRun ? 1 : 0).Append(',');
            b.Append(Csv(result.ErrorMessage)).Append(',');
            b.Append(commandJson == null ? 0 : Encoding.UTF8.GetByteCount(commandJson)).Append(',');
            b.Append(Csv(targetSummary)).Append(',');
            b.Append(Csv(commandJson));
            EnqueueBounded(_commandQueue, b.ToString());

            EnqueueEvent(PaperExperimentDefaults.CommandEchoEvent, BuildCommandEventNotes(seq, route, commandType, reqId, result, sendWallMs, targetSummary));
        }

        public void EnqueueEvent(string eventType, string notes)
        {
            if (!enablePaperRecorder || !_running)
            {
                return;
            }

            string experimentId;
            string sessionId;
            string runId;
            string trialId;
            int phaseId;
            int segmentId;
            bool streamEnabled;
            bool recordEnabled;
            lock (_contextLock)
            {
                experimentId = _experimentId;
                sessionId = _sessionId;
                runId = _runId;
                trialId = _trialId;
                phaseId = _phaseId;
                segmentId = _segmentId;
                streamEnabled = _streamEnabled;
                recordEnabled = _recordEnabled;
            }

            long wallMs = SystemClock.UtcUnixMs();
            long hostNs = SystemClock.UtcUnixNs();
            StringBuilder b = new StringBuilder(256);
            b.Append(wallMs).Append(',');
            b.Append(Escape(eventType)).Append(',');
            b.Append(Escape(experimentId)).Append(',');
            b.Append(Escape(sessionId)).Append(',');
            b.Append(phaseId).Append(',');
            b.Append(segmentId).Append(',');
            b.Append(streamEnabled ? 1 : 0).Append(',');
            b.Append(recordEnabled ? 1 : 0).Append(',');
            b.Append(Escape(notes));
            EnqueueBounded(_eventQueue, b.ToString());
            EnqueueBounded(_eventJsonQueue, BuildEventJsonLine(eventType, notes, runId, trialId, experimentId, sessionId, phaseId, segmentId, streamEnabled, recordEnabled, wallMs, hostNs));
        }

        public void Shutdown()
        {
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
            _ready = false;
        }

        private void ApplyProfile(TwinRuntimeProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            TwinRuntimeSettings settings = profile.BuildRuntimeSettings();
            enablePaperRecorder = settings.EnablePaperRecorder;
            recordReceiveFrames = settings.PaperRecordReceiveFrames;
            recordApplyFrames = settings.PaperRecordApplyFrames;
            storageRootDirectory = string.IsNullOrWhiteSpace(settings.PaperStorageRootDirectory)
                ? DefaultRootDirectory
                : settings.PaperStorageRootDirectory;
            batchSize = settings.PaperRecorderBatchSize;
            flushIntervalMs = settings.PaperRecorderFlushIntervalMs;
            queueHardLimit = settings.PaperQueueHardLimit;
        }

        private void BeginSession(string experimentId, string sessionId)
        {
            Shutdown();
            DrainAllQueues();
            _runStamp = string.IsNullOrEmpty(_runStamp)
                ? DateTime.Now.ToString("MMddHHmm", CultureInfo.InvariantCulture)
                : _runStamp;
            _sessionDirectory = Path.Combine(ResolveRootDirectory(), _runStamp, SafePath(experimentId), SafePath(sessionId));
            Directory.CreateDirectory(_sessionDirectory);
            WriteManifest(experimentId, sessionId);
            _running = true;
            _writerThread = new Thread(WriterLoop)
            {
                IsBackground = true,
                Name = "DigitalTwin Paper Recorder"
            };
            _writerThread.Start();
            EnqueueEvent("SESSION_CREATED", string.Empty);
        }

        private bool CanWriteHighFrequency()
        {
            lock (_contextLock)
            {
                return _recordEnabled && !string.IsNullOrEmpty(_sessionId);
            }
        }

        private void WriterLoop()
        {
            StreamWriter receiveWriter = null;
            StreamWriter applyWriter = null;
            StreamWriter frameWriter = null;
            StreamWriter commandWriter = null;
            StreamWriter eventWriter = null;
            StreamWriter eventJsonWriter = null;
            try
            {
                receiveWriter = new StreamWriter(Path.Combine(_sessionDirectory, "unity_receive.csv"), false, new UTF8Encoding(true));
                applyWriter = new StreamWriter(Path.Combine(_sessionDirectory, "unity_apply.csv"), false, new UTF8Encoding(true));
                frameWriter = new StreamWriter(Path.Combine(_sessionDirectory, "unity_frames.csv"), false, new UTF8Encoding(true));
                commandWriter = new StreamWriter(Path.Combine(_sessionDirectory, "unity_commands.csv"), false, new UTF8Encoding(true));
                eventWriter = new StreamWriter(Path.Combine(_sessionDirectory, "unity_event.csv"), false, new UTF8Encoding(true));
                eventJsonWriter = new StreamWriter(Path.Combine(_sessionDirectory, "unity_events.jsonl"), false, new UTF8Encoding(false));
                string frameHeader = BuildFrameHeader();
                receiveWriter.WriteLine(frameHeader);
                applyWriter.WriteLine(frameHeader);
                frameWriter.WriteLine(BuildUnifiedFrameHeader());
                commandWriter.WriteLine(BuildCommandHeader());
                eventWriter.WriteLine("wall_time_ms,event_type,experiment_id,session_id,phase_id,segment_id,stream_enabled,record_enabled,notes");
                _ready = true;

                DateTime lastFlush = DateTime.UtcNow;
                while (_running || !_receiveQueue.IsEmpty || !_applyQueue.IsEmpty || !_frameQueue.IsEmpty || !_commandQueue.IsEmpty || !_eventQueue.IsEmpty || !_eventJsonQueue.IsEmpty)
                {
                    int written = 0;
                    written += DrainQueue(_receiveQueue, receiveWriter, batchSize);
                    written += DrainQueue(_applyQueue, applyWriter, batchSize);
                    written += DrainQueue(_frameQueue, frameWriter, batchSize);
                    written += DrainQueue(_commandQueue, commandWriter, batchSize);
                    written += DrainQueue(_eventQueue, eventWriter, batchSize);
                    written += DrainQueue(_eventJsonQueue, eventJsonWriter, batchSize);

                    bool flushDue = (DateTime.UtcNow - lastFlush).TotalMilliseconds >= flushIntervalMs;
                    if (flushDue || written > 0 && PendingCount == 0)
                    {
                        receiveWriter.Flush();
                        applyWriter.Flush();
                        frameWriter.Flush();
                        commandWriter.Flush();
                        eventWriter.Flush();
                        eventJsonWriter.Flush();
                        lastFlush = DateTime.UtcNow;
                    }

                    if (written == 0)
                    {
                        Thread.Sleep(25);
                    }
                }

                receiveWriter.Flush();
                applyWriter.Flush();
                frameWriter.Flush();
                commandWriter.Flush();
                eventWriter.Flush();
                eventJsonWriter.Flush();
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
            }
            finally
            {
                try { receiveWriter?.Close(); } catch { }
                try { applyWriter?.Close(); } catch { }
                try { frameWriter?.Close(); } catch { }
                try { commandWriter?.Close(); } catch { }
                try { eventWriter?.Close(); } catch { }
                try { eventJsonWriter?.Close(); } catch { }
                _ready = false;
            }
        }

        private int DrainQueue(ConcurrentQueue<string> queue, StreamWriter writer, int maxCount)
        {
            int written = 0;
            while (written < maxCount && queue.TryDequeue(out string row))
            {
                Interlocked.Decrement(ref _pendingRows);
                writer.WriteLine(row);
                written++;
            }

            return written;
        }

        private void EnqueueBounded(ConcurrentQueue<string> queue, string row)
        {
            while (PendingCount >= queueHardLimit && queue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _pendingRows);
                Interlocked.Increment(ref _droppedRows);
            }

            queue.Enqueue(row);
            Interlocked.Increment(ref _pendingRows);
        }

        private void DrainAllQueues()
        {
            while (_receiveQueue.TryDequeue(out _)) { }
            while (_applyQueue.TryDequeue(out _)) { }
            while (_frameQueue.TryDequeue(out _)) { }
            while (_commandQueue.TryDequeue(out _)) { }
            while (_eventQueue.TryDequeue(out _)) { }
            while (_eventJsonQueue.TryDequeue(out _)) { }
            Interlocked.Exchange(ref _pendingRows, 0);
        }

        private string BuildFrameHeader()
        {
            StringBuilder b = new StringBuilder(512);
            b.Append("experiment_id,session_id,source_session_id,source_id,source,seq,source_ts_ms,send_wall_ms,send_perf_ns");
            b.Append(",unity_receive_wall_ms,unity_receive_ns,unity_publish_wall_ms,unity_publish_ns,unity_apply_wall_ms,unity_apply_ns");
            b.Append(",network_wall_ms,runtime_publish_ms,unity_process_ms,end_to_end_wall_ms");
            b.Append(",clock_sync_status,frame_quality,payload_bytes,phase_id,segment_id,stream_enabled,record_enabled,mode,motion,motion_error");
            int jointCount = ResolveJointCount(null);
            for (int i = 0; i < jointCount; i++) b.Append(",joint_").Append(i + 1).Append("_rad");
            for (int i = 0; i < jointCount; i++) b.Append(",joint_").Append(i + 1).Append("_vel_rad_s");
            for (int i = 0; i < jointCount; i++) b.Append(",joint_").Append(i + 1).Append("_torque_nm");
            b.Append(",force_x,force_y,force_z,torque_x,torque_y,torque_z,tcp_x,tcp_y,tcp_z,tcp_qx,tcp_qy,tcp_qz,tcp_qw,event_type,notes");
            return b.ToString();
        }

        private string BuildUnifiedFrameHeader()
        {
            StringBuilder b = new StringBuilder(512);
            b.Append("run_id,trial_id,experiment_id,session_id,req_id,seq,frame_stage,source,source_id,source_session_id,channel");
            b.Append(",host_wall_ms,host_wall_ns,unity_time_s,unity_frame_count,source_ts_ms,send_wall_ms,send_perf_ns");
            b.Append(",unity_receive_wall_ms,unity_receive_ns,unity_publish_wall_ms,unity_publish_ns,unity_apply_wall_ms,unity_apply_ns");
            b.Append(",receive_to_publish_ms,receive_to_apply_ms,end_to_end_wall_ms,clock_sync_status,frame_quality,flags,payload_bytes");
            b.Append(",phase_id,segment_id,stream_enabled,record_enabled,mode,motion,motion_error");
            int jointCount = ResolveJointCount(null);
            for (int i = 0; i < jointCount; i++) b.Append(",joint_").Append(i + 1).Append("_rad");
            for (int i = 0; i < jointCount; i++) b.Append(",joint_").Append(i + 1).Append("_vel_rad_s");
            for (int i = 0; i < jointCount; i++) b.Append(",joint_").Append(i + 1).Append("_torque_nm");
            b.Append(",force_x,force_y,force_z,torque_x,torque_y,torque_z,tcp_x,tcp_y,tcp_z,tcp_qx,tcp_qy,tcp_qz,tcp_qw,event_type,notes");
            return b.ToString();
        }

        private static string BuildCommandHeader()
        {
            return "run_id,trial_id,experiment_id,session_id,req_id,cmd_seq,send_wall_ms,send_wall_ns,send_unity_ns,result_wall_ms,result_wall_ns,result_unity_ns,unity_time_s,phase_id,segment_id,stream_enabled,record_enabled,route,command,status,success,dry_run,error,payload_bytes,target_summary,command_json";
        }

        private string BuildFrameRow(RobotStateFrame frame, bool includeApply)
        {
            string experimentId;
            string sessionId;
            int phaseId;
            int segmentId;
            bool streamEnabled;
            bool recordEnabled;
            lock (_contextLock)
            {
                experimentId = string.IsNullOrEmpty(frame.ExperimentId) ? _experimentId : frame.ExperimentId;
                sessionId = string.IsNullOrEmpty(frame.SourceSessionId) ? _sessionId : frame.SourceSessionId;
                phaseId = frame.PhaseId != 0 ? frame.PhaseId : _phaseId;
                segmentId = frame.SegmentId != 0 ? frame.SegmentId : _segmentId;
                streamEnabled = frame.StreamEnabled || _streamEnabled;
                recordEnabled = frame.RecordEnabled || _recordEnabled;
            }

            long applyNs = includeApply ? frame.UnityApplyTimestampNs : 0;
            long applyWall = includeApply ? frame.UnityApplyWallMs : 0;
            double networkWallMs = frame.SendWallMs > 0d && frame.UnityReceiveWallMs > 0
                ? frame.UnityReceiveWallMs - frame.SendWallMs
                : 0d;
            double runtimePublishMs = frame.UnityReceiveTimestampNs > 0 && frame.UnityPublishTimestampNs > 0
                ? SystemClock.ElapsedMs(frame.UnityReceiveTimestampNs, frame.UnityPublishTimestampNs)
                : 0d;
            double processMs = includeApply && frame.UnityReceiveTimestampNs > 0 && applyNs > 0
                ? SystemClock.ElapsedMs(frame.UnityReceiveTimestampNs, applyNs)
                : 0d;
            double endToEndWallMs = includeApply && frame.SendWallMs > 0d && applyWall > 0
                ? applyWall - frame.SendWallMs
                : 0d;

            StringBuilder b = new StringBuilder(1024);
            b.Append(Escape(experimentId)).Append(',');
            b.Append(Escape(sessionId)).Append(',');
            b.Append(Escape(frame.SourceSessionId)).Append(',');
            b.Append(Escape(frame.SourceId)).Append(',');
            b.Append(Escape(frame.SourceName)).Append(',');
            b.Append(frame.SequenceId).Append(',');
            b.Append(frame.SourceTimestampMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            b.Append(frame.SendWallMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            b.Append(frame.SendPerfNs).Append(',');
            b.Append(frame.UnityReceiveWallMs).Append(',');
            b.Append(frame.UnityReceiveTimestampNs).Append(',');
            b.Append(frame.UnityPublishWallMs).Append(',');
            b.Append(frame.UnityPublishTimestampNs).Append(',');
            b.Append(applyWall).Append(',');
            b.Append(applyNs).Append(',');
            b.Append(networkWallMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            b.Append(runtimePublishMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            b.Append(processMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            b.Append(endToEndWallMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            b.Append(frame.ClockSyncStatus).Append(',');
            b.Append(frame.Quality).Append(',');
            b.Append(frame.RawPayload == null ? 0 : frame.RawPayload.Length).Append(',');
            b.Append(phaseId).Append(',');
            b.Append(segmentId).Append(',');
            b.Append(streamEnabled ? 1 : 0).Append(',');
            b.Append(recordEnabled ? 1 : 0).Append(',');
            b.Append(Escape(frame.Mode)).Append(',');
            b.Append(Escape(frame.MotionState)).Append(',');
            b.Append(Escape(frame.MotionError));

            int jointCount = ResolveJointCount(frame);
            AppendArray(b, frame.JointPositionRad, jointCount);
            AppendArray(b, frame.JointVelocityRad, jointCount);
            AppendArray(b, frame.JointTorqueNm, jointCount);
            AppendArray(b, frame.ForceVector, 6);
            AppendTcp(b, frame);
            b.Append(',').Append(Escape(frame.EventType));
            b.Append(',').Append(Escape(frame.Notes));
            return b.ToString();
        }

        private string BuildUnifiedFrameRow(RobotStateFrame frame, string stage)
        {
            string experimentId;
            string sessionId;
            string runId;
            string trialId;
            int phaseId;
            int segmentId;
            bool streamEnabled;
            bool recordEnabled;
            lock (_contextLock)
            {
                experimentId = string.IsNullOrEmpty(frame.ExperimentId) ? _experimentId : frame.ExperimentId;
                sessionId = string.IsNullOrEmpty(frame.SourceSessionId) ? _sessionId : frame.SourceSessionId;
                runId = string.IsNullOrEmpty(frame.RunId) ? _runId : frame.RunId;
                trialId = string.IsNullOrEmpty(frame.TrialId) ? _trialId : frame.TrialId;
                phaseId = frame.PhaseId != 0 ? frame.PhaseId : _phaseId;
                segmentId = frame.SegmentId != 0 ? frame.SegmentId : _segmentId;
                streamEnabled = frame.StreamEnabled || _streamEnabled;
                recordEnabled = frame.RecordEnabled || _recordEnabled;
            }

            long hostWallMs = SystemClock.UtcUnixMs();
            long hostNs = SystemClock.UtcUnixNs();
            long applyNs = string.Equals(stage, "apply", StringComparison.OrdinalIgnoreCase) ? frame.UnityApplyTimestampNs : 0;
            long applyWall = string.Equals(stage, "apply", StringComparison.OrdinalIgnoreCase) ? frame.UnityApplyWallMs : 0;
            double receiveToPublishMs = frame.UnityReceiveTimestampNs > 0 && frame.UnityPublishTimestampNs > 0
                ? SystemClock.ElapsedMs(frame.UnityReceiveTimestampNs, frame.UnityPublishTimestampNs)
                : 0d;
            double receiveToApplyMs = applyNs > 0 && frame.UnityReceiveTimestampNs > 0
                ? SystemClock.ElapsedMs(frame.UnityReceiveTimestampNs, applyNs)
                : 0d;
            double endToEndWallMs = applyWall > 0 && frame.SendWallMs > 0d
                ? applyWall - frame.SendWallMs
                : 0d;

            StringBuilder b = new StringBuilder(1200);
            b.Append(Escape(runId)).Append(',');
            b.Append(Escape(trialId)).Append(',');
            b.Append(Escape(experimentId)).Append(',');
            b.Append(Escape(sessionId)).Append(',');
            b.Append(Escape(frame.RequestId)).Append(',');
            b.Append(frame.SequenceId).Append(',');
            b.Append(Escape(stage)).Append(',');
            b.Append(Escape(frame.SourceName)).Append(',');
            b.Append(Escape(frame.SourceId)).Append(',');
            b.Append(Escape(frame.SourceSessionId)).Append(',');
            b.Append(Escape(frame.Channel)).Append(',');
            b.Append(hostWallMs).Append(',');
            b.Append(hostNs).Append(',');
            b.Append(UnityEngine.Time.realtimeSinceStartup.ToString("0.######", CultureInfo.InvariantCulture)).Append(',');
            b.Append(UnityEngine.Time.frameCount).Append(',');
            b.Append(frame.SourceTimestampMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            b.Append(frame.SendWallMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            b.Append(frame.SendPerfNs).Append(',');
            b.Append(frame.UnityReceiveWallMs).Append(',');
            b.Append(frame.UnityReceiveTimestampNs).Append(',');
            b.Append(frame.UnityPublishWallMs).Append(',');
            b.Append(frame.UnityPublishTimestampNs).Append(',');
            b.Append(applyWall).Append(',');
            b.Append(applyNs).Append(',');
            b.Append(receiveToPublishMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            b.Append(receiveToApplyMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            b.Append(endToEndWallMs.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            b.Append(frame.ClockSyncStatus).Append(',');
            b.Append(frame.Quality).Append(',');
            b.Append(frame.Flags).Append(',');
            b.Append(frame.RawPayload == null ? 0 : Encoding.UTF8.GetByteCount(frame.RawPayload)).Append(',');
            b.Append(phaseId).Append(',');
            b.Append(segmentId).Append(',');
            b.Append(streamEnabled ? 1 : 0).Append(',');
            b.Append(recordEnabled ? 1 : 0).Append(',');
            b.Append(Escape(frame.Mode)).Append(',');
            b.Append(Escape(frame.MotionState)).Append(',');
            b.Append(Escape(frame.MotionError));

            int jointCount = ResolveJointCount(frame);
            AppendArray(b, frame.JointPositionRad, jointCount);
            AppendArray(b, frame.JointVelocityRad, jointCount);
            AppendArray(b, frame.JointTorqueNm, jointCount);
            AppendArray(b, frame.ForceVector, 6);
            AppendTcp(b, frame);
            b.Append(',').Append(Escape(frame.EventType));
            b.Append(',').Append(Escape(frame.Notes));
            return b.ToString();
        }

        private int ResolveJointCount(RobotStateFrame frame)
        {
            if (_schema != null && _schema.JointCount > 0)
            {
                return _schema.JointCount;
            }

            return frame != null && frame.JointPositionRad != null && frame.JointPositionRad.Length > 0
                ? frame.JointPositionRad.Length
                : 6;
        }

        private static void AppendArray(StringBuilder b, float[] values, int count)
        {
            for (int i = 0; i < count; i++)
            {
                b.Append(',');
                if (values != null && i < values.Length)
                {
                    b.Append(values[i].ToString("0.########", CultureInfo.InvariantCulture));
                }
            }
        }

        private static void AppendTcp(StringBuilder b, RobotStateFrame frame)
        {
            for (int i = 0; i < 7; i++)
            {
                b.Append(',');
                if (!frame.HasTcpPose)
                {
                    continue;
                }

                switch (i)
                {
                    case 0: b.Append(frame.TcpPositionMeters.x.ToString("0.########", CultureInfo.InvariantCulture)); break;
                    case 1: b.Append(frame.TcpPositionMeters.y.ToString("0.########", CultureInfo.InvariantCulture)); break;
                    case 2: b.Append(frame.TcpPositionMeters.z.ToString("0.########", CultureInfo.InvariantCulture)); break;
                    case 3: b.Append(frame.TcpRotation.x.ToString("0.########", CultureInfo.InvariantCulture)); break;
                    case 4: b.Append(frame.TcpRotation.y.ToString("0.########", CultureInfo.InvariantCulture)); break;
                    case 5: b.Append(frame.TcpRotation.z.ToString("0.########", CultureInfo.InvariantCulture)); break;
                    case 6: b.Append(frame.TcpRotation.w.ToString("0.########", CultureInfo.InvariantCulture)); break;
                }
            }
        }

        private void WriteManifest(string experimentId, string sessionId)
        {
            StringBuilder b = new StringBuilder(512);
            b.AppendLine("{");
            b.Append("  \"schema_version\": \"").Append(SchemaVersion).Append("\",").AppendLine();
            b.Append("  \"experiment_id\": \"").Append(EscapeJson(experimentId)).Append("\",").AppendLine();
            b.Append("  \"session_id\": \"").Append(EscapeJson(sessionId)).Append("\",").AppendLine();
            b.Append("  \"run_id\": \"").Append(EscapeJson(string.IsNullOrEmpty(_runId) ? experimentId : _runId)).Append("\",").AppendLine();
            b.Append("  \"trial_id\": \"").Append(EscapeJson(string.IsNullOrEmpty(_trialId) ? sessionId : _trialId)).Append("\",").AppendLine();
            b.Append("  \"run_stamp\": \"").Append(EscapeJson(_runStamp)).Append("\",").AppendLine();
            b.Append("  \"created_wall_ms\": ").Append(SystemClock.UtcUnixMs()).Append(',').AppendLine();
            b.Append("  \"unity_version\": \"").Append(EscapeJson(Application.unityVersion)).Append("\",").AppendLine();
            b.Append("  \"scene_name\": \"").Append(EscapeJson(SceneManager.GetActiveScene().name)).Append("\",").AppendLine();
            b.Append("  \"profile_name\": \"").Append(EscapeJson(_profile == null ? string.Empty : _profile.name)).Append("\",").AppendLine();
            b.Append("  \"schema_name\": \"").Append(EscapeJson(_schema == null ? string.Empty : _schema.name)).Append("\",").AppendLine();
            b.Append("  \"source_type\": \"").Append(EscapeJson(ResolveSourceType())).Append("\",").AppendLine();
            TwinRuntimeSettings settings = TwinRuntimeSettings.FromProfile(_profile);
            b.Append("  \"target_hz\": ").Append((_profile == null ? 0f : settings.RobotApplyRateHz).ToString("0.###", CultureInfo.InvariantCulture)).Append(',').AppendLine();
            b.Append("  \"recording_hz\": ").Append((_profile == null ? 0f : settings.MetricsRefreshRateHz).ToString("0.###", CultureInfo.InvariantCulture)).Append(',').AppendLine();
            b.Append("  \"calibration_version\": \"").Append(EscapeJson(_schema == null ? string.Empty : _schema.CalibrationVersion)).Append("\",").AppendLine();
            b.Append("  \"joint_names\": ").Append(BuildJsonArray(_schema == null ? null : _schema.JointNames)).Append(',').AppendLine();
            b.Append("  \"enabled_modules\": {");
            b.Append("\"dart\":").Append(_profile == null || settings.UseDartStudio ? "true" : "false").Append(',');
            b.Append("\"ros2\":").Append(_profile != null && settings.UseRos2 ? "true" : "false").Append(',');
            b.Append("\"replay\":").Append(_profile != null && settings.UseReplay ? "true" : "false").Append(',');
            b.Append("\"paper_recorder\":").Append(enablePaperRecorder ? "true" : "false").Append(',');
            b.Append("\"legacy_recorder\":").Append(_profile == null || settings.EnableRecording ? "true" : "false").Append(',');
            b.Append("\"runtime_ui\":").Append(_profile == null || settings.EnableRuntimeUi ? "true" : "false").Append("},").AppendLine();
            b.Append("  \"record_receive_frames\": ").Append(recordReceiveFrames ? "true" : "false").Append(',').AppendLine();
            b.Append("  \"record_apply_frames\": ").Append(recordApplyFrames ? "true" : "false").Append(',').AppendLine();
            b.Append("  \"outputs\": [\"unity_frames.csv\",\"unity_commands.csv\",\"unity_events.jsonl\",\"unity_manifest.json\",\"unity_receive.csv\",\"unity_apply.csv\",\"unity_event.csv\"],").AppendLine();
            b.Append("  \"notes\": \"\"").AppendLine();
            b.AppendLine("}");
            string content = b.ToString();
            File.WriteAllText(Path.Combine(_sessionDirectory, "session_manifest.json"), content, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(_sessionDirectory, "manifest.json"), content, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(_sessionDirectory, "unity_manifest.json"), content, new UTF8Encoding(false));
        }

        private string ResolveSourceType()
        {
            TwinRuntimeSettings settings = TwinRuntimeSettings.FromProfile(_profile);
            if (_profile != null && settings.UseReplay) return "Replay";
            if (_profile != null && settings.UseRos2) return "ROS2";
            return "DartStudio";
        }

        private static string BuildJsonArray(string[] values)
        {
            if (values == null || values.Length == 0)
            {
                return "[]";
            }

            StringBuilder b = new StringBuilder();
            b.Append('[');
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) b.Append(',');
                b.Append('"').Append(EscapeJson(values[i])).Append('"');
            }
            b.Append(']');
            return b.ToString();
        }

        private static string BuildCommandEventNotes(long commandSeq, string route, string commandType, string reqId, RobotCommandResult result, long sendWallMs, string targetSummary)
        {
            StringBuilder b = new StringBuilder(256);
            b.Append('{');
            AppendJsonProperty(b, "cmd_seq", commandSeq.ToString(CultureInfo.InvariantCulture), false, true);
            AppendJsonProperty(b, "route", route, true);
            AppendJsonProperty(b, "command", commandType, true);
            AppendJsonProperty(b, "req_id", reqId, true);
            AppendJsonProperty(b, "send_wall_ms", sendWallMs.ToString(CultureInfo.InvariantCulture), false, true);
            AppendJsonProperty(b, "status", result.Status, true);
            AppendJsonProperty(b, "success", result.Success ? "true" : "false", false, true);
            AppendJsonProperty(b, "dry_run", result.DryRun ? "true" : "false", false, true);
            AppendJsonProperty(b, "target_summary", targetSummary, true);
            AppendJsonProperty(b, "error", result.ErrorMessage, true);
            b.Append('}');
            return b.ToString();
        }

        private static string BuildEventJsonLine(string eventType, string notes, string runId, string trialId, string experimentId, string sessionId, int phaseId, int segmentId, bool streamEnabled, bool recordEnabled, long wallMs, long hostNs)
        {
            StringBuilder b = new StringBuilder(512);
            b.Append('{');
            AppendJsonProperty(b, "run_id", runId, true);
            AppendJsonProperty(b, "trial_id", trialId, true);
            AppendJsonProperty(b, "experiment_id", experimentId, true);
            AppendJsonProperty(b, "session_id", sessionId, true);
            AppendJsonProperty(b, "event_type", eventType, true);
            AppendJsonProperty(b, "host_wall_ms", wallMs.ToString(CultureInfo.InvariantCulture), false, true);
            AppendJsonProperty(b, "host_wall_ns", hostNs.ToString(CultureInfo.InvariantCulture), false, true);
            AppendJsonProperty(b, "phase_id", phaseId.ToString(CultureInfo.InvariantCulture), false, true);
            AppendJsonProperty(b, "segment_id", segmentId.ToString(CultureInfo.InvariantCulture), false, true);
            AppendJsonProperty(b, "stream_enabled", streamEnabled ? "true" : "false", false, true);
            AppendJsonProperty(b, "record_enabled", recordEnabled ? "true" : "false", false, true);
            AppendJsonProperty(b, "notes", notes, true);
            b.Append('}');
            return b.ToString();
        }

        private static void AppendJsonProperty(StringBuilder b, string name, string value, bool quoteValue, bool forceComma = false)
        {
            if (b.Length > 1 && (forceComma || b[b.Length - 1] != '{'))
            {
                b.Append(',');
            }

            b.Append('"').Append(EscapeJson(name)).Append("\":");
            if (quoteValue)
            {
                b.Append('"').Append(EscapeJson(value)).Append('"');
            }
            else
            {
                b.Append(string.IsNullOrWhiteSpace(value) ? "null" : value);
            }
        }

        private static string ExtractJsonString(string json, string propertyName)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(propertyName))
            {
                return string.Empty;
            }

            string marker = "\"" + propertyName + "\":\"";
            int start = json.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0)
            {
                return string.Empty;
            }

            start += marker.Length;
            StringBuilder b = new StringBuilder();
            bool escaped = false;
            for (int i = start; i < json.Length; i++)
            {
                char c = json[i];
                if (escaped)
                {
                    b.Append(c);
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"')
                {
                    break;
                }

                b.Append(c);
            }

            return b.ToString();
        }

        private string ResolveRootDirectory()
        {
            return string.IsNullOrWhiteSpace(storageRootDirectory) ? DefaultRootDirectory : storageRootDirectory;
        }

        private static string SafePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "session";
            }

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }

            return value;
        }

        private static string Escape(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace(",", "_").Replace("\r", " ").Replace("\n", " ");
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            bool quote = value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
            string clean = value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ");
            return quote ? "\"" + clean + "\"" : clean;
        }

        private static string EscapeJson(string value)
        {
            return string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
