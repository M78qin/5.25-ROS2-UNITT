# DigitalTwin PROGRESS

## 当前基线

当前可运行基线以 `Assets/DigitalTwin` 为准。系统目标是 H2515 机械臂数字孪生：DartStudio 向 Unity 实时推送关节角和 6 轴力/力矩，Unity 显示 Live Robot、记录 CSV、计算延迟/丢帧/抖动等指标，并预留 Unity -> DartStudio 双向控制入口。

最近完成两件关键整理：

- `JointMappingConfig` 已删除。关节绑定、方向、零点、限位统一由 `RobotModelController` 维护，以 URDF Import 后的 `ArticulationBody` 为准。
- Runtime HUD V2 已落地。右侧工业 HUD 支持卡片式模块、拖拽、缩放、折叠、Slider debounce、REAL ON 二次确认。

本轮 HUD 改造只动 UI 层和 Editor Builder，没有改：

```text
DartStudioBridge
DigitalTwinRuntime
TwinCommandController
TwinRecorder
RobotModelController
RobotIkController
URDF / Articulation / 通信协议 / IK / 轨迹规划
```

## 当前配置

只保留两个必需配置资产：

```text
DigitalTwin/Config/TwinRuntimeProfile.asset
DigitalTwin/Config/RobotSignalSchema.asset
```

`DigitalTwinRuntime` 需要绑定：

```text
Profile        -> TwinRuntimeProfile.asset
Schema         -> RobotSignalSchema.asset
DartStudioBridge
RobotModel
Recorder
UIController
CommandController
```

不再需要 `Joint Mapping` 字段。

`RobotSignalSchema` 只是通信数据格式说明，不是功能开关。当前最小有效 DartStudio UDP 包只需要：

```text
joint_states.position   6 个关节角，degree
tool_force              [Fx,Fy,Fz,Tx,Ty,Tz]
```

可选增强字段：

```text
seq
ts_ms
mode
motion
joint_states.velocity
joint_states.effort
tcp_pose
extra signals
```

没有这些可选字段时，Unity 仍应能同步关节和 6 轴力。

## 脚本结构

```text
DigitalTwin/Scripts/
  Runtime/
    DigitalTwinRuntime.cs
    RuntimeServices.cs
  Communication/
    DartStudioBridge.cs
    DartStudioProtocol.cs
    RobotCommunicationContracts.cs
  CoreState/
    RobotStateTypes.cs
    RobotStateBus.cs
  RobotModel/
    RobotModelController.cs
    EditorRobotPoseTool.cs
    RobotIkController.cs
  Control/
    TwinCommandController.cs
    PlanningControlService.cs
    SafetyCheckService.cs
  Recording/
    TwinRecorder.cs
  Experiment/
    Contracts/
      ExperimentRecordingContracts.cs
      PaperExperimentDefaults.cs
    Session/
      ExperimentSessionController.cs
    Recording/
      TwinPaperRecorder.cs
    Metrics/
      TwinExperimentTracker.cs
      JointDriftMonitor.cs
    Replay/
      ReplayStateSource.cs
  UI/
    TwinUIController.cs
    TwinRuntimeUIFactory.cs
    TwinRuntimeUICard.cs
    TwinRuntimeUIPanelInteractor.cs
    TMP_TextExtensions.cs
```

Editor 入口：

```text
DigitalTwin/Editor/TwinRuntimeUIBuilderEditor.cs
```

## 推荐挂载

```text
DigitalTwin_System
  DigitalTwinRuntime

Communication_System
  DartStudioBridge

Planning_Control_System
  TwinCommandController

DataLogging_System
  TwinRecorder

UI_System
  TwinUIController

Robot_H2515_Real
  RobotModelController

Experiment_System (可选，实验时挂载)
  TwinExperimentTracker
```

可选开发脚本：

```text
Robot_H2515_Real
  EditorRobotPoseTool
  RobotIkController
```

## Runtime HUD V2

推荐使用新菜单生成：

```text
选中带 TwinUIController 的 UI_System
GameObject > Digital Twin > Create Runtime HUD
```

生成对象：

```text
H2515_DigitalTwinRuntimeCanvas
  H2515_DigitalTwinRuntimePanel
```

旧入口仍保留兼容：

```text
GameObject > Digital Twin > Create Runtime UI For Selected Runtime
```

但后续推荐统一使用 `Create Runtime HUD`。

HUD 文件职责：

```text
TwinUIController.cs
  数据刷新、按钮事件、刷新节流、Slider debounce、REAL ON 确认。

TwinRuntimeUIFactory.cs
  生成 HUD Canvas / Panel / Card / Button / Slider。

TwinRuntimeUIBindings
  定义在 TwinRuntimeUIFactory.cs 中；作为 Panel 上的绑定组件，保存所有 Text/Button/Slider/Card 引用，并提供 Validate。

TwinRuntimeUICard.cs
  CardState 状态机：Hidden / Collapsed / Expanded。

TwinRuntimeUIPanelInteractor.cs
  面板拖动、右下角缩放、最小化。

TMP_TextExtensions.cs
  SetIfChanged，避免高频无意义 TMP 文本写入。

TwinRuntimeUIBuilderEditor.cs
  Editor 菜单生成、清理旧 Canvas、自动绑定、Console 校验绑定缺失。
```

HUD 运行机制：

```text
Joints / ForceTorque       20 Hz
CommandSafety             10 Hz
Connection                5 Hz
Metrics                   3 Hz
CSV                       1 Hz
```

规则：

- Card 为 `Hidden` 或 `Collapsed` 时，不刷新 Body。
- 外部关节同步使用 `Slider.SetValueWithoutNotify(currentJointAngle)`。
- 用户拖动 Slider 后，100ms debounce，再调用 `TwinCommandController.UpdatePlanTarget()`。
- Online / STALE / RECONNECT / INIT 使用不同颜色，断线时不能保留最后一帧绿色。
- `EnableRealRobotCommand=true` 且 `EnableDryRun=false` 时，Execute 按钮变红并弹出纯 uGUI 二次确认框。

## 当前数据流

```text
DartStudio bridge.py
  -> UDP 9090 robot_state JSON
  -> DartStudioBridge.ReceiveLoop()
  -> ConcurrentQueue<RawSourcePacket>
  -> DigitalTwinRuntime.Update()
  -> StateFrameNormalizer
  -> RobotStateFrame
  -> StateFrameBus latest frame
  -> RobotModelController / TwinRecorder / TwinUIController / Metrics
```

命令流：

```text
TwinUIController / future IK
  -> TwinCommandController
  -> PlanningControlService
  -> SafetyCheckService
  -> DartStudioBridge TCP 9092
  -> DartStudio SET_MODE / MOVE_JOINT / HALT
```

## 安全默认值

```text
enableBidirectionalControl = false
enableRealRobotCommand = false
enableDryRun = true
```

默认不会真实运动。真实机械臂执行前必须明确打开双向控制和真实命令，并关闭 dry-run。

`mode1_test` 会让真实臂执行 A/B 来回测试，必须确认安全空间后再启用真实命令。

## 后续待做

