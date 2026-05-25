# DigitalTwin Experiment Layer

Unity is the paper-grade data processing and collection endpoint. Python/DartStudioMock stays outside Unity as the sender, mock source, and offline analyzer.

## Folder Roles

```text
Experiment/
  Contracts/    Shared experiment-facing contracts and marker names.
  Session/      Manual session orchestration and Inspector execution panels.
  Dart_E/       DartStudio V1-RC runtime experiment panel; reuses existing bridge/session/recorder.
  Recording/    Paper CSV and manifest writers.
  Metrics/      Runtime markers for communication, sync, drift, and control echo facts.
  Replay/       Recorded CSV replay source for reproducible validation.
```

## Main Experiment Defaults

The hot path should stay narrow for the first paper runs:

```text
enabled:  Dart source, joint_position, tool_force, paper receive/apply/event logs
disabled: joint_velocity, joint_effort, tcp_pose, extra_signals, ROS2 dual source,
          SQLite, Replay, Ghost/IK, runtime UI refresh, debug overlay,
          legacy frames_*.csv recorder
```

`TwinPaperRecorder` records raw facts only. Paper metrics such as latency, jitter, frame quality, synchronization error, and control timing are computed offline by `analyze.py`.

## Extension Rule

Add new paper experiments here first. Keep transport parsing in `Communication`, real command safety in `Control`, model writing in `RobotModelController`, and offline metric formulas in the Python analyzer.
