using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace DigitalTwin
{
    [DisallowMultipleComponent]
    public sealed class ReplayStateSource : MonoBehaviour, IRobotStateSource
    {
        [SerializeField] private string csvPath = string.Empty;
        [SerializeField] private float replayHz = 30f;
        [SerializeField] private bool loop;
        [SerializeField] private bool autoLoadOnInitialize = true;

        private readonly Queue<RobotStateFrame> _queue = new Queue<RobotStateFrame>();
        private readonly List<RobotStateFrame> _frames = new List<RobotStateFrame>();
        private RobotSignalSchema _schema;
        private int _cursor;
        private float _lastEmitAt;
        private string _lastError = string.Empty;
        private long _lastReceiveNs;
        private long _droppedFrames;
        private bool _loaded;

        public RuntimeSourceKind Kind => RuntimeSourceKind.Replay;
        public int QueuedFrameCount => _queue.Count;
        public bool IsLoaded => _loaded;
        public int FrameCount => _frames.Count;

        public void Initialize(TwinRuntimeProfile profile, RobotSignalSchema schema)
        {
            _schema = schema;
            if (profile != null)
            {
                TwinRuntimeSettings settings = profile.BuildRuntimeSettings();
                csvPath = string.IsNullOrWhiteSpace(settings.ReplayCsvPath) ? csvPath : settings.ReplayCsvPath;
                replayHz = Mathf.Max(1f, settings.ReplayHz);
                loop = settings.ReplayLoop;
            }

            if (autoLoadOnInitialize)
            {
                LoadCsv(csvPath);
            }
        }

        private void OnValidate()
        {
            replayHz = Mathf.Max(1f, replayHz);
        }

        private void Update()
        {
            if (!_loaded || _frames.Count == 0)
            {
                return;
            }

            float interval = 1f / Mathf.Max(1f, replayHz);
            if (Time.unscaledTime - _lastEmitAt < interval)
            {
                return;
            }

            _lastEmitAt = Time.unscaledTime;
            if (_cursor >= _frames.Count)
            {
                if (!loop)
                {
                    return;
                }

                _cursor = 0;
            }

            RobotStateFrame frame = _frames[_cursor++].Clone();
            frame.UnityReceiveTimestampNs = SystemClock.NowNs();
            frame.UnityReceiveWallMs = SystemClock.UtcUnixMs();
            frame.SourceName = "Replay";
            frame.Quality = frame.Quality == FrameQuality.Lost ? FrameQuality.Lost : FrameQuality.Normal;
            _lastReceiveNs = frame.UnityReceiveTimestampNs;
            _queue.Enqueue(frame);
        }

        public bool TryDequeueFrame(out RobotStateFrame frame)
        {
            if (_queue.Count > 0)
            {
                frame = _queue.Dequeue();
                return true;
            }

            frame = null;
            return false;
        }

        public RobotSourceStatus GetStatus()
        {
            return new RobotSourceStatus("ReplayStateSource", _loaded, _lastError, _lastReceiveNs, _queue.Count);
        }

        public SourceHealth GetHealth()
        {
            double ageMs = _lastReceiveNs <= 0 ? -1d : SystemClock.ElapsedMs(_lastReceiveNs, SystemClock.NowNs());
            return new SourceHealth("ReplayStateSource", _loaded, _lastReceiveNs, ageMs, _cursor, _droppedFrames, replayHz, _lastError);
        }

        public bool LoadCsv(string path)
        {
            _frames.Clear();
            _queue.Clear();
            _cursor = 0;
            _loaded = false;
            _lastError = string.Empty;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _lastError = "Replay CSV not found.";
                return false;
            }

            try
            {
                string[] lines = File.ReadAllLines(path);
                if (lines.Length <= 1)
                {
                    _lastError = "Replay CSV is empty.";
                    return false;
                }

                string[] header = SplitCsvLine(lines[0]);
                Dictionary<string, int> columns = BuildColumnMap(header);
                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i]))
                    {
                        continue;
                    }

                    string[] row = SplitCsvLine(lines[i]);
                    if (TryParseFrame(row, columns, out RobotStateFrame frame))
                    {
                        _frames.Add(frame);
                    }
                }

                _loaded = _frames.Count > 0;
                if (!_loaded)
                {
                    _lastError = "Replay CSV contains no valid frames.";
                }

                return _loaded;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }
        }

        private bool TryParseFrame(string[] row, Dictionary<string, int> columns, out RobotStateFrame frame)
        {
            frame = new RobotStateFrame
            {
                SourceName = "Replay",
                Flags = RobotFrameFlags.Valid | RobotFrameFlags.HasJointPosition,
                ClockSyncStatus = TwinClockSyncStatus.Unsynced
            };

            frame.SequenceId = GetLong(row, columns, "seq");
            if (frame.SequenceId <= 0)
            {
                return false;
            }

            frame.ExperimentId = GetString(row, columns, "experiment_id");
            frame.SourceSessionId = GetString(row, columns, "session_id");
            frame.SourceId = GetString(row, columns, "source_id");
            frame.SourceTimestampMs = GetDouble(row, columns, "source_ts_ms");
            frame.SendWallMs = GetDouble(row, columns, "send_wall_ms");
            frame.SendPerfNs = GetLong(row, columns, "send_perf_ns");
            frame.Mode = GetString(row, columns, "mode");
            frame.MotionState = GetString(row, columns, "motion");
            frame.MotionError = GetString(row, columns, "motion_error");
            frame.PhaseId = (int)GetLong(row, columns, "phase_id");
            frame.SegmentId = (int)GetLong(row, columns, "segment_id");
            frame.StreamEnabled = GetLong(row, columns, "stream_enabled") != 0;
            frame.RecordEnabled = GetLong(row, columns, "record_enabled") != 0;
            frame.EventType = GetString(row, columns, "event_type");
            frame.Notes = GetString(row, columns, "notes");

            string quality = GetString(row, columns, "frame_quality");
            if (!string.IsNullOrEmpty(quality) && Enum.TryParse(quality, true, out FrameQuality parsedQuality))
            {
                frame.Quality = parsedQuality;
            }

            int jointCount = ResolveJointCount(columns);
            frame.JointPositionRad = new float[jointCount];
            for (int i = 0; i < jointCount; i++)
            {
                frame.JointPositionRad[i] = (float)GetDouble(row, columns, "joint_" + (i + 1) + "_rad");
            }

            frame.ForceVector = new float[6];
            string[] forceNames = { "force_x", "force_y", "force_z", "torque_x", "torque_y", "torque_z" };
            for (int i = 0; i < forceNames.Length; i++)
            {
                frame.ForceVector[i] = (float)GetDouble(row, columns, forceNames[i]);
            }

            return true;
        }

        private int ResolveJointCount(Dictionary<string, int> columns)
        {
            if (_schema != null && _schema.JointCount > 0)
            {
                return _schema.JointCount;
            }

            int count = 0;
            while (columns.ContainsKey("joint_" + (count + 1) + "_rad"))
            {
                count++;
            }

            return Math.Max(1, count);
        }

        private static Dictionary<string, int> BuildColumnMap(string[] header)
        {
            Dictionary<string, int> map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
            {
                if (!map.ContainsKey(header[i]))
                {
                    map.Add(header[i], i);
                }
            }

            return map;
        }

        private static string[] SplitCsvLine(string line)
        {
            return line.Split(',');
        }

        private static string GetString(string[] row, Dictionary<string, int> columns, string name)
        {
            return columns.TryGetValue(name, out int index) && index >= 0 && index < row.Length ? row[index] : string.Empty;
        }

        private static long GetLong(string[] row, Dictionary<string, int> columns, string name)
        {
            string value = GetString(row, columns, name);
            return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0;
        }

        private static double GetDouble(string[] row, Dictionary<string, int> columns, string name)
        {
            string value = GetString(row, columns, name);
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : 0d;
        }
    }
}
