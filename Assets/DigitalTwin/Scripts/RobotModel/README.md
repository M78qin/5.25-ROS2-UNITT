# RobotModel 机器人模型

> 上级索引：[DigitalTwin 总索引](../../README.md) · 信号映射：[Config/README.md](../../Config/README.md)

## 职责

**唯一**写入 `ArticulationBody` 的运行时模块。URDF 关节绑定、Zero/Home、限位、校准偏移、TCP 读取。

```text
RobotStateFrame.JointPositionRad → ApplyStateFrame → ArticulationDrive.target
```

## 关键文件

| 文件 | 作用 |
|------|------|
| `RobotModelController.cs` | 关节绑定、姿态应用、ControlAuthority |
| `RobotIkController.cs` | IK 预览（不发送真实命令） |
| `EditorRobotPoseTool.cs` | Play 模式手动关节（可抢占 LiveFeedback） |

## ControlAuthority（谁能改模型）

| 值 | 说明 |
|----|------|
| `LiveFeedback` | Runtime/Dart/ROS 实时帧（默认） |
| `RuntimeManualJoint` | 手动滑条抢占 |
| `RuntimeIkPreview` | IK 预览抢占 |
| `CommandPreview` | 规划预览 |
| `Disabled` | 禁止外部写入 |

**常见问题**：模型不动 → 检查 `EditorRobotPoseTool.enableRuntimeManualJointControl` 是否抢占 Live。

## 场景挂载

```text
4.24-Try/h2515-1 (4)/base_link → RobotModelController, RobotIkController, EditorRobotPoseTool
```

## 改进定位

| 想改什么 | 文件 / 位置 |
|----------|-------------|
| 关节方向/零点/限位 | `RobotModelController` JointBinding（**唯一**映射点） |
| 启动姿态 Home0/Zero | `startupPoseMode`, `ApplyStartupPoseOnPlay` |
| Ghost 克隆显示 | Profile `enableGhostRobot` + Planning（**未完成**） |
| IK 拖拽预览 | `RobotIkController.cs` |
| 校准偏移 | `RobotSignalSchema.calibrationOffsetDeg` + Model 应用 |
| URDF 自动绑定 | `Auto Bind Joints`（Inspector） |

## 已知缺口

1. **Ghost 克隆 + IK 拖拽预览** 未完整接入（PROGRESS #10）
2. 不新增第二套 Joint Mapping 资产（已删除 `JointMappingConfig`）

## 硬规则

- Communication 层不得写 Articulation
- 关节 deg/rad 转换：外部 deg → Frame rad → Model 内部 deg → Drive
- 校准只影响显示，raw CSV 不改
