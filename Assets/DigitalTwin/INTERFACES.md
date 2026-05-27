# DigitalTwin 接口与索引总表

> **AI 首选入口**：改接口、查 Topic、对协议、找场景挂载 → 先读本文件，再进子目录 README。  
> 上级：[README.md](README.md) · 进度：[PROGRESS.md](PROGRESS.md)

---

## 1. 文档地图（全部 README）

| 层级 | 路径 |
|------|------|
| 工程根 | [`/README.md`](../../README.md) |
| DigitalTwin 总索引 | [README.md](README.md) |
| **本文件（接口总表）** | **INTERFACES.md** |
| 进度/待办 | [PROGRESS.md](PROGRESS.md) |
| 配置 | [Config/README.md](Config/README.md) · [Config/Profiles/README.md](Config/Profiles/README.md) |
| 代码总览 | [Scripts/README.md](Scripts/README.md) |
| Communication | [Scripts/Communication/README.md](Scripts/Communication/README.md) · [lib/README.md](Scripts/Communication/lib/README.md) |
| Control | [Scripts/Control/README.md](Scripts/Control/README.md) |
| CoreState | [Scripts/CoreState/README.md](Scripts/CoreState/README.md) |
| Runtime | [Scripts/Runtime/README.md](Scripts/Runtime/README.md) |
| RobotModel | [Scripts/RobotModel/README.md](Scripts/RobotModel/README.md) |
| Recording | [Scripts/Recording/README.md](Scripts/Recording/README.md) |
| UI | [Scripts/UI/README.md](Scripts/UI/README.md) |
| Editor | [Editor/README.md](Editor/README.md) |
| Experiment | [Scripts/Experiment/README.md](Scripts/Experiment/README.md) |
| Experiment 子目录 | [Contracts](Scripts/Experiment/Contracts/README.md) · [Session](Scripts/Experiment/Session/README.md) · [Dart_E](Scripts/Experiment/Dart_E/README.md) · [Ros2](Scripts/Experiment/Ros2/README.md) · [Recording](Scripts/Experiment/Recording/README.md) · [Metrics](Scripts/Experiment/Metrics/README.md) · [Replay](Scripts/Experiment/Replay/README.md) |

---

## 2. C# 契约（Contracts）

### 2.1 `IRobotStateSource` — 状态输入

文件：`Scripts/Communication/RobotCommunicationContracts.cs`

| 成员 | 说明 |
|------|------|
| `RuntimeSourceKind Kind` | `DartStudio` / `Ros2` / `Replay` / `Mock` |
| `int QueuedFrameCount` | 待消费队列深度 |
| `bool TryDequeueFrame(out RobotStateFrame)` | Runtime 每帧 drain |
| `RobotSourceStatus GetStatus()` | 连接名、是否连接、错误、队列 |
| `SourceHealth GetHealth()` | 连接、Hz、丢帧、最后接收时间 |

| 实现类 | `Kind` |
|--------|--------|
| `DartStudioBridge` | `DartStudio` |
| `Ros2Bridge` | `Ros2` |
| `ReplayStateSource` | `Replay` |

### 2.2 `IRobotCommandSink` — Dart TCP 命令

| 成员 | 说明 |
|------|------|
| `SetMode(string mode)` | → `SET_MODE` JSON |
| `SendMoveJoint(float[] targetDeg)` | 度，无 speed |
| `SendHalt()` | → `HALT` |
| `SendRawCommand(string json)` | 实验 lifecycle / 扩展命令 |

| 实现类 | 备注 |
|--------|------|
| `DartStudioBridge` | **唯一**正式实现 |
| `Ros2Bridge` | **未实现**；用 `SetMode` / `SendMoveJointRad` / `SendHalt` |

### 2.3 `IExperimentRecorder` — 论文记录

文件：`Scripts/Experiment/Contracts/ExperimentRecordingContracts.cs`

| 方法 | 时机 |
|------|------|
| `OnFrameReceived` | 每帧从源 dequeue 后 |
| `OnFrameApplied` | 应用到 `RobotModelController` 后 |
| `ConfigureSession` / `SetSessionRecordEnabled` / `SetSessionStreamEnabled` | 会话生命周期 |
| `CloseSession` | 结束写入 |

