# Experiment/Session — 会话编排

> 上级：[Experiment/README.md](../README.md) · **TCP cmd 全表**：[INTERFACES.md §4.4](../../INTERFACES.md#44-实验会话-tcpexperimentsessioncontroller--dart_eexperimentpanel)

## 职责

论文实验 **生命周期 TCP 命令**，经 `DartTcpCommandSender` → `DartStudioBridge.SendRawCommand`。**不**创建第二套 socket。

## 关键文件

| 文件 | 作用 |
|------|------|
| `ExperimentSessionController.cs` | 会话状态机；`BuildCommand`；`COMMAND_ECHO` |
| `Editor/ExperimentSessionControllerEditor.cs` | Inspector 按钮 |

## 会话状态

`Disconnected → Connected → SessionCreated → Streaming → Recording → PausedRecording → Stopped`

## `ExperimentSessionController` 公开 API

| 方法 | 前置 |
|------|------|
| `Connect` / `Disconnect` | — |
| `GetStatus` | 已 Connect |
| `CreateSession` / `CloseSession` | 已 Connect |
| `ApplyChannels` / `GetChannels` | 已 Connect |
| `StartStream` / `PauseStream` / `ResumeStream` / `StopStream` | 有 sessionId |
| `StartRecord` / `PauseRecord` / `ResumeRecord` / `StopRecord` | 有 sessionId |
| `NewPhase` / `MarkEvent` | 有 sessionId |
| `ApplyNetworkImpairment` / `ResetNetworkImpairment` | 已 Connect |
| `EnterIdleMode` / `StartPresetMotion` / `EnterControlMode` | 已 Connect |
| `EnterControlModeWithPreset` | 可选 mode2 + MOVE_JOINT |
| `HaltMotion` | 已 Connect |
| `ApplyMainPaperExperimentDefaults` | 重置 Inspector |

## TCP `cmd` 一览

| cmd | 说明 |
|-----|------|
| `GET_STATUS` | 查询 |
| `CREATE_SESSION` | experiment_id, session_id, mode, source_id, random_seed |
| `CLOSE_SESSION` | 结束 |
| `SET_CHANNELS` | sources, channels, dart_hz, ros2_hz |
| `GET_CHANNELS` | 查询 |
| `START_STREAM` / `PAUSE_STREAM` / `RESUME_STREAM` / `STOP_STREAM` | 遥测流 |
| `START_RECORD` / `PAUSE_RECORD` / `RESUME_RECORD` / `STOP_RECORD` | segment_id |
| `NEW_PHASE` | phase_id, phase_note |
| `MARK_EVENT` | event_note |
| `SET_NETWORK_IMPAIRMENT` | delay_ms, jitter_ms, drop_rate, duplicate_rate, reorder_rate |
| `RESET_NETWORK_IMPAIRMENT` | 清除 |
| `SET_MODE` | idle_stream / mode1_test / mode2_ctrl |
| `MOVE_JOINT` | target[] 度 |
| `HALT` | 急停 |

Dart_E 扩展（`HELLO`, `PRESET`, `HOME`, `MODE_A`, `MOVE_TCP`）→ [Dart_E/README.md](../Dart_E/README.md)

信封：`{"id":"unity-...","cmd":"...","session_id":"...","experiment_id":"...",...}`

## 场景挂载

```text
dartstudio_E/TCP反馈 → ExperimentSessionController + DartTcpCommandSender
```

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| 新 lifecycle 命令 | `BuildCommand` + DRL mock |
| COMMAND_ECHO | `LogCommandEcho` → `TwinPaperRecorder` |
| Legacy frames | `enableLegacyFrameRecorder`（默认 false） |
| dartHz 下发时机 | 仅 `SET_CHANNELS` |

## 已知缺口

- DRL `movej` 真机签名（PROGRESS 2026-05-15）
- TCP ACK 未回读（INTERFACES §12 P1）

## 联调顺序

`Connect → Create Session → Apply Channels → Start Stream → Start Record → … → Close Session`
