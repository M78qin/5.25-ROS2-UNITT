# UI 运行时界面

> 上级索引：[DigitalTwin 总索引](../../README.md) · Editor 生成：[Editor/README.md](../../Editor/README.md)

## 职责

低频读取 `StateSnapshot`，刷新 HUD；按钮事件转发到 `TwinCommandController`。**不**直接 socket。

## 关键文件

| 文件 | 作用 |
|------|------|
| `TwinUIController.cs` | 刷新节流；Plan Slider debounce；REAL ON 二次确认 |
| `TwinRuntimeUIFactory.cs` | Canvas/Panel/Card 生成；`TwinRuntimeUIBindings` |
| `TwinRuntimeUICard.cs` | Hidden/Collapsed/Expanded |
| `TwinRuntimeUIPanelInteractor.cs` | 拖拽、缩放、最小化 |
| `TMP_TextExtensions.cs` | `SetIfChanged` 减 GC |

## 刷新频率（HUD V2）

```text
Joints/Force   20 Hz
CommandSafety  10 Hz
Connection      5 Hz
Metrics         3 Hz
CSV             1 Hz
```

Card 为 Hidden/Collapsed 时不刷新 Body。

## 场景挂载

```text
UI_System → TwinUIController
H2515_DigitalTwinRuntimeCanvas（由菜单生成，当前场景默认 inactive）
```

生成菜单：`GameObject > Digital Twin > Create Runtime HUD`

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| 新卡片/布局 | `TwinRuntimeUIFactory.cs` |
| 按钮/Slider 行为 | `TwinUIController.cs` |
| **解除急停按钮** | `TwinUIController` → `commandController.SetEmergencyStopped(false)` |
| 连接状态颜色 Online/STALE | `TwinUIController` Connection 卡 |
| 实验面板 RTT/OneWay | `SetExperimentTracker` 路径 |

## 已知缺口

1. **解除急停 UI**（PROGRESS #7）
2. 主实验默认 `enableRuntimeUI=false`，性能测试时 UI 应零开销

## 硬规则

- 改 HUD 优先限 `Scripts/UI/` + `Editor/TwinRuntimeUIBuilderEditor.cs`
- 不动 Communication / Runtime / 命令 / 记录 / IK 核心逻辑
