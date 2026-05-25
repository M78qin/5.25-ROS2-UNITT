# DigitalTwin Architecture

## 2026-05-15 DartStudio 联调状态

Unity 侧 `Dart_E` / `ExperimentSessionController` / `DartStudioBridge` 已能把会话、通道、数据流、记录和模式命令发到 DartStudio。当前阻塞点在 `E:\zyq\DRL_3.12-\5.10-TW\dartstudio\digital_twin_robot_main_v1_rc.drl` 的 H2515 `movej` 运动 API 签名，暂不通过 Unity 协议层解决。

下一步先在 DartStudio 做最小 `posj + movej` 运动签名验证；通过后再回到 Unity 测 `mode1_test`、`mode2_ctrl` 抢占和 `HALT`。

This Unity digital twin is organized around three runtime planes.

## Data Plane

`Bridge -> Protocol -> DigitalTwinRuntime -> RobotStateBus -> RobotModel/UI/Recorder`

- `DartStudioBridge`, `Ros2Bridge`, and `ReplayStateSource` implement `IRobotStateSource`.
- `DartStudioProtocol` converts transport payloads into `RobotStateFrame`.
- `DigitalTwinRuntime` drains the active source, publishes to `RobotStateBus`, applies the latest frame to `RobotModelController`, and posts raw facts to recorders.
- `RobotModelController` is the only runtime writer to `ArticulationBody`.
- `TwinUIController` reads low-frequency snapshots, not transport queues.

## Control Plane

`UI/Experiment -> TwinCommandController -> Safety -> CommandSender`

- UI and experiment scripts must not open sockets directly.
- `TwinCommandController` owns command safety gates and mode state.
- `DartTcpCommandSender` is the high-level Inspector/experiment command wrapper.
- `DartStudioBridge.SendRawCommand` is the protocol-level TCP send path.

## Experiment Plane

`Experiment/Session -> Experiment/Recording + Experiment/Metrics -> CSV/Manifest -> analyze.py`

- Unity records raw facts only.
- Paper metrics are computed offline by `analyze.py`.
- `Experiment/Recording/TwinPaperRecorder` writes `session_manifest.json`, `manifest.json`, `unity_receive.csv`, `unity_apply.csv`, and `unity_event.csv`.
- CSV schema changes must update `TwinPaperRecorder.SchemaVersion` and `analyze.py`.

## Default Priorities

- `TwinRuntimeProfile.asset` is the single scene-facing entry, but its live settings are split into sub assets under `DigitalTwin/Config/Profiles`.
- Runtime builds a `TwinRuntimeSettings` snapshot on `OnEnable`, then uses that snapshot for module resolution and hot-path switches.
- Keep `TwinRecorder`, `RobotIkController`, SQLite replay switches, Ghost robot, and `Ros2Bridge` out of the main experiment hot path unless explicitly enabled.
- Keep Unity-side paper experiment scripts under `DigitalTwin/Scripts/Experiment`.
- Prefer `Experiment/Replay/ReplayStateSource` for reproducible demos and offline UI/model validation.
- Keep new top-level folders limited to `Config`, `Communication`, `Control`, `CoreState`, `Experiment`, `Recording`, `RobotModel`, `Runtime`, and `UI`.
