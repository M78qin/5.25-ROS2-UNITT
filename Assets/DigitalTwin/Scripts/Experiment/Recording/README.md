# Experiment/Recording — 论文级记录

> 上级：[Experiment/README.md](../README.md) · **CSV 列**：[INTERFACES.md §9](../../INTERFACES.md#9-记录输出) · 契约：[Contracts/README.md](../Contracts/README.md)

## 职责

`IExperimentRecorder` 实现：**只写 raw facts**，指标由 Python `analyze.py` 离线计算。

## 关键文件

| 文件 | 作用 |
|------|------|
| `TwinPaperRecorder.cs` | `unity_paper_v2` schema；receive/apply/event CSV；manifest |

## 输出文件

```text
PaperLogs/<date>/<experiment_id>/<session_id>/
  session_manifest.json
  manifest.json
  unity_receive.csv
  unity_apply.csv
  unity_event.csv    # 含 COMMAND_ECHO
```

默认根目录：`TwinExperimentProfile.paperStorageRootDirectory`

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| CSV 列 / schema 版本 | `TwinPaperRecorder.SchemaVersion` + `analyze.py` |
| receive vs apply 记录开关 | Profile `paperRecordReceiveFrames` / `paperRecordApplyFrames` |
| 命令 echo 事件 | `ExperimentSessionController.LogCommandEcho` |
| event notes JSON 逗号问题 | `TwinPaperRecorder` 写入转义（PROGRESS 待修） |

## 硬规则

- 不在 Unity 算 p95 RTT 等论文指标（Tracker 已降级为 marker）
- 改列必改 analyzer
