# Recording 通用记录

> 上级索引：[DigitalTwin 总索引](../../README.md) · 论文记录：[Experiment/Recording/README.md](../Experiment/Recording/README.md)

## 职责

**Legacy** 运行时 CSV 记录器（`frames_*.csv`）。主论文路径已迁移到 `TwinPaperRecorder`；本模块在 Profile 默认关闭。

## 关键文件

| 文件 | 作用 |
|------|------|
| `TwinRecorder.cs` | 异步 writer 线程；frames CSV；实验 timing/ACK 扩展 |

## 与 Paper Recorder 分工

| | TwinRecorder | TwinPaperRecorder |
|---|--------------|-------------------|
| 输出 | `frames_*.csv` | `unity_receive/apply/event.csv` + manifest |
| 默认 | `enableLegacyRecording=false` | `enablePaperRecorder=true` |
| 接口 | 直接 Enqueue | `IExperimentRecorder` |

## 场景挂载

```text
DataLogging_System → TwinRecorder
```

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| Legacy CSV 列 | `TwinRecorder.cs` |
| 队列硬上限 | `recordQueueHardLimit`（Profile） |
| 输出目录 | 默认 `D:\Unity Projects\DART_R-data` |
| 论文级记录 | 改 `Experiment/Recording/TwinPaperRecorder.cs` |

## 已知缺口

- `unity_event.csv` 的 notes JSON 逗号转义问题（PROGRESS 2026-05-15）
- `tool_force` 曾出现 CSV 全空，需查 Protocol + Python 发包

## 硬规则

- Unity 只记 raw facts；p95/抖动等由 `analyze.py` 离线算
- 改 schema 同步 `SchemaVersion` 与 analyzer