1. 在 Unity 中刷新编译，处理可能的 C# 报错。
2. 选中 `UI_System`，用 `Create Runtime HUD` 重新生成 HUD 并保存场景。
3. Play 后验证 HUD 拖动、缩放、最小化、卡片折叠。
4. 验证 DartStudio online/stale/reconnect 颜色状态。
5. 验证 Plan 状态下 Slider 显示，非 Plan 状态隐藏。
6. 验证 REAL ON 且 dry-run 关闭时，Execute 弹二次确认。
7. 增加“解除急停”UI。
8. 给 `DartStudioBridge` 增加接收队列上限。
9. 读取 DartStudio TCP ACK/ERROR。
10. 接入完整 Ghost 克隆和 IK 拖拽预览。

## 后续 AI 修改硬规则

- 不新增第二套 DartStudio socket。
- 不新增第二套 Joint Mapping 资产。
- UI 不直接发 TCP/UDP。
- IK / Slider / Ghost 不直接发真实命令。
- 通信层不直接写 `ArticulationBody`。
- 关节方向、零点、限位只在 `RobotModelController` 处理。
- 高频路径不加频繁 `Debug.Log`。
- 修改 HUD 时优先在 `DigitalTwin/Scripts/UI/` 内处理，不要动通信、Runtime、命令、安全、记录、模型、IK。

## 2026-05-10 Update (Compressed Context)

### Done (today)

1. DartStudio TCP command build/sender layer was refactored without creating a second TCP client.
   - Added `DigitalTwin/Scripts/Communication/lib/DartTcpCommandBuilder.cs`
   - Added `DigitalTwin/Scripts/Communication/lib/DartTcpCommandSender.cs`
2. `DartStudioBridge` command path now reuses the builder and exposes raw send entry.
   - Added `SendRawCommand(string json)` and `SendGetState()`
   - `SetMode / SendMoveJoint / SendHalt` now build command via builder
3. Runtime inspector quick-send panel was added for direct testing in Play mode.
   - Added `DigitalTwin/Scripts/Communication/lib/Editor/DartTcpCommandSenderEditor.cs`
   - Supports preset buttons: `Mode Idle/Test/Ctrl`, `Move A/B`, `HALT`, `GET_STATE`, `Send RAW`
4. Dry-run command JSON consistency was unified in `TwinCommandController`.
   - `BuildMoveJointJson()` now reuses `DartTcpCommandBuilder.BuildMoveJoint(...)`
5. CSV output default path was redirected to:
   - `D:\Unity Projects\DART_R-data`
   - Implemented in `DigitalTwin/Scripts/Recording/TwinRecorder.cs`

### Key diagnosis

Observed symptom:
- Console shows `mode=mode1_test motion=MOVING`, but robot model appears not moving.

Root cause identified in scene data:
- `EditorRobotPoseTool.enableRuntimeManualJointControl=1` on `base_link` can seize runtime model authority, blocking `RuntimeLive` frame application.
- Scene ref: `Scenes/SampleScene.unity` (`base_link` object block).

Recommended runtime fix:
1. Disable `Enable Runtime Manual Joint Control` on `EditorRobotPoseTool`.
2. Ensure `RobotModelController` authority is `LiveFeedback` (or call `ReleaseToLiveFeedback`).

### Quick handoff for next session

1. Verify `DartTcpCommandSender` runtime panel sends in Play mode and check `LastCommandStatus`.
2. Run mode switch + movement regression:
   - `Mode Ctrl -> Move A/B -> Halt -> Get State`
3. Confirm new CSV generation under:
   - `D:\Unity Projects\DART_R-data\frames_*.csv`
4. Keep `DartStudioBridge` as the main bridge; `EX-UES` scripts remain transitional and optional.

## 2026-05-11 Start Handoff

### Current truth

1. 主进度文件以本文件为准：`Assets/DigitalTwin/PROGRESS.md`。
2. 旧路径 `Assets/Docs/05_实验推进/PROGRESS.md` 只是入口提示，不再承载详细进度。
3. 场景内 `Scenes/SampleScene.unity` 已确认 `EditorRobotPoseTool.enableRuntimeManualJointControl=0`，之前会抢占 Live 模型控制权的风险点当前处于关闭状态。
4. TCP 命令测试入口已存在：`DartTcpCommandSender` + `DartTcpCommandSenderEditor`，用于 Play 模式下快速发送 `Mode Idle/Test/Ctrl`、`Move A/B`、`HALT`、`GET_STATE`、RAW JSON。
5. CSV 默认输出目录仍是 `D:\Unity Projects\DART_R-data`。

### Recommended next push

1. 打开 Unity，等待 C# 编译完成，先处理 Console 中的编译错误。
2. 进入 Play，选中挂有 `DartTcpCommandSender` 的对象，用 Inspector quick-send 面板验证命令链路。
3. 按顺序跑一遍：`Mode Ctrl -> Move A -> Move B -> HALT -> GET_STATE`。
4. 同时观察三处：
   - DartStudio 是否进入目标模式并反馈 `motion=MOVING/IDLE`。
   - Unity Live Robot 是否跟随 UDP `joint_states.position` 更新。
   - `DartStudioBridge.LastCommandStatus / LastCommandError` 是否符合预期。
5. 验证 CSV 是否在 `D:\Unity Projects\DART_R-data\frames_*.csv` 生成，并检查前几行是否包含关节角、力/力矩、模式、motion。
6. 如果 DartStudio 已显示运动但 Unity 模型仍不动，优先检查 `RobotModelController.CanAcceptPoseSource(RuntimeLive)` 和当前 `ControlAuthority`，不要新建第二套 socket 或 Joint Mapping。

### Good first implementation tasks

1. 给 `DartStudioBridge` 接收队列增加上限，避免 UDP 长时间堆积。
2. 读取 DartStudio TCP ACK/ERROR，并把状态汇入 `LastCommandStatus / LastCommandError`。
3. 增加”解除急停”UI，但仍走 `TwinCommandController -> DartStudioBridge`，UI 不直接发 TCP。
4. 接入 Ghost 克隆和 IK 拖拽预览；预览只更新计划目标或预览姿态，不直接发送真实机械臂命令。

## 2026-05-11 Experiment Framework

### Done

Python 端和 Unity 端实验框架已完成，支持端到端数字孪生通信指标测量。

**Unity 端修改清单：**

| 文件 | 改动 |
|---|---|
| `CoreState/RobotStateTypes.cs` | RobotStateFrame 加 `ExperimentId`, `ExperimentType`, `SendPerfNs` 字段 |
| `Communication/DartStudioProtocol.cs` | StateFrameNormalizer 传递 `exp` 字段和 `session_id` 到 RobotStateFrame |
| `Communication/DartStudioBridge.cs` | 添加 `_experimentTracker`，帧处理和 PONG 时通知 Tracker |
| `Config/TwinRuntimeProfile.cs` | 加实验配置（`enableExperimentTracking`, `enableExperimentCsv`, `showExperimentPanel`） |
| `Recording/TwinRecorder.cs` | 新增实验 CSV 日志（`StartExperimentLog`, `EnqueueExperimentAck`, `EnqueueJointSyncError`） |
| `Runtime/DigitalTwinRuntime.cs` | 初始化 Tracker，注入到 Bridge/UI，自动启动实验 CSV |
| `UI/TwinUIController.cs` | 实验状态面板（ID、包数、PONG 数、RTT、OneWay、关节误差） |

**新增文件：**
- `Scripts/Experiment/Metrics/TwinExperimentTracker.cs` — 实验指标聚合器