| 实现类 |
|--------|
| `TwinPaperRecorder` |

### 2.4 其它公开类型

| 类型 | 文件 | 用途 |
|------|------|------|
| `RobotCommandResult` | `TwinCommandController.cs` | `Success`, `DryRun`, `Status`, `ErrorMessage` |
| `RobotStateFrame` | `RobotStateTypes.cs` | 统一状态帧 |
| `StateSnapshot` | `RobotStateBus.cs` | UI 低频快照 |
| `TwinMode` | `TwinCommandController.cs` | `Sync` / `Plan` / `Execute` |
| `DigitalTwinMode` | `RuntimeServices.cs` | `Mirror` / `Plan` / `Execute` / `Replay` / `Fault` |
| `RuntimeSourceKind` | `DigitalTwinRuntime.cs` | 活跃数据源枚举 |
| `TwinExperimentSessionState` | `ExperimentSessionController.cs` | 实验会话状态机 |
| `RobotFrameFlags` / `FrameQuality` / `TwinClockSyncStatus` | `RobotStateTypes.cs` | 帧质量与时钟 |

### 2.5 实现关系矩阵

```text
                    IRobotStateSource    IRobotCommandSink    IExperimentRecorder
DartStudioBridge           ✓                    ✓                      —
Ros2Bridge                 ✓                    ✗ (独立方法)            —
ReplayStateSource          ✓                    —                      —
TwinPaperRecorder          —                    —                      ✓
TwinCommandController      —                    路由到 Bridge/ROS2      —
```

---

## 3. `RobotStateFrame` 字段（内部统一模型）

文件：`Scripts/CoreState/RobotStateTypes.cs`

| 字段组 | 字段 | 单位/说明 |
|--------|------|-----------|
| 标识 | `SourceName`, `SequenceId`, `Channel` | |
| 时间 | `SourceTimestampMs`, `UnityReceiveTimestampNs`, `UnityReceiveWallMs` | ms / ns |
| 时间 | `UnityPublishTimestampNs`, `UnityApplyTimestampNs`, `UnityApplyWallMs` | Bus publish / 模型 apply |
| 时钟 | `ClockSyncStatus` | `Synced` 才可算跨进程 wall 延迟 |
| 质量 | `Flags`, `Quality` | `RobotFrameFlags`, `FrameQuality` |
| 关节 | `JointPositionRad[]`, `JointVelocityRad[]`, `JointTorqueNm[]` | **弧度** |
| 力 | `ForceVector[6]` | Fx,Fy,Fz,Tx,Ty,Tz |
| 末端 | `TcpPositionMeters`, `TcpRotation`, `HasTcpPose` | Unity 坐标 |
| 状态 | `Mode`, `MotionState`, `MotionError` | 字符串 |
| 会话 | `SourceId`, `SourceSessionId`, `RunId`, `TrialId`, `RequestId` | |
| 实验 | `ExperimentId`, `ExperimentType`, `PhaseId`, `SegmentId` | `STATE` / `PING` |
| 实验 | `StreamEnabled`, `RecordEnabled`, `EventType`, `Notes` | |
| 探测 | `SendPerfNs`, `SendWallMs` | 源端 monotonic / wall |
| 原始 | `RawPayload` | JSON 或 brief |

**单位规则（全项目）**

| 边界 | 关节角 | 力 |
|------|--------|-----|
| DartStudio UDP in | degree | N, Nm |
| DartStudio TCP `MOVE_JOINT` | degree | — |
| `RobotStateFrame` | radian | 6 轴向量 |
| `RobotModelController` | display deg → Drive | — |
| ROS `JointState` | radian (ROS 标准) | `Wrench` |

---

## 4. DartStudio 线缆协议

### 4.1 传输

| 通道 | 方向 | 默认 | 类 |
|------|------|------|-----|
| UDP 状态 | → Unity | **9090** | `DartStudioBridge` |
| UDP 心跳/PONG | Unity → | **9091** | `DartTcpCommandBuilder.BuildHeartbeat()` |
| TCP 命令 | Unity → | **9092** | JSON + `\n`，`SendRawCommand` |

