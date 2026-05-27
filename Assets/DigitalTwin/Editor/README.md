# Editor 编辑器工具

> 上级索引：[DigitalTwin 总索引](../README.md)

## 职责

Inspector 扩展、HUD 一键生成、Play 模式调试入口。**不含**运行时热路径逻辑。

## 关键文件

| 文件 | 作用 |
|------|------|
| `TwinRuntimeUIBuilderEditor.cs` | `Create Runtime HUD` 菜单；绑定校验 |
| `EditorRobotPoseToolEditor.cs` | 关节滑条 Inspector |
| `RobotIkControllerEditor.cs` | IK 目标 Inspector |

## 关联 Experiment Editor

```text
Scripts/Experiment/Session/Editor/ExperimentSessionControllerEditor.cs
Scripts/Experiment/Dart_E/Editor/DartEExperimentPanelEditor.cs
Scripts/Experiment/Ros2/Editor/Ros2ExperimentPanelEditor.cs
Scripts/Communication/lib/Editor/DartTcpCommandSenderEditor.cs
```

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| HUD 生成/清理旧 Canvas | `TwinRuntimeUIBuilderEditor.cs` |
| 实验 Session Inspector 按钮 | `ExperimentSessionControllerEditor.cs` |
| TCP quick-send 预设 | `DartTcpCommandSenderEditor.cs` |

## 硬规则

- Editor 脚本放 `DigitalTwin/Editor/` 或对应模块的 `Editor/` 子文件夹
- `#if UNITY_EDITOR` 包装 Play 不可用代码
