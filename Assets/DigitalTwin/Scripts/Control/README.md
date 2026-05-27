# Control 控制层

> 上级：[README](../../README.md) · **控制 API**：[INTERFACES.md §6](../../INTERFACES.md#6-控制层-apitwincommandcontroller) · 进度：[PROGRESS.md](../../PROGRESS.md)

## 职责

Unity → 真实机器人/DRL 的**命令安全门**与 Plan/Execute 状态机。所有 MOVE_JOINT / HALT / SET_MODE 应经此层（Experiment Session 的 lifecycle 命令除外，仍走 TCP raw）。

```text
UI / Experiment Panel
  → TwinCommandController（安全门 + 路由）
    → PlanningControlService（Plan/Execute/Ghost 预览）
    → SafetyCheckService（dry-run、急停、起点匹配）
    → DartStudioBridge 或 Ros2Bridge
```

## 关键文件

| 文件 | 作用 |
|------|------|
| `TwinCommandController.cs` | 模式 Sync/Plan/Execute；命令路由；急停 |
| `PlanningControlService.cs` | Plan 进入/取消；Ghost 目标；Execute 入口 |
| `SafetyCheckService.cs` | 双向控制开关、dry-run、关节校验 |

## 模式与路由

| TwinMode | 行为 |
|----------|------|
| `Sync` | 跟随反馈；发 `idle_stream` |
| `Plan` | 调整预览目标，不发真实命令 |
| `Execute` | 允许 `SendMoveJoint` |

**命令路由**（`ResolveCommandRoute`）：按 `DigitalTwinRuntime.ActiveSourceKind` → ROS2 优先于 DartStudio。

## 安全开关（来自 Profile）

```text
enableBidirectionalControl   总开关
enableRealRobotCommand       真实臂
enableDryRun                 干跑（默认 true）
emergencyStopped             急停锁存
```

`CanSendRealMove = 上述全部满足且非急停`

## 场景挂载

```text
Planning_Control_System → TwinCommandController
4.24-Try/DigitalTwin_System 也可挂（当前场景在独立对象）
```

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| 新增命令类型（Pause/Resume） | `TwinCommandController.cs` + Bridge |
| Plan/Execute 流程 | `PlanningControlService.cs` |
| 执行前安全检查 | `SafetyCheckService.cs` |
| **解除急停 UI** | `UI/TwinUIController.cs` 调 `SetEmergencyStopped(false)` |
| ROS vs Dart 路由优先级 | `TwinCommandController.ResolveCommandRoute` |
| mode1_test / mode2_ctrl 预设 | `StartMode1Test()` / `EnterDartControlMode()` |

## 已知缺口

1. **解除急停 UI** 未实现（PROGRESS 待做 #7）
2. Ghost 克隆 + IK 预览与 Execute 分离 — 部分在 `PlanningControlService` + `RobotModel`
3. `Ros2Bridge.SendMoveJointRad` 带 speedPercent；Dart `MOVE_JOINT` 不带速度

## 硬规则

- UI / IK / Slider **不**直接发 TCP/ROS 命令
- HALT 可 bypass dry-run（仅停止运动）
- `mode1_test` 会驱动真实臂 A/B 测试，启用前确认安全空间