默认 IP：`127.0.0.1`（`dartIp`）

### 4.2 入站 JSON（`DartStudioPacket`）

解析：`DartStudioProtocol.StateFrameNormalizer.TryFromDartJson`

| 字段 | 说明 |
|------|------|
| `seq`, `ts_ms`, `ros_time_ns`, `host_wall_ns`, `monotonic_ns` | 时间与序号 |
| `channel`, `mode`, `motion`, `motion_error` | 运行状态 |
| `joint_states` | `name[]`, `position[]`, `velocity[]`, `effort[]`；`joint_units`=`rad`/`deg` |
| `tcp_pose` | mm + 欧拉；转 Unity m + Quaternion |
| `tool_force` | `force` + `torque` |
| `experiment_id`, `session_id`, `run_id`, `trial_id`, `req_id` | 论文元数据 |
| `phase_id`, `segment_id`, `stream_enabled`, `record_enabled` | |
| `exp.type` | `PING` → Unity 回 PONG，不进入模型 |

### 4.3 出站 TCP 命令（`DartTcpCommandBuilder`）

| `cmd` | JSON 要点 |
|-------|-------------|
| `SET_MODE` | `"mode":"idle_stream"` \| `mode1_test` \| `mode2_ctrl` |
| `MOVE_JOINT` | `"target":[deg...]`，**禁止** `speed/vel/acc` |
| `HALT` | 无参 |
| `GET_STATE` | 查询 |
| `HEARTBEAT` | UDP 9091 |

### 4.4 实验会话 TCP（`ExperimentSessionController` / `DartEExperimentPanel`）

信封：

```json
{"id":"unity-<ticks>","cmd":"<NAME>","session_id":"...","experiment_id":"...", ...}
```

| `cmd` | 调用方 | 主要 extra 字段 |
|-------|--------|-----------------|
| `HELLO` | Dart_E | `client`, `protocol_version` |
| `GET_STATUS` | Session | — |
| `CREATE_SESSION` | Session, Dart_E, Ros2Panel | `experiment_id`, `session_id`, `mode`, `source_id`, `random_seed` |
| `CLOSE_SESSION` | Session, Dart_E, Ros2Panel | — |
| `SET_CHANNELS` | Session, Dart_E, Ros2Panel | `sources`, `channels`, `dart_hz`, `ros2_hz` |
| `GET_CHANNELS` | Session | — |
| `START_STREAM` / `PAUSE_STREAM` / `RESUME_STREAM` / `STOP_STREAM` | Session, Dart_E, Ros2Panel | — |
| `START_RECORD` / `PAUSE_RECORD` / `RESUME_RECORD` / `STOP_RECORD` | Session, Dart_E, Ros2Panel | `segment_id`（START） |
| `NEW_PHASE` | Session | `phase_id`, `phase_note` |
| `MARK_EVENT` | Session, Dart_E | `event_note` |
| `SET_NETWORK_IMPAIRMENT` | Session | `delay_ms`, `jitter_ms`, `drop_rate`, ... |
| `RESET_NETWORK_IMPAIRMENT` | Session | — |
| `SET_MODE` | Session, Dart_E | `mode` |
| `MOVE_JOINT` | Session, Dart_E | `target`[] deg |
| `HALT` | Session, Dart_E | — |
| `PRESET` | Dart_E | `"action":"start"` |
| `HOME` | Dart_E | — |
| `MODE_A` | Dart_E | — |
| `MOVE_TCP` | Dart_E | `target` 相对偏移 |

发送链：`Panel/Session` → `DartTcpCommandSender.SendRaw` 或 `DartStudioBridge.SendRawCommand` → TCP 9092

---

## 5. ROS2 线缆协议（`Ros2Bridge`）

依赖：`Unity.Robotics.ROSTCPConnector`

### 5.1 订阅（→ `RobotStateFrame`）

| Topic（默认） | 消息类型 | 启用开关 |
|---------------|----------|----------|
| `/joint_states` | `JointStateMsg` | 始终 |
| `/wrench` | `WrenchStampedMsg` | `enableWrenchTopic` |
| `/tcp_pose` | `PoseStampedMsg` | `enableTcpPoseTopic` |
| `/dt/status/mode` | `StringMsg` | 始终 |
| `/dt/status/motion` | `StringMsg` | 格式 `motion\|error` |