**数据流：**
```text
Python exp.type=STATE → DartStudioBridge.TryDequeueFrame → StateFrameNormalizer (exp→frame) → Tracker.OnFrameReceived
Python exp.type=PING  → DartStudioBridge.TryHandlePing → PONG 回包 + Tracker.OnPongSent
```

**Unity 实验面板显示：**
```
EXPERIMENT
ID   rtt_20260511_143022
Pkts 1234  Pongs 1200
RTT  2.3ms  mean 2.1ms
p95  4.1ms  max 8.2ms
OneWay 1.1ms
JointErr max 0.120  rms 0.085
```

### Verification

1. Python `--mode rtt --ack`，Unity Play 模式下观察实验面板
2. Python `--mode oneway`，Unity 显示单向延迟
3. 检查 `D:\Unity Projects\DART_R-data\exp_ack_*.csv` 内容
4. `--mode idle` 时 Unity 无实验面板（向后兼容）

### Next

1. 打开 Unity，等待 C# 编译完成，处理可能的编译错误
2. 场景中挂载 `TwinExperimentTracker` 组件（任意 GameObject）
3. Python 端运行 `--mode basic --freq 10 --count 10` 快速验证
4. 逐步验证 rtt / oneway / joint_sync 模式

## 2026-05-11 End-to-End Logging System (晚间)

### 目标

Python 和 Unity CSV 按 seq 合并，计算真实端到端指标。

### 修复核心 BUG：unity_apply_ns=0

**根因：** `PublishFrame()` clone 到 recorder 时 `UnityApplyTimestampNs=0`，`ApplyLatestToRobot()` 设置的是 bus 里的另一份 clone。

**修复：** `DrainSourceFrames()` 存帧到 `_pendingRecord` 字典；`ApplyLatestToRobot()` apply 后同步 apply timestamp 再 enqueue；`FlushStalePendingFrames()` 处理被覆盖的旧帧。

### Unity 修改清单

| 文件 | 改动 |
|---|---|
| `CoreState/RobotStateTypes.cs` | +`UnityReceiveWallMs`, `UnityApplyWallMs` 字段；`MarkAppliedNow()` 记 wall-clock；`RawSourcePacket` +`ReceiveWallMs` |
| `Communication/DartStudioBridge.cs` | `ReceiveLoop()` 记 `SystemClock.UtcNowMs()`；`TryDequeueFrame()` 传 wall_ms |
| `Communication/DartStudioProtocol.cs` | `TryFromDartJson()` 新增 `receiveWallMs` 参数 |
| `Runtime/DigitalTwinRuntime.cs` | 延迟 recorder enqueue（`_pendingRecord` + `FlushStalePendingFrames`）；`PublishFrame()` 不再直接 enqueue |
| `Recording/TwinRecorder.cs` | CSV 新增 `unity_receive_wall_ms`, `unity_apply_wall_ms`, `payload_bytes`, `source_session_id`；session_id 优先用 Python ExperimentId |

### Unity CSV 新格式

```
session_id,session_name,source,sequence,source_time_s,source_time_ms,
unity_receive_ns,unity_receive_wall_ms,unity_apply_ns,unity_apply_wall_ms,
flags,mode,motion,motion_error,joint_1_rad,...,joint_6_rad,
force_x,...,tcp_qw,payload_bytes,source_session_id
```

### 端到端指标（analyze.py --end-to-end）

| 指标 | 计算 |
|---|---|
| receive_latency_ms | unity_receive_wall_ms - source_time_ms |
| display_latency_ms | unity_apply_wall_ms - source_time_ms |
| unity_process_ms | (unity_apply_ns - unity_receive_ns) / 1e6 |
| receive_interval_ms | diff(unity_receive_ns) by seq |
| joint_error | \|py_deg→rad - unity_rad\| per joint (MAE/RMSE/Max) |
| loss/dup rate | py_only / unity_only vs total_sent |

### 待验证

1. Unity C# 编译通过
2. Python `--mode basic --freq 10 --count 50 --send-log` + Unity Play
3. 确认 unity_apply_ns 不再为 0
4. `analyze.py --logs-dir logs --unity-csv <path> --end-to-end` 输出端到端指标
5. 断开重连测试 GAP 标记

## 2026-05-12 Project Sync

### 当前基线

1. `Assets/DigitalTwin/PROGRESS.md` 仍作为项目进度单一来源。
2. 从 `Assets` 和 Unity 项目根目录检查时，当前目录不在 git 仓库内；本次同步基于本地文件和场景数据，不基于 git 历史。
3. `Scenes/SampleScene.unity` 中检查到的 `EditorRobotPoseTool.enableRuntimeManualJointControl=0`，之前会抢占 LiveFeedback 控制权的风险点当前仍处于关闭状态。

### 已确认完成

1. Runtime HUD V2 已在 `DigitalTwin/Scripts/UI/` 下落地，包含卡片刷新节流、面板拖拽/缩放、Slider debounce、REAL ON 二次确认。
2. TCP 命令辅助层已存在：
   - `DigitalTwin/Scripts/Communication/lib/DartTcpCommandBuilder.cs`
   - `DigitalTwin/Scripts/Communication/lib/DartTcpCommandSender.cs`
   - `DigitalTwin/Scripts/Communication/lib/Editor/DartTcpCommandSenderEditor.cs`
3. `DartStudioBridge` 仍是 DartStudio 主通信入口，并已暴露 `SendRawCommand(...)` / `SendGetState()` 供 quick-send 测试。
4. 实验框架已接入：
   - `DigitalTwin/Scripts/Experiment/Metrics/TwinExperimentTracker.cs`
   - profile 开关 `enableExperimentTracking`、`enableExperimentCsv`、`showExperimentPanel`
   - `TwinUIController` 中的实验面板刷新路径
5. 端到端 CSV logging 改造已在代码中体现：
   - receive/apply wall-clock 字段
   - `_pendingRecord` 延迟 recorder enqueue
   - `payload_bytes` 和 `source_session_id`
   - experiment ACK / joint-sync CSV 队列支持

### 当前验证重点

1. 打开 Unity，等待 C# 编译完成。
2. 先处理 Console 编译错误，再做通信链路测试。
3. 确认当前场景挂载了 `TwinExperimentTracker`，或至少能被 `DigitalTwinRuntime` 找到。
4. Play 模式下验证 `DartTcpCommandSender` quick-send：
   - `Mode Ctrl -> Move A -> Move B -> HALT -> GET_STATE`
   - 同时观察 DartStudio mode/motion、Unity Live Robot 动作、`LastCommandStatus / LastCommandError`
5. Python send-log + Unity Play 验证：
   - `--mode basic --freq 10 --count 50 --send-log`
   - 确认 `unity_apply_ns` 不再为 `0`
6. 运行端到端分析：
   - `analyze.py --logs-dir logs --unity-csv <path> --end-to-end`
7. 检查 `D:\Unity Projects\DART_R-data` 下的 CSV，重点看 `frames_*.csv` 和 `exp_ack_*.csv`。

### 推荐下一步实现

