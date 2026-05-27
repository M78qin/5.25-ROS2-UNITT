# Communication 通信层

> 上级：[README](../../README.md) · **接口总表**：[INTERFACES.md §4–§5](../../INTERFACES.md#4-dartstudio-线缆协议) · 进度：[PROGRESS.md](../../PROGRESS.md)

## 职责

外部数据源 ↔ Unity 内部 `RobotStateFrame` 的**唯一**传输入口。本层只做收发与协议解析，**不写** `ArticulationBody`，**不**做 UI 或安全门。

```text
DartStudio UDP/TCP  ──► DartStudioBridge ──► StateFrameNormalizer
ROS2 Topics       ──► Ros2Bridge         ──► JointState/Wrench/Pose
CSV 回放          ──► ReplayStateSource  （见 Experiment/Replay）
```

## 关键文件

| 文件 | 作用 |
|------|------|
| `RobotCommunicationContracts.cs` | `IRobotStateSource`、`IRobotCommandSink` |
| `DartStudioBridge.cs` | UDP 9090 状态、UDP 9091 心跳/PONG、TCP 9092 命令 |
| `DartStudioProtocol.cs` | JSON → `RobotStateFrame`；`DartStudioPacket` 数据结构 |
| `Ros2Bridge.cs` | ROS TCP Connector 订阅/发布 |
| `lib/DartTcpCommandBuilder.cs` | TCP JSON 命令构造 |
| `lib/DartTcpCommandSender.cs` | Inspector 薄封装，复用 Bridge TCP |
| `lib/Editor/DartTcpCommandSenderEditor.cs` | Play 模式 quick-send 面板 |

## 接口速查

完整契约、字段、实现矩阵见 [INTERFACES.md](../../INTERFACES.md)。

| 契约 | 实现类 |
|------|--------|
| `IRobotStateSource` | `DartStudioBridge`, `Ros2Bridge`, `ReplayStateSource` |
| `IRobotCommandSink` | **仅** `DartStudioBridge` |

### `DartStudioBridge` 公开 API

| 类别 | 成员 |
|------|------|
| 生命周期 | `Initialize`, `ConnectTransport`, `DisconnectTransport`, `SetExperimentTracker` |
| 状态源 | `TryDequeueFrame`, `GetStatus`, `GetHealth`, `Kind`, `QueuedFrameCount` |
| 连接 | `IsRunning`, `IsConnected`, `IsTcpConnected`, `FrameRateHz`, `LastSeq`, `LatencyMs` |
| 命令 | `SetMode`, `SendMoveJoint(deg[])`, `SendHalt`, `SendGetState`, `SendRawCommand` |
| 状态 | `CurrentMode`, `MotionState`, `LastCommandStatus`, `LastCommandError` |

### `Ros2Bridge` 公开 API（**未**实现 `IRobotCommandSink`）

| 类别 | 成员 |
|------|------|
| 连接 | `Connect`, `Disconnect`, `IsRunning`, `IsConnected` |
| 配置 | `ConfigureTopics`, `ConfigureTelemetryTopics`, `Initialize` |
| 状态源 | `TryDequeueFrame`, `GetStatus`, `GetHealth` |
| 命令 | `SetMode`, `SendMoveJointRad`, `SendHalt` |
| Topic 只读属性 | `JointStateTopic`, `MoveJointTopic`, `HaltTopic`, … |

### 线缆（默认）

| 通道 | 端口/Topic |
|------|------------|
| Dart UDP 入 | **9090** |
| Dart UDP 心跳 | **9091** |
| Dart TCP 出 | **9092** |
| ROS 订阅 | `/joint_states`, `/wrench`, `/tcp_pose`, `/dt/status/*` |
| ROS 发布 | `/dt/cmd/set_mode`, `move_joint`, `halt` |

## 场景挂载

```text
4.24-Try/DART          → DartStudioBridge
4.24-Try/ROS2          → Ros2Bridge
dartstudio_E/TCP反馈   → DartTcpCommandSender（可选）
```

## 改进定位（AI 先看这里）

| 想改什么 | 改哪个文件 | 备注 |
|----------|------------|------|
| Dart JSON 字段映射 / 单位 | `DartStudioProtocol.cs` | 关节 deg→rad、TCP 坐标系在此 |
| UDP 队列上限 / 断线判定 | `DartStudioBridge.cs` | 已有 `maxRawQueueSize` |
| **TCP ACK/ERROR 回读** | `DartStudioBridge.cs` | **待做**：汇入 `LastCommandStatus` |
| ROS Topic 名 / 消息组装 | `Ros2Bridge.cs` | `ConfigureTopics()` |
| 命令 JSON 格式 | `lib/DartTcpCommandBuilder.cs` | 与 DRL 协议对齐 |
| 新增第二套 socket | **禁止** | 走现有 Bridge |

## 已知缺口（摘自 PROGRESS）

1. **TCP ACK/REJECTED 未读取** — Unity 无法完整记录 DartStudio accepted/rejected
2. DartStudio 真机 `movej` 签名在 DRL 侧，非本层协议问题
3. `Ros2Bridge` 未实现 `IRobotCommandSink`，由 `TwinCommandController` 直接调用

## 硬规则

- 不新增第二套 DartStudio socket
- UI / Experiment 不直接 `TcpClient` / `UdpClient`
- 关节映射不在此层，见 `RobotModel/README.md`

## 子目录

- [lib/README.md](lib/README.md) — TCP 命令构建与 Sender 薄层
