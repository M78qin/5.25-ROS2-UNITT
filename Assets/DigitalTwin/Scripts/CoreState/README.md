# CoreState 核心状态

> 上级：[README](../../README.md) · **字段全表**：[INTERFACES.md §3](../../INTERFACES.md#3-robotstateframe-字段内部统一模型)

## 职责

全项目统一状态帧与双缓冲总线。Communication 产出帧，Runtime 写入 Bus，Model/UI/Recorder 读取。

## 关键文件

| 文件 | 作用 |
|------|------|
| `RobotStateTypes.cs` | `RobotStateFrame`, `MetricsSample`, `SourceHealth`, `SystemClock` |
| `RobotStateBus.cs` | 双缓冲；`StateSnapshot` |

## `RobotStateBus` API

| 方法 | 说明 |
|------|------|
| `Publish(frame)` | 写缓冲；打 `UnityPublish*`；检测丢帧/乱序 |
| `Swap()` | 主线程交换 read 缓冲 |
| `TryGetLatest(out frame)` | clone 给 Model/UI |
| `MarkApplied(seq, applyNs)` | 写 apply 时间戳（paper 指标关键） |
| `Clear()` | 会话重置 |

## 枚举

| 枚举 | 值（节选） |
|------|------------|
| `RobotFrameFlags` | `HasJointPosition`, `HasForce`, `OutOfOrder`, `InvalidSchema`, … |
| `FrameQuality` | `Normal`, `Delayed`, `Duplicated`, `Interpolated`, `Lost` |
| `TwinClockSyncStatus` | `Unknown`, `Synced`, `Unsynced` |

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| 新增帧字段 | `RobotStateTypes.cs` → Bridge/Protocol/Recorder/analyze.py |
| 丢帧逻辑 | `RobotStateBus.Publish` |
| UI 快照 | `StateSnapshot.FromFrame` |

## 硬规则

- 跨进程延迟用 **wall-clock**（`UnityReceiveWallMs` vs `SourceTimestampMs`）
- 进程内处理时间用 `SystemClock.NowNs()`（`unity_process_ms`）