1. 给 `DartStudioBridge` 接收队列增加上限，可参考 `Ros2Bridge` 现有 bounded queue 思路。
2. 读取 DartStudio TCP ACK/ERROR，并汇入 `LastCommandStatus / LastCommandError`。
3. 增加“解除急停”UI，但必须走 `TwinCommandController -> DartStudioBridge`，UI 不直接发 TCP。
4. 继续接入 Ghost 克隆和 IK 拖拽预览，保持预览态与真实机械臂命令执行分离。

## 2026-05-12 Priority Note

### 当前阶段约束

1. 当前优先级是先跑通实验数据链路、日志记录、Python/Unity CSV 合并分析和端到端指标。
2. UI/HUD/面板显示类功能暂时不作为当前推进重点；等数据链路稳定、实验流程跑通后再考虑显示层优化。
3. 任何暂时关闭的功能都必须做到低开销或零开销：关闭后不应继续进行高频 Update、Socket 处理、日志写入、指标计算、UI 刷新或后台线程占用。
4. 后续修改开关逻辑时，需要确认关闭路径不会影响主链路性能，尤其是记录、实验追踪、UI、Ghost/IK、ROS2 预留源等可选模块。

## 2026-05-12 Clock/Metrics Calibration

### Implemented

1. 统一时钟语义：跨进程端到端延迟只用 Unix wall-clock；Unity 内部 receive/apply 耗时仍用 `SystemClock.NowNs()`。
2. `RobotStateFrame` 增加 `SendWallMs`，并保留 `SendPerfNs` 只作源进程内 monotonic 时间，不再与 Unity monotonic 时间相减。
3. `TwinExperimentTracker.LastOneWayMs` 改为 `UnityReceiveWallMs - SourceTimestampMs`，仅在 `ClockSyncStatus=Synced` 时显示。
4. `TwinRecorder` 主 CSV 增加 `clock_sync_status`；实验 CSV 改为记录 `source_wall_ms / unity_receive_wall_ms / unity_apply_wall_ms / unity_process_ms`，不再输出跨进程假 RTT。
5. `Ros2Bridge` 补写 `UnityReceiveWallMs`，并根据 ROS header stamp 与 Unity wall-clock 的偏差设置 `ClockSyncStatus`。
6. `DartStudioBridge` 增加 UDP raw queue 上限，超限丢弃旧包，避免高频实验积压。
7. Python DartStudioMock 发实验包时增加 `exp.send_wall_ms` 和 send log 的 `send_wall_ms` 列。
8. Python `analyze.py` 改为按 `session_id + seq` 合并，端到端输出 `wall_receive_latency_ms / wall_display_latency_ms / unity_process_ms / python_rtt_ms`。
9. Runtime UI 仍保持可配置；默认 profile 已关闭 `enableVerboseLog`，避免 UI 关闭时继续低频 Console 状态日志影响实验。

### Verification

1. Python 三个脚本已通过纯 `compile()` 语法检查：
   - `experiment.py`
   - `dartstudio_mock.py`
   - `analyze.py`
2. `python -m py_compile` 因外部目录 `__pycache__` 权限报错，非脚本语法错误。
3. Unity C# 仍需打开 Unity 等待编译验证。
## 2026-05-12 200Hz Configurable Experiment Framework

### Implemented

1. Python DartStudioMock added `--profile` as the main experiment entry while keeping the old CLI arguments compatible.
2. Added the default experiment profiles:
   - `experiments/profiles/basic_joint_30hz.yaml`
   - `experiments/profiles/joint_force_100hz.yaml`
   - `experiments/profiles/full_payload_200hz.yaml`
   - `experiments/profiles/dual_source_60hz.yaml`
3. Python payload switches are now field-level: `joint_position / joint_velocity / joint_effort / tool_force / tcp_pose / extra_signals`. Disabled fields are not generated or serialized; CSV headers stay stable and disabled fields are left blank.
4. Python state CSV uses batched flush through profile-controlled `flush_interval_rows`; per-frame flush was removed from the hot path.
5. Experiment meta and summaries now include `profile_name / run_id / repeat_index / payload_channels` for repeatable paper experiments and table aggregation.
6. Unity `TwinRuntimeProfile` added `recordAllReceivedFrames`: enabled for data-completeness experiments, disabled for latest-frame-only realtime display tests.
7. Unity `TwinRecorder` now enforces `recordQueueHardLimit` by dropping oldest queued frames, protecting 200Hz tests from recorder backlog.
8. Unity recorder CSV now includes joint velocity and joint torque columns, and leaves TCP pose blank when that payload is not enabled.
9. DartStudio JSON parsing now tolerates missing `joint_states.position`, so velocity/effort-only extension packets remain valid.
10. ROS2Mock also accepts the shared `--profile` entry, applies the same payload switches to JointState/Wrench/TCP topics, and removes per-frame CSV flush.
11. Unity `Ros2Bridge` now accepts JointState packets where position is absent but velocity/effort is present.

### Verification

1. Python syntax checks passed for `experiment.py`, `experiment_profile.py`, `dartstudio_mock.py`, and `analyze.py`.
2. `dartstudio_mock.py --help` shows the new `--profile` entry and `joint_force` payload.
3. `full_payload_200hz` profile loads as `200Hz + joint_position/joint_velocity/joint_effort/tool_force/tcp_pose`.
4. ROS2Mock syntax check passed for `ros2_mock_node.py`.
5. Unity C# still needs Unity Editor compilation and Play Mode validation before running the full 30/100/200Hz experiment ladder.

### Tomorrow Handoff

1. Open Unity first and wait for C# compilation; fix compile errors before running any experiment.
2. Keep `enableRuntimeUI=0`, `enableVerboseLog=0`, and debug overlay off for performance validation.
3. Run the ladder in order: `basic_joint_30hz`, `joint_force_100hz`, then `full_payload_200hz`.
4. For each run, collect Python `exp_send_*.csv`, Unity `frames_*.csv`, and execute `analyze.py --end-to-end`.
5. Only run `dual_source_60hz` after the single-source DartStudio path is stable.

## 2026-05-13 Bidirectional Experiment Session Control

### Implemented

1. Added a thin Unity `ExperimentSessionController` under `DigitalTwin/Scripts/Experiment/`.
   - Reuses `DartTcpCommandSender` / `DartStudioBridge.SendRawCommand(...)`.
   - Does not create a new TCP socket.
   - Provides Inspector buttons for connect, session, stream, record, phase/event, and network impairment commands.
2. Added an Editor inspector panel for the new controller.
3. Extended `RobotStateFrame` and `DartStudioProtocol` with paper-grade session metadata:
   - `source_id`
   - `source_session_id`
   - `experiment_id`
   - `phase_id`
   - `segment_id`
   - `stream_enabled`
   - `record_enabled`
   - `event_type`
   - `notes`
4. Extended `TwinRecorder` without adding a second CSV system.
   - Frames CSV appends session/phase/segment/record fields.
   - `record_enabled=false` returns before enqueue, so CSV rows are not built while recording is paused.
   - Existing writer thread and batching remain in use.
5. Python DartStudioMock now supports lifecycle commands:
   - `HELLO`
   - `GET_STATUS`
   - `CREATE_SESSION`
   - `CLOSE_SESSION`
   - `START_STREAM / PAUSE_STREAM / RESUME_STREAM / STOP_STREAM`
   - `START_RECORD / PAUSE_RECORD / RESUME_RECORD / STOP_RECORD`
   - `NEW_PHASE / MARK_EVENT`
   - `SET_NETWORK_IMPAIRMENT / RESET_NETWORK_IMPAIRMENT`
