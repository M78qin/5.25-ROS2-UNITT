# Experiment/Ros2 — ROS2 实验面板

> 上级：[Experiment/README.md](../README.md) · **ROS 线缆**：[INTERFACES.md §5](../../../INTERFACES.md#5-ros2-线缆协议ros2bridge)

## 职责

ROS2 + MoveIt 论文实验的 **Inspector 驱动面板**：连接、Topic 配置、Paper Session、预设关节命令。

## 关键文件

| 文件 | 作用 |
|------|------|
| `Ros2ExperimentPanel.cs` | Connect/Disconnect；Topic；CreateSession；Move Home/A/B；Halt |
| `Editor/Ros2ExperimentPanelEditor.cs` | Inspector UI |

## 默认 Topic（`ApplyRos2Defaults`）

```text
/dsr01/joint_states
/dt/cmd/move_joint, /dt/cmd/halt, /dt/cmd/set_mode
/dt/status/mode, /dt/status/motion
```

## 场景挂载

```text
Ros2Experimen → Ros2ExperimentPanel
4.24-Try/ROS2 → Ros2Bridge
```

## `Ros2ExperimentPanel` 公开 API

| 方法 | 说明 |
|------|------|
| `ResolveBindings` / `ApplyRos2Defaults` | 绑定与默认 Topic |
| `ConnectRos2` / `DisconnectRos2` | `Ros2Bridge.Connect/Disconnect` |
| `ApplyTopicSettings` | `ConfigureTopics` + telemetry |
| `CreateSession` / `CloseSession` | Paper session 元数据 |
| `ApplyChannelsAndRates` | 通道（逻辑同 Session，走 panel 配置） |
| `StartStream` / `StopStream` | 流标记 + recorder |
| `StartPaperRecord` / `StopPaperRecord` / `StopRecordAndStream` | Paper CSV |
| `SendHome` / `SendPresetA` / `SendPresetB` | 预设关节（度→rad） |
| `Halt` | 急停 |
| `MarkEvent` | 事件标记 |

属性：`Ros2Running`, `Ros2Connected`, `Ros2FrameRateHz`, `StreamEnabled`, `RecordEnabled`

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| Topic 名/遥测开关 | `Ros2ExperimentPanel` + `Ros2Bridge.ConfigureTopics` |
| 直接发 ROS vs 走 CommandController | `publishDirectlyToRos2` |
| Paper 记录会话 | `paperRecorder.ConfigureSession` |
| 主实验是否启用 ROS2 | `TwinSystemProfile.enableRos2Source`（默认 false） |

## 已知缺口

- 双源 Dart+ROS2 同时开时 Runtime 强制 ROS2 为 active source
- 真机 ROS2 端到端需单独验证（与 Dart 路径独立）