`Ros2ExperimentPanel` 默认 joint topic：`/dsr01/joint_states`

### 5.2 发布（命令）

| Topic（默认） | 消息类型 | C# 入口 |
|---------------|----------|---------|
| `/dt/cmd/set_mode` | `StringMsg` | `SetMode(mode)` |
| `/dt/cmd/move_joint` | `JointTrajectoryMsg` | `SendMoveJointRad(rad[], speedPercent)` |
| `/dt/cmd/halt` | `BoolMsg` | `SendHalt()` |

### 5.3 `Ros2Bridge` 公开 API（非 `IRobotCommandSink`）

```csharp
Connect() / Disconnect()
ConfigureTopics(joint, move, halt, setMode, modeStatus, motionStatus)
ConfigureTelemetryTopics(enableWrench, wrench, enableTcp, tcp)
Initialize(profile, schema)
// IRobotStateSource
TryDequeueFrame / GetStatus / GetHealth
// 命令
SetMode / SendMoveJointRad / SendHalt
GetLatestForceCopy()
```

---

## 6. 控制层 API（`TwinCommandController`）

| 方法 | 说明 |
|------|------|
| `Initialize(DigitalTwinRuntime)` | 绑定 Bridge、Profile 安全开关 |
| `EnterSync()` | Plan 释放 + `idle_stream` |
| `EnterPlan()` / `EnterExecute()` | 规划/执行模式 |
| `StartMode1Test()` | 需真实命令 + 非 dry-run → `mode1_test` |
| `EnterDartControlMode()` / `StopDartTask()` | `mode2_ctrl` / `idle_stream` |
| `SetEmergencyStopped(bool)` | 急停锁存 |
| `UpdatePlanTarget(rad[])` | Ghost/Slider 预览 |
| `ExecuteTarget(rad[], speed%)` | 经 Planning + Safety |
| `SendMoveJoint(rad[], speed%)` | 路由 Dart(deg) 或 ROS2(rad) |
| `EmergencyStop()` | HALT；可 bypass dry-run |

**路由**：`ActiveSourceKind==Ros2` → `Ros2Bridge`；`==DartStudio` → `DartStudioBridge.SendMoveJoint(deg)`

**属性**：`CanSendRealMove`, `EnableDryRun`, `LastResult`, `PlanningStartFrame`

---

## 7. 运行时入口（`DigitalTwinRuntime`）

| 属性/方法 | 说明 |
|-----------|------|
| `Profile`, `Settings`, `Schema` | 配置快照 |
| `ActiveSourceKind`, `ActiveSource`, `ActiveSourceHealth` | 当前源 |
| `DartBridge`, `Ros2Bridge`, `ReplaySource` | 模块引用 |
| `RobotModel`, `CommandController`, `Recorder`, `PaperRecorder` | |
| `LatestFrame`, `LatestSnapshot`, `LatestMetrics` | 只读状态 |
| `TryGetLatestFrame` / `PublishFrame` | Bus 访问 |

**源优先级**：`Replay` > `Ros2` > `DartStudio` > `Mock`（双开 Dart+ROS2 时强制 ROS2）

---

## 8. 模型层 API（`RobotModelController` 摘要）

| 方法 | 说明 |
|------|------|
| `ApplyStateFrame(frame)` | Runtime 主路径（rad） |
| `CanAcceptPoseSource(PoseSource)` | `RuntimeLive` 是否可写 |
| `ReleaseToLiveFeedback()` | 释放手动/IK 抢占 |
| `ApplyJointDisplayDegrees(deg[], ...)` | UI/Plan 预览 |
| `ReadCurrentDisplayDegrees()` | 当前显示角 |
| `AutoBindJoints()` / `CaptureCurrentPoseAsZero()` | 绑定与 Zero |
| `SetControlAuthority` | 抢占控制 |

**场景**：`4.24-Try/h2515-1 (4)` 上 `base_link` 或根节点

---

## 9. 记录输出

### 9.1 Paper（`TwinPaperRecorder`，schema `unity_paper_v2`）