6. Python manual mode now waits after startup.
   - No formal session CSV is created until `CREATE_SESSION`.
   - No telemetry is sent until `START_STREAM`.
   - No state rows are written until `START_RECORD`.
7. Existing profile experiments remain compatible.
   - `basic_joint_30hz` and similar profile runs still auto-start stream/record.
   - When a profile experiment ends, Python switches back to a normal non-experiment session id to avoid polluting experiment analysis.
8. `analyze.py` now resolves duplicate Unity seq rows by choosing the row closest to Python send time, preventing normal-stream seq reuse from creating fake multi-second latency.
9. `analyze.py` now prints summaries even if the reports/logs directories reject output file writes.

### Verification

1. Python syntax check passed for `dartstudio_mock.py` and `analyze.py`.
2. Re-ran end-to-end analysis on `basic_86ed73a20513`; duplicate seq pollution is fixed:
   - total_sent = 1736
   - total_received = 1736
   - packet_loss_rate_pct = 0
   - display latency p95 ~= 18.69 ms
   - unity_process p95 ~= 19.28 ms
3. Unity C# still needs Unity Editor compilation validation.

### Next Validation Steps

1. Add `ExperimentSessionController` to the same scene object area as the existing command sender/communication system.
2. Enter Play Mode and run:
   - `Connect`
   - `Create Session`
   - `Apply Channels`
   - `Start Stream`
   - `Start Record`
   - `Pause Record`
   - `Resume Record`
   - `New Phase`
   - `Mark Event`
   - `Stop Record`
   - `Stop Stream`
   - `Close Session`
3. Confirm that CSV row count stops during `Pause Record` while Unity display keeps moving.

## 2026-05-14 Industrial/Paper-Grade Architecture Consolidation

### Implemented

1. Directory layout was consolidated for the DigitalTwin runtime.
   - `PlanningControl/` was renamed to `Control/`.
   - Existing experiment controller scripts were moved into `Control/`.
   - `.meta` files were moved with the scripts so Unity GUID references should remain stable.
   - Current top-level runtime folders are now: `Communication`, `Control`, `CoreState`, `Recording`, `RobotModel`, `Runtime`, `UI`.
2. Small fragmented contracts were merged.
   - `IRobotStateSource` and `IRobotCommandSink` now live in `Communication/RobotCommunicationContracts.cs`.
   - `IExperimentRecorder` now lives in `Experiment/Contracts/ExperimentRecordingContracts.cs`.
   - `DigitalTwinModeService` and `FeatureSwitchService` now live in `Runtime/RuntimeServices.cs`.
3. `RobotStateBus` was upgraded toward a true double-buffer model.
   - Runtime publishes into a write buffer.
   - Main-thread `Swap()` exposes a read buffer for model/UI/recorder consumers.
   - Publish time is stamped at bus publish point via `UnityPublishTimestampNs` and `UnityPublishWallMs`.
4. Core frame data was extended for paper analysis.
   - Added `FrameQuality`: `Normal`, `Delayed`, `Duplicated`, `Interpolated`, `Lost`.
   - Added `SourceHealth` as the unified source health contract.
   - `DartStudioBridge`, `Ros2Bridge`, and `ReplayStateSource` expose `GetHealth()`.
5. `TwinPaperRecorder` schema was upgraded.
   - New schema version: `unity_paper_v2`.
   - Writes both `session_manifest.json` and legacy-compatible `manifest.json`.
   - CSV now records receive, publish, and apply timestamps.
   - CSV now includes runtime publish delay, apply delay ingredients, and `frame_quality`.
6. `TwinExperimentTracker` was reduced toward event-marker semantics.
   - Runtime no longer recomputes p95/mean/max RTT statistics for paper metrics.
   - Compatibility properties remain so current UI does not break.
   - Added `MarkEvent(tag, timestamp)` and `MarkPhase(phase)`.
7. Replay and calibration foundations were added.
   - Added `Experiment/Replay/ReplayStateSource.cs`.
   - `ReplayStateSource` reads recorded paper CSV and feeds the normal `IRobotStateSource -> DigitalTwinRuntime -> RobotStateBus -> RobotModelController` path.
   - `TwinRuntimeProfile` gained replay switches: `enableReplayStateSource`, `replayCsvPath`, `replayHz`, `replayLoop`.
   - `RobotSignalSchema` gained `calibrationVersion` and `calibrationOffsetDeg`.
   - `RobotModelController` applies calibration offsets only to model display; raw CSV remains unmodified.
   - Added disabled-by-default `Experiment/Metrics/JointDriftMonitor.cs`.
8. Command echo event logging was added.
   - `ExperimentSessionController` now writes `COMMAND_ECHO` records to `unity_event.csv`.
   - Event notes include `cmd_seq`, action, send wall time, status, dry-run flag, success flag, expected joint placeholder, and error.
9. Architecture README was added.
   - `DigitalTwin/README.md` documents Data Plane, Control Plane, and Experiment Plane.
   - It also records the rule that Unity records raw facts while `analyze.py` computes paper metrics.
10. Python analyzer compatibility was updated.
    - `E:\zyq\DRL_3.12-\5.10-TW\Test\DartStudioMock\src\analyze.py` now reads `session_manifest.json`.
    - It handles `unity_publish_ns`, `unity_publish_wall_ms`, `frame_quality`, `runtime_publish_ms`, and `apply_delay_ms`.
    - Legacy `manifest.json` and old Unity paper logs remain compatible.

### Verification

1. Python analyzer syntax check passed:
   - `python -m py_compile E:\zyq\DRL_3.12-\5.10-TW\Test\DartStudioMock\src\analyze.py`
2. Analyzer help command passed:
   - `python ...\analyze.py --help`
3. Re-ran old end-to-end data successfully:
   - Python logs: `E:\zyq\DRL_3.12-\5.10-TW\Test\DartStudioMock\Log\05141905\exp_manual\exp_manual_20260514_110545`
   - Unity paper logs: `D:\Unity Projects\DART_R-data\PaperLogs\05141905\exp_manual\exp_manual_20260514_110545`
   - Output: `...\reports\e2e_summary.json`
4. Old data remains compatible.
   - `runtime_publish_ms` and `apply_delay_ms` are empty on old v1 data because publish timestamps did not exist yet.
   - New runs should populate these fields.
5. Unity C# still requires Unity Editor compilation validation.

### Important Path Notes

1. IDE tabs may still show old paths, but the current source paths are:
   - `DigitalTwin/Scripts/Communication/RobotCommunicationContracts.cs`
   - `DigitalTwin/Scripts/Experiment/Metrics/TwinExperimentTracker.cs`
   - `DigitalTwin/Scripts/Experiment/Session/ExperimentSessionController.cs`
   - `DigitalTwin/Scripts/Experiment/Session/Editor/ExperimentSessionControllerEditor.cs`
2. The old `DigitalTwin/Scripts/Communication/Interfaces/IRobotCommandSink.cs` path has been merged away.
3. `DigitalTwin/Scripts/Experiment/` is restored as the Unity-side paper experiment layer. Python/DartStudioMock remains outside Unity.

### Tomorrow Handoff

