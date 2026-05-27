# DigitalTwin Experiment Layer

> 上级：[README](../../README.md) · **接口总表**：[INTERFACES.md §4.4、§9](../../INTERFACES.md) · 进度：[PROGRESS.md](../../PROGRESS.md)

Unity 是论文级**数据采集端**；Python/DartStudioMock 在工程外负责发包与 `analyze.py` 离线指标。

## 子目录文档

| 目录 | README | 职责 |
|------|--------|------|
| `Contracts/` | [Contracts/README.md](Contracts/README.md) | `IExperimentRecorder`、`PaperExperimentDefaults` |
| `Session/` | [Session/README.md](Session/README.md) | `ExperimentSessionController` 生命周期 TCP |
| `Dart_E/` | [Dart_E/README.md](Dart_E/README.md) | Dart 实验 Inspector 面板 |
| `Ros2/` | [Ros2/README.md](Ros2/README.md) | `Ros2ExperimentPanel` |
| `Recording/` | [Recording/README.md](Recording/README.md) | `TwinPaperRecorder` |
| `Metrics/` | [Metrics/README.md](Metrics/README.md) | `TwinExperimentTracker`、PING/PONG |
| `Replay/` | [Replay/README.md](Replay/README.md) | `ReplayStateSource` CSV 回放 |

## Folder Roles

```text
Experiment/
  Contracts/    Shared experiment-facing contracts and marker names.
  Session/      Manual session orchestration and Inspector execution panels.
  Dart_E/       DartStudio V1-RC runtime experiment panel; reuses existing bridge/session/recorder.
  Ros2/         ROS2/MoveIt experiment panel and topic presets.
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

Add new paper experiments under `Experiment/` first. Keep transport in `Communication`, safety in `Control`, model in `RobotModelController`, metrics in `analyze.py`. **Wire formats and APIs:** [INTERFACES.md](../../INTERFACES.md).