目录：`{paperStorageRoot}/{date}/{experiment_id}/{session_id}/`

| 文件 | 内容 |
|------|------|
| `unity_receive.csv` | 每帧 receive 阶段 |
| `unity_apply.csv` | 应用到模型后 |
| `unity_event.csv` | `COMMAND_ECHO` 等 |
| `session_manifest.json` / `manifest.json` | 元数据 |

主列（节选）：`seq`, `source_ts_ms`, `unity_receive_wall_ms`, `unity_publish_wall_ms`, `unity_apply_wall_ms`, `clock_sync_status`, `frame_quality`, `joint_*_rad`, `force_*`, `tcp_*`

### 9.2 Legacy（`TwinRecorder`）

| 文件 | 默认路径 |
|------|----------|
| `frames_{session}.csv` | `D:\Unity Projects\DART_R-data` |
| `exp_ack_*.csv` | 实验 ACK（旧 Tracker 路径） |

---

## 10. 场景挂载总表（SampleScene）

| GameObject | 组件 |  Plane |
|------------|------|--------|
| `4.24-Try/DigitalTwin_System` | `DigitalTwinRuntime`, `TwinUIController`, `TwinRecorder`, `TwinPaperRecorder` | Runtime + UI + 记录 |
| `4.24-Try/DART` | `DartStudioBridge` | Data |
| `4.24-Try/ROS2` | `Ros2Bridge` | Data |
| `4.24-Try/h2515-1 (4)` | `RobotModelController`, `RobotIkController`, `EditorRobotPoseTool` | Model |
| `Planning_Control_System` | `TwinCommandController` | Control |
| `UI_System` | `TwinUIController` | UI |
| `DataLogging_System` | `TwinRecorder` | Recording |
| `Ros2Experimen` | `Ros2ExperimentPanel` | Experiment |
| `dartstudio_E/TCP反馈` | `ExperimentSessionController`, `DartTcpCommandSender` | Experiment |
| `dartstudio_E/Dart_E_Experiment` | `DartEExperimentPanel`（常 inactive） | Experiment |

---

## 11. 调用链（禁止绕路）

### 数据平面

```text
[Dart UDP / ROS Subscribe / Replay]
  → IRobotStateSource.TryDequeueFrame
  → DigitalTwinRuntime.DrainSourceFrames → FillFrameContext
  → RobotStateBus.Publish → Swap
  → RobotModelController.ApplyStateFrame (若 LiveFeedback)
  → IExperimentRecorder.OnFrameReceived / OnFrameApplied
  → TwinUIController.Refresh (低频)
```

### 控制平面

```text
[TwinUIController / Ros2ExperimentPanel / ExperimentSessionController / DartEExperimentPanel]
  → TwinCommandController (运动类，带安全门)
  或 DartStudioBridge.SendRawCommand (lifecycle 类)
  → TCP 9092 / ROS Publish
```

**禁止**：UI 直接 `new TcpClient`；Communication 写 `ArticulationBody`；IK 直接发真实命令。

---

## 12. 待实现 / 已知缺口（与 PROGRESS 同步）

| ID | 项 | 改哪里 |
|----|-----|--------|
| P1 | TCP ACK/REJECTED 回读 | `DartStudioBridge` |
| P2 | 解除急停 UI | `TwinUIController` |
| P3 | Ghost + IK 完整预览 | `PlanningControlService`, `RobotIkController` |
| P4 | DRL `movej` 真机签名 | 工程外 `.drl`，非 Unity 协议 |
| P5 | `unity_event.csv` notes JSON 转义 | `TwinPaperRecorder` |
| P6 | `tool_force` CSV 空列 | Protocol + 发包端 |

---

## 13. 外部工程（不在 Assets 内）

| 路径/工具 | 作用 |
|-----------|------|
| `Tools/dart_mock_100hz.py` | 本地 UDP/TCP mock |
| `E:\zyq\...\dartstudio\digital_twin_robot_main_v1_rc.drl` | DartStudio V1-RC |
| `...\DartStudioMock\src\analyze.py` | 离线论文指标 |
| `experiments/profiles/*.yaml` | Python 200Hz 实验 profile |