1. Open Unity first and wait for C# compilation.
2. Fix any compile errors before running Play Mode or changing architecture again.
3. If compile passes, run the manual paper session flow:
   - `Connect`
   - `Create Session`
   - `Apply Channels`
   - `Start Stream`
   - `Start Record`
   - `Stop Record`
   - `Stop Stream`
   - `Close Session`
4. Confirm new Unity output contains:
   - `session_manifest.json`
   - `manifest.json`
   - `unity_receive.csv`
   - `unity_apply.csv`
   - `unity_event.csv`
5. Confirm new CSV headers include:
   - `unity_publish_wall_ms`
   - `unity_publish_ns`
   - `frame_quality`
6. Run `analyze.py --end-to-end` on the new run and check:
   - `runtime_publish_ms.count > 0`
   - `apply_delay_ms.count > 0`
   - `frame_quality_counts` is populated
7. After the live path is stable, test `ReplayStateSource` with the newly generated `unity_receive.csv`.

### Known Risks / Watch Items

1. Unity compile is the first gate because several files moved folders and new C# types were added.
2. `ReplayStateSource` uses simple CSV splitting; current recorder escapes commas in fields, so this is acceptable for current paper CSV, but it should not be used with arbitrary quoted CSV yet.
3. `TwinExperimentTracker` keeps compatibility UI properties, but paper metrics should not use them anymore.
4. `RobotModelController` calibration offset affects display only; analyzer should continue using raw recorded values.
5. No git repository was detected earlier under the Unity project path, so this progress file remains the single local handoff source.

## 2026-05-15 Unity Paper Experiment Layer Reorganization

### Implemented

1. Restored `DigitalTwin/Scripts/Experiment/` as the Unity-side paper experiment entry.
   - `Contracts/` holds experiment-facing contracts and defaults.
   - `Session/` holds `ExperimentSessionController` and its Inspector panel.
   - `Recording/` holds `TwinPaperRecorder`.
   - `Metrics/` holds `TwinExperimentTracker` and `JointDriftMonitor`.
   - `Replay/` holds `ReplayStateSource`.
2. Moved existing `.cs` and `.meta` files together to keep Unity GUID references stable.
3. Kept namespace `DigitalTwin` unchanged, so existing scene/component references should deserialize normally.
4. Added `PaperExperimentDefaults` for the main paper hot path:
   - Dart source enabled.
   - `joint_position=true`.
   - `tool_force=true`.
   - velocity, effort, TCP pose, extra signals, and ROS2-like source disabled.
5. Added `Apply Main Paper Defaults` to the `ExperimentSessionController` Inspector for quick reset before manual runs.
6. Updated `TwinRuntimeProfile` defaults and the current profile asset so the main paper path does not auto-enable the legacy runtime recorder or runtime UI.
7. Added an `enableLegacyFrameRecorder` switch to `ExperimentSessionController`; it is default-off so `Start Record` only starts `TwinPaperRecorder` unless explicitly re-enabled.
8. Updated `Scenes/SampleScene.unity` so the serialized experiment controller has `toolForce=1`.
9. Added `DigitalTwin/Scripts/Experiment/README.md` to document folder roles, default hot-path switches, and extension rules.

### Verification Needed

1. Open Unity and wait for C# compilation.
2. Confirm `ExperimentSessionController`, `TwinPaperRecorder`, `TwinExperimentTracker`, and `ReplayStateSource` references survive the path move.
3. Run the manual paper session flow and verify `unity_receive.csv`, `unity_apply.csv`, and `unity_event.csv`.
4. Run `analyze.py --end-to-end` on a new session and confirm publish/apply metrics are populated.

## 2026-05-15 Runtime Profile Sub-Asset Split

### Implemented

1. Kept `TwinRuntimeProfile.asset` as the single scene-facing runtime config entry.
2. Added compact modular sub-profile assets under `DigitalTwin/Config/Profiles/`:
   - `TwinSystemProfile.asset`
   - `TwinExperimentProfile.asset`
   - `TwinPresentationProfile.asset`
3. Added `TwinRuntimeSettings` as an `OnEnable` runtime snapshot. `DigitalTwinRuntime` now uses this snapshot for module resolution and hot-path switches.
4. `TwinRuntimeProfile` still keeps legacy fields as a compatibility fallback, but now builds effective settings from sub-profiles when present.
5. Runtime syncs the effective settings back into legacy fields once during startup so existing modules remain compatible while the architecture migrates.
6. Updated Dart/ROS2/Replay/legacy recorder/paper recorder/UI profile reads to use effective settings.
7. `ExperimentRunProfile` / `PaperScenario` are intentionally deferred until the first real-machine live communication run is stable.

### Current Main Experiment Defaults

```text
enabled:
  Dart source
  Live robot sync
  Paper recorder
  joint_position
  tool_force

disabled:
  ROS2 source
  Replay source
  SQLite
  Runtime UI
  Ghost/IK runtime features
  Legacy frames_*.csv recorder
  Debug overlay
```

### Verification Needed

1. Open Unity and wait for C# compilation.
2. Confirm `TwinRuntimeProfile.asset` shows the three sub-profile references.
3. Confirm disabled modules do not initialize in Play Mode.
4. Run `Connect -> Create Session -> Apply Channels -> Start Stream -> Start Record`.
5. Confirm paper logs are generated without legacy `frames_*.csv` unless explicitly enabled.

## 2026-05-15 Runtime Profile Consolidation

### Implemented

1. Consolidated the previous seven small runtime sub-profiles into three high-cohesion assets:
   - `TwinSystemProfile.asset`
   - `TwinExperimentProfile.asset`
   - `TwinPresentationProfile.asset`
2. Removed the earlier tiny profile scripts and assets for source, robot view, command safety, legacy recording, paper recording, UI, and performance.
3. Kept one ScriptableObject class per file so Unity asset serialization remains stable.
4. Moved each profile's settings export logic into its own class via `ApplyTo(TwinRuntimeSettings)`, reducing `TwinRuntimeProfile` as the central pressure point.
5. Kept `TwinRuntimeProfile.asset` as the single scene-facing entry and kept `TwinRuntimeSettings` as the runtime snapshot.
6. Main paper defaults remain unchanged: Dart source, live robot sync, paper recorder, `joint_position`, and `tool_force` enabled; ROS2, replay, runtime UI, debug overlay, SQLite, and legacy recorder disabled.

### Verification Needed

1. Open Unity and confirm `TwinRuntimeProfile.asset` references exactly three sub-assets.
2. Confirm there are no missing scripts in `DigitalTwin/Config/Profiles`.
3. Run the existing manual paper session flow after C# compilation succeeds.

## 2026-05-15 配置脚本归档与中文 Inspector

### 已完成

1. 将 `DigitalTwin/Config/` 根目录下的配置脚本移动到 `DigitalTwin/Config/Scripts/`，并同步移动 `.meta`，保持 Unity GUID 不变。
2. `Config` 根目录现在只保留配置资产、README 和子文件夹，脚本与资产不再混放。
3. 为三个主子资产补充中文 `Header`、`Tooltip` 和 `InspectorName`：
   - `TwinSystemProfile.asset`：数据源、机器人显示、控制安全、性能。
   - `TwinExperimentProfile.asset`：论文记录、旧版记录、主实验通道。
   - `TwinPresentationProfile.asset`：Runtime UI、调试工具。
