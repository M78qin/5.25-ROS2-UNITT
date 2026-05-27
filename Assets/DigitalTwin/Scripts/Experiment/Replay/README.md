# Experiment/Replay — CSV 回放源

> 上级：[Experiment/README.md](../README.md) · 实现：`ReplayStateSource.cs`

## 职责

从已录制的 paper CSV **回放** `RobotStateFrame`，走正常 `IRobotStateSource → DigitalTwinRuntime → RobotModel` 路径，用于可复现演示与离线 UI 验证。

## 配置（Profile）

```text
enableReplayStateSource = true
replayCsvPath = unity_receive.csv 路径
replayHz = 30
replayLoop = false
```

启用后 **优先级最高**，覆盖 Dart/ROS2。

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| CSV 解析 | `ReplayStateSource.LoadCsv` |
| 回放时钟 | `replayHz` vs CSV 时间戳 |
| 引号 CSV 支持 | 当前简单 split，复杂 CSV 未支持（PROGRESS 风险） |

## 验证步骤

1. 先跑一轮 live 生成 `unity_receive.csv`
2. Profile 开 Replay，填路径，Play
3. 确认 Model/UI 无 live socket 也能动
