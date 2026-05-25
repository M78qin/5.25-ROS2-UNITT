# EX-UES 过渡命令分支说明

本目录脚本用于过渡阶段联调：

- 主数据与主控制链：`DigitalTwin/Scripts/Communication/DartStudioBridge.cs`
- 过渡命令分支：`Scripts/EX-UES/DartStudioChannelCtrl.cs`

`DartStudioChannelCtrl` 只负责通过 `9092` 命令端口进行手动/按键双向控制（发命令 + 收响应），不替代主桥接逻辑。

## 推荐运行方式

1. 保持 `DartStudioBridge` 作为主脚本（状态流、主命令链、运行时逻辑都走它）。
2. `DartStudioChannelCtrl` 仅在手动调试时启用。
3. 为避免与主链误冲突，默认启用 ARM 保护（`requireArm=true`）。

## 键盘映射（默认）

- `BackQuote`：ARM / DISARM 切换
- `G`：`GET_STATE`
- `F1`：`SET_MODE idle_stream`
- `F2`：`SET_MODE mode1_test`
- `F3`：`SET_MODE mode2_ctrl`
- `1`：`MOVE_JOINT` 到 `presetA`
- `2`：`MOVE_JOINT` 到 `presetB`
- `Space`：`HALT`

说明：

- 当 `requireArm=true` 时，只有 ARM 后按键才会生效。
- `requireFocusForKeyboard=true` 时，仅 Unity 窗口聚焦下响应按键。

## 低资源配置建议

- `autoReconnect=false`
- `maintenanceInterval=1.0`
- `disconnectIdleSec=10`
- `verboseLog=false`
- `logKeyboardResponse=true`（仅保留命令响应日志，便于排查）