4. `TwinRuntimeProfile.asset` 的三个子资产引用也增加中文显示名和说明，方便只在总入口检查引用。
5. `Config/README.md` 与 `Config/Profiles/README.md` 已改成中文，说明当前主实验默认开关和后续 AI 修改规则。

### 待验证

1. 打开 Unity 等待 C# 编译，确认移动脚本后没有 missing script。
2. 在 Inspector 中点开 3 个 `Profiles/*.asset`，确认字段名和悬浮说明显示中文。
3. Play Mode 检查关闭模块仍不初始化，主实验日志仍输出 paper CSV。

## 2026-05-15 Python Mock 联调入口

### 已完成

1. 新增工程外模拟端 `Tools/dart_mock_100hz.py`，不放入 Unity `Assets`。
2. 模拟端提供：
   - UDP 9090：按 `--hz` 向 Unity 发送 `joint_states.position` 和 `tool_force`。
   - UDP 9091：接收 Unity heartbeat。
   - TCP 9092：接收 Unity 实验面板命令。
3. Unity 默认 `dartHz` 保持 30Hz；它只作为 `Apply Channels + Hz` 的下发请求值，不代表 Unity 接收频率。
4. `Create Session` 和 `Start Stream` 不再携带 channels/hz；只有 `SET_CHANNELS` 会改变 Python mock 的通道和发送频率。

### 待验证

1. 先运行 `python Tools/dart_mock_100hz.py --hz 80`。
2. Unity 进入 Play Mode，按 `Connect -> Create Session -> Start Stream`，确认 Python 仍保持约 80Hz。
3. 修改 Unity `dartHz` 后点击 `Apply Channels + Hz`，确认 Python 才切换频率。
4. 按 `Start Record` 后确认 Unity Live Robot 实时动，paper logs 正常写入。

## 2026-05-15 Python Mock 首轮论文指标复盘

### 本轮日志

最新有效会话：

```text
D:\Unity Projects\DART_R-data\PaperLogs\05151657\exp_manual\exp_manual_20260515_085811
```

输出文件齐全：

```text
manifest.json
session_manifest.json
unity_receive.csv
unity_apply.csv
unity_event.csv
```

### 指标结论

这轮可以证明 Unity 接收、应用、事件记录和 paper recorder 链路已经跑通，但暂时不能作为论文级最终样例。

```text
receive rows: 1279
apply rows:   1277
event rows:   14
seq gaps:     0
duplicates:   0
clock sync:   Synced 1279/1279
frame quality: Normal 1279/1279
receive hz:   30.02 Hz
apply hz:     29.99 Hz
runtime_publish_ms avg/p95: 3.735 / 7.609 ms
apply_delay_ms avg/p95:     10.265 / 20.070 ms
```

已验证通过：

1. UDP 状态帧进入 Unity，没有丢帧、重复帧或乱序。
2. Unity receive/apply/event 三类 CSV 都能写入。
3. `COMMAND_ECHO` 已记录 `SET_MODE`、`MOVE_JOINT`、`SET_CHANNELS`。
4. Runtime publish 与 apply 延迟指标已经有值，可以进入后续 analyzer 固化。

当前问题：

1. 本轮实际只跑到约 30Hz，不是高频基线；下一轮需要用 `--hz 80/100` 验证。
2. `tool_force` 没有进入 CSV，`force_x` 全空；论文主实验需要先修到关节角 + 六轴力同时有效。
3. `unity_event.csv` 的 `notes` 字段里 JSON 逗号被 CSV 转义成 `_`，后续 analyzer 解析不方便，需要修事件 notes 写法。
4. `manifest.json` 里的 `target_hz: 60` 容易误解为数据源真实频率；后续应区分 Unity apply/display target 与 source actual/request Hz。

### 下一步

进入 DartStudio 前先做三件小修：

1. 修 `unity_event.csv notes` 的 JSON/CSV 写入格式。
2. 确认 Python mock 和 DartStudio 状态包都稳定包含 `tool_force.force.x/y/z` 与 `tool_force.torque.x/y/z`。
3. 再跑一组 `--hz 80` 或 `--hz 100`，并通过 `Apply Channels + Hz` 切换频率，确认 receive Hz 跟随数据源实际频率。

修完后再开始将 Python mock 行为迁移到 DartStudio/真实机器人接口。

## 2026-05-15 DartStudio 单文件 V1-RC 评审同步

### 当前文件

```text
E:\zyq\DRL_3.12-\5.10-TW\dartstudio\digital_twin_robot_main_v1_rc.drl
```

配套文档：

```text
E:\zyq\DRL_3.12-\5.10-TW\dartstudio\architecture_note.md
E:\zyq\DRL_3.12-\5.10-TW\dartstudio\protocol_message_examples.md
E:\zyq\DRL_3.12-\5.10-TW\dartstudio\unity_interface_todo.md
E:\zyq\DRL_3.12-\5.10-TW\dartstudio\experiment_data_schema.md
```

### 当前判断

本版不再推倒重写。通信、遥测、Unity 兼容协议、状态机、记录骨架已经具备 DartStudio 联调条件；但仍建议保持 `V1-RC`，不要直接称为真机最终版。

核心原因：

1. 运动层已从阻塞式 `movej()` 主流程调整为 `amovej()` 异步启动 + `check_motion()` 主循环 tick 检查。
2. `MODE_TEST/back_and_forth` 已拆为小状态机，测试动作不再阻塞主循环。
3. `HALT` 已做高优先级直达：清普通命令队列并触发 `motion_abort()`。
4. 已加入 `stream_hz / joint_hz / force_hz / tcp_hz` 分层，区分 UDP 发布频率与内部传感器轮询频率。
5. `SET_CHANNELS` 仍兼容 Unity 当前 `dart_hz`，不需要 Unity 端立刻改协议。
6. DRL telemetry 已保留 `seq`、`ts_ms`、`sample_ts_ms`、`rates_hz`、`session_id`、`stream_enabled`、`record_enabled`、`tool_force.force/torque` 等论文字段。

### 已验证

1. `python -m py_compile E:\zyq\DRL_3.12-\5.10-TW\dartstudio\digital_twin_robot_main_v1_rc.drl` 通过。
2. `dartstudio` 目录下保持单 `.drl` 主文件，没有拆多个 DRL 模块。
3. 当前外部协议仍是 Unity 已跑通的 flat JSON：UDP 9090 状态流、UDP 9091 heartbeat/PING、TCP 9092 JSON Lines 命令、TCP 9093 预留。

### 真机/模拟器前剩余收口项

1. `HELLO` 不能单独代表 heartbeat 健康；`MOVE_JOINT` 前应要求 UDP heartbeat 时间戳有效且未超时。
2. `CLOSE_SESSION` 前 drain recorder 队列，避免 session 尾部事件丢失或落到下一次记录。
3. `sw_tcp=false` 时跳过 TCP pose API 轮询；`sw_force=false` 时可跳过 force API，减少关闭通道后的热路径负载。
4. `event_seq` 增加计数锁，保证多线程事件序号单调。
5. banner 建议从 `V1-Final-Rates` 改回 `V1-RC-Rates`，避免版本语义误导。
6. Unity 后续补 TCP ACK/REJECTED reader，把 DRL 返回的 accepted/rejected/status 写入 `COMMAND_ECHO` 和论文事件。

### 下一轮建议测试

