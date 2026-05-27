# Experiment/Metrics — 运行时指标标记

> 上级：[Experiment/README.md](../README.md)

## 职责

运行时 **事件标记** 与轻量统计（非论文最终指标）。PING/PONG RTT 等兼容 UI 显示；正式论文用 `analyze.py`。

## 关键文件

| 文件 | 作用 |
|------|------|
| `TwinExperimentTracker.cs` | PING/PONG；帧计数；`MarkEvent` / `MarkPhase` |
| `JointDriftMonitor.cs` | 关节漂移监测（默认禁用） |

## 数据流

```text
DartStudio exp.type=STATE → Bridge → Tracker.OnFrameReceived
DartStudio exp.type=PING   → Bridge.TryHandlePing → PONG + OnPongSent
```

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| 实验面板 RTT/OneWay 显示 | `TwinUIController` + Tracker 属性 |
| 新 marker 类型 | `TwinExperimentTracker.MarkEvent` |
| 漂移报警 | `JointDriftMonitor`（启用 + Profile） |

## 已知缺口

- `LastOneWayMs` 仅 `ClockSyncStatus=Synced` 时有效
- 勿用 Tracker 统计替代 paper CSV + analyzer
