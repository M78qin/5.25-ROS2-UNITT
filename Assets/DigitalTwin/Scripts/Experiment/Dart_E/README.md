# Experiment/Dart_E — Dart 实验面板

> 上级：[Experiment/README.md](../README.md) · **TCP cmd**：[INTERFACES.md §4.4](../../../INTERFACES.md#44-实验会话-tcpexperimentsessioncontroller--dart_eexperimentpanel) · Session：[Session/README.md](../Session/README.md)

## 职责

DartStudio V1-RC **运行时实验 UI**，复用 `ExperimentSessionController` 逻辑并扩展 **HELLO / PRESET / HOME / MODE_A / MOVE_TCP** 等命令。所有发送经 `DartTcpCommandSender` 或 `DartStudioBridge.SendRawCommand`。

## 关键文件

| 文件 | 作用 |
|------|------|
| `DartEExperimentPanel.cs` | 面板 API + `BuildCommand` |
| `Editor/DartEExperimentPanelEditor.cs` | Inspector 按钮 |

## 公开 API（Inspector / 代码调用）

| 方法 | 对应 `cmd` / 说明 |
|------|-------------------|
| `Connect` | `HELLO`（含 `protocol_version: 2.0`） |
| `DisconnectSafe` | 断开传输 |
| `ConnectAndCreate` | Connect + CreateSession |
| `CreateSession` / `CloseSession` | 会话 |
| `ApplyChannelsAndRates` | `SET_CHANNELS` |
| `StartStream` / `StopStream` | 流 |
| `StartPaperRecord` / `StopPaperRecord` / `StopRecordAndStream` | 记录 |
| `EnterIdleMode` | Halt + `SET_MODE` idle |
| `StartPresetTest` | `PRESET` action=start |
| `EnterControlMode` | Halt + `mode2_ctrl` |
| `SendHome` | `HOME` |
| `SendPresetA` | `MODE_A` |
| `SendPresetB` | `MOVE_TCP` 相对 Y 偏移 |
| `Halt` | `HALT` |
| `MarkEvent` | `MARK_EVENT` + `TwinPaperRecorder.EnqueueEvent` |
| `ApplyMainDefaults` / `ResolveBindings` | 绑定与默认 |

私有：`SendControlMove` → `MOVE_JOINT`（可先 `EnterControlMode`）

## 状态只读属性

`BridgeConnected`, `TcpConnected`, `StreamEnabled`, `RecordEnabled`, `ActiveSessionId`, `LastAction`, `LastStatus`, `LastError`, `LastJson`

## 场景挂载

```text
dartstudio_E/Dart_E_Experiment  → DartEExperimentPanel（常 inactive）
dartstudio_E/TCP反馈            → ExperimentSessionController（主 Session 入口）
```

## 与 Session 控制器分工

| 场景 | 用谁 |
|------|------|
| 论文标准 lifecycle | `ExperimentSessionController` |
| V1-RC 扩展命令（HELLO/PRESET/HOME） | `DartEExperimentPanel` |
| 底层 TCP | 二者均 → `DartStudioBridge` |

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| 新 DRL 命令 | `DartEExperimentPanel.BuildCommand` + INTERFACES §4.4 |
| 会话/记录逻辑 | 优先 `Session/ExperimentSessionController.cs` |
| DRL 运动阻塞 | 工程外 `.drl`，见 PROGRESS 2026-05-15 |