1. DartStudio/模拟器启动 DRL，Unity 走 `Connect -> Create Session -> Apply Channels + Hz -> Start Stream -> Start Record`。
2. 分别跑 `stream_hz=30/80/100`，检查 Unity receive Hz、seq gap、duplicate、runtime publish delay、apply delay。
3. `mode1_test` 运动过程中连续发 `HALT`，确认不等当前运动完成即可停止。
4. `mode2_ctrl -> MOVE_JOINT` 运动过程中断开 Unity heartbeat，确认 3 秒 offline、8 秒 FAULT_SAFE 并停止运动。
5. 连续记录 30 分钟，检查 DRL jsonl 和 Unity paper CSV 均持续写入，队列不增长，seq 单调。

### 架构边界

当前阶段不要新增第二套 socket、不要新增第二套 joint mapping、不要把 Unity 改成直接控制机器人。继续保持：

```text
Unity = 显示、交互、记录、请求
DRL/Robot = 安全检查、状态机仲裁、真实执行、真实状态源
Analyzer = 离线指标计算与论文图表
```
## 2026-05-15 晚间 DartStudio 真环境联调记录

### 当前结论

Unity 与 DartStudio 的基础命令链路已经打通，但完整通信闭环还没有完全完成。DartStudio 端能收到 Unity 发出的生命周期和控制命令：

```text
HELLO
GET_STATUS
CREATE_SESSION
SET_CHANNELS
START_STREAM
START_RECORD
SET_MODE mode1_test
```

DRL 端也能进入：

```text
state=RUN_PRESET
mode=mode1_test
motion worker movej start id=preset_B
```

所以当前主要问题不是 Unity 端命令没有发到，也不是 TCP/UDP 端口完全不通，而是“命令送达 -> DartStudio 执行运动 -> 状态/ACK 回到 Unity -> 论文日志完整记录”的闭环仍未完全验证。当前最明确卡点是 DartStudio/H2515 真环境下 `movej` 运动 API 签名仍未最终确认。

### 今晚已尝试的 DRL 运动路径

1. `amovej(pos, vel, acc, mod)`：状态机可进入 `MOVING`，但 DartStudio 视图没有真实运动。
2. motion worker + `movej(pos, vel=20, acc=40, mod)`：报错 `Invalid value : vel, v([20.0,...])`。
3. motion worker + `movej(pos, vel=10, acc=10, mod)`：仍报 `Invalid value : vel, v([10.0,...])`。
4. `set_velj(10)` / `set_accj(10)` + `movej(pos, mod)`：仍触发内部 `vel` 报错，说明当前环境连默认关节速度路径也可能不兼容。
5. 当前文件已切到待测方案：`USE_TIME_BASED_MOVEJ = True`，即 `movej(pos=target_pos, time=5.0, mod=DR_MV_MOD_ABS)`，并跳过 `set_velj/set_accj`。

### 当前 DRL 风险标记

`E:\zyq\DRL_3.12-\5.10-TW\dartstudio\digital_twin_robot_main_v1_rc.drl` 目前仍是 `V1-RC`，不能标记为 V1-Final。原因：

```text
通信、遥测、记录、状态机：可继续联调
运动执行：未完全通过 H2515 DartStudio 真环境验证
完整闭环：未完成，不能标记为通信完全完成
```

明天优先不要扩大功能范围，先做最小运动签名验证：

```python
target = posj(0, 0, 90, 0, 0, 0)
movej(pos=target, time=5.0, mod=DR_MV_MOD_ABS)
```

如果 `time=` 方案仍报错，需要在 DartStudio 中单独确认 H2515 可接受的最小 `movej` 调用格式，再把该格式回填到 `thread_motion_worker()`。

### 明天继续项

1. 先跑当前 `time_based=True` 版本，看是否还出现 `Invalid value : vel`。
2. 如果还失败，建立最小 DRL 运动脚本，只测 `posj + movej`，不带 Unity、不带 socket、不带 recorder。
3. 最小运动通过后，再回填到 `digital_twin_robot_main_v1_rc.drl` 的 `thread_motion_worker()`。
4. 运动打通后，再测 `mode1_test -> mode2_ctrl` 抢占，以及 `HALT` 是否能打断运动。
5. Unity 端暂不改协议；Python mock 不参与本轮 DartStudio 运动语法判断，只作为 Unity flat JSON 接口参考。

### 5.15 状态同步修正

本轮进度应按“部分通信打通，完整通信未完成”记录：

1. 已确认 TCP 9092 能把 Unity 命令送到 DartStudio，`HELLO / GET_STATUS / CREATE_SESSION / SET_CHANNELS / START_STREAM / START_RECORD / SET_MODE mode1_test` 可到达 DRL。
2. 已确认 DRL 状态机能进入 `RUN_PRESET / mode1_test`，并触发 motion worker 尝试执行 `preset_B`。
3. 未确认 `MOVE_JOINT / mode1_test` 能在 H2515 DartStudio 真环境中真实驱动模型；`movej/amovej` 参数签名仍是当前阻塞点。
4. 未完成 Unity 端 TCP ACK/REJECTED 读取，因此 Unity 现在还不能完整记录 DartStudio 的 accepted/rejected/status。
5. 未完成高频真实 DartStudio 遥测回灌验证；Python mock 已跑通的 paper CSV 不能直接等同于 DartStudio 真环境完成。

结论：5.15 不能写成“DartStudio 通信已经完全弄好”。当前正确表述是：基础端口和命令送达已打通，完整闭环通信、运动执行和 ACK/状态回读仍在联调中。

## 2026-05-16 接续交接

### 今日第一优先级

先不要继续扩展 Unity 面板、论文指标或 DartStudio 大架构。今天第一步是只验证 DartStudio/H2515 的最小关节运动签名。

推荐先在 DartStudio 单独跑一个最小脚本，不带 Unity、不带 socket、不带 recorder：

```python
from DRCF import *

target = posj(0, 0, 90, 0, 0, 0)
movej(pos=target, time=5.0, mod=DR_MV_MOD_ABS)
```

如果这条能驱动模型，再把同样签名回填到：

```text
E:\zyq\DRL_3.12-\5.10-TW\dartstudio\digital_twin_robot_main_v1_rc.drl
thread_motion_worker()
```

### 当前文件状态

`digital_twin_robot_main_v1_rc.drl` 当前已切到：

```text
USE_MOTION_WORKER = True
USE_ASYNC_MOVEJ = False
USE_INLINE_MOVEJ_SPEED_ARGS = False
USE_TIME_BASED_MOVEJ = True
MOVEJ_TIME_SEC = 5.0
```

这表示当前主 DRL 预期使用：

```python
movej(pos=target_pos, time=5.0, mod=DR_MV_MOD_ABS)
```

并跳过 `set_velj/set_accj`，避免再次触发 `Invalid value : vel`。

### 明确边界

1. Python mock 不改；它只作为 Unity flat JSON 协议参考。
2. Unity 协议不改；当前通信链路已经能把命令送到 DartStudio。
3. 不新增第二套 socket、不新增第二套 joint mapping。
4. DRL 仍保持单文件；如果需要最小运动验证脚本，只作为临时测试脚本，不拆主程序模块。
5. 最小运动签名通过前，不把 `digital_twin_robot_main_v1_rc.drl` 标记为 V1-Final。
