# Runtime 运行时入口

> 上级索引：[DigitalTwin 总索引](../../README.md) · 配置：[Config/README.md](../../Config/README.md)

## 职责

**DigitalTwinRuntime** 是场景主 orchestrator：选活跃数据源、消费帧、驱动模型、分发记录与 UI 刷新。

```text
OnEnable → 读 TwinRuntimeProfile 快照 → 初始化各 Bridge/Recorder/Command
Update   → DrainSource → Bus.Swap → ApplyToRobot → Metrics → UI.Refresh
```

## 关键文件

| 文件 | 作用 |
|------|------|
| `DigitalTwinRuntime.cs` | 主循环；源优先级；`_pendingRecord` 延迟 enqueue |
| `RuntimeServices.cs` | `DigitalTwinModeService`、`FeatureSwitchService` |

## 数据源优先级

```text
1. Replay（enableReplayStateSource）
2. ROS2（enableRos2Source）— 与 Dart 同开时 ROS2 优先并 Warning
3. DartStudio（enableDartStudioSource）
4. Mock（enableMockSource，Inspector 本地）
```

## 场景挂载

```text
4.24-Try/DigitalTwin_System → DigitalTwinRuntime
  绑定：Profile, Schema, DartStudioBridge, Ros2Bridge, RobotModel, Recorder, UI, CommandController
```

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| 源选择逻辑 | `ResolveActiveSource()` |
| 每帧 drain 上限 | `maxSourceDrainPerFrame`（Profile） |
| Live 同步开关/频率 | `enableLiveRobotSync`, `robotApplyRateHz` |
| 记录 all received vs apply-only | `recordAllReceivedFrames` |
| 实验 Tracker 注入 | `OnEnable` 中 `SetExperimentTracker` |
| 模式服务 Mirror/Plan/Execute | `RuntimeServices.DigitalTwinModeService` |

## 已知缺口

- 双源 Dart+ROS2 同时开仅 Warning，无运行时热切换 UI
- Mock 源仅在 Runtime Inspector，非 Profile 主路径

## 硬规则

- Profile 关闭的模块不在 `OnEnable` 初始化（零开销原则）
- 改开关先改 `Config/Profiles/*.asset`，再查 Runtime 是否读快照
