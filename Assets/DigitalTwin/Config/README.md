# DigitalTwin 配置资产说明

当前场景只需要两个主要配置入口：

```text
TwinRuntimeProfile.asset
RobotSignalSchema.asset
```

配置脚本统一放在 `Config/Scripts/`，配置资产放在 `Config/` 和 `Config/Profiles/`。这样 Project 面板里资产和脚本不会混在一起，后续 AI 扩展时也更容易定位。

## 1. TwinRuntimeProfile.asset

作用：系统总入口。场景里的 `DigitalTwinRuntime` 只绑定这个总资产；实际运行开关拆到 3 个高内聚子资产里。

```text
Profiles/TwinSystemProfile.asset
Profiles/TwinExperimentProfile.asset
Profiles/TwinPresentationProfile.asset
```

建议修改顺序：

```text
1. 先改 Profiles/ 里的 3 个子资产。
2. TwinRuntimeProfile.asset 只检查 3 个引用是否正确。
3. 旧字段只作为兼容兜底，不作为新实验的主要修改入口。
```

Runtime 在 `OnEnable` 时会从资产构建 `TwinRuntimeSettings` 快照。运行热路径读取快照，不反复访问 ScriptableObject；关闭的 ROS2、Replay、UI、Ghost、IK、SQLite、旧版记录器不会主动初始化。

## 2. 三个子资产怎么改

`TwinSystemProfile.asset` 管系统热路径：

```text
enableDartStudioSource = true
enableRos2Source = false
enableReplayStateSource = false
enableLiveRobotSync = true
enableGhostRobot = false
enableBidirectionalControl = false
enableRealRobotCommand = false
enableDryRun = true
enableMetrics = true
maxSourceDrainPerFrame = 256
```

`TwinExperimentProfile.asset` 管论文记录和实验通道：

```text
enablePaperRecorder = true
paperRecordReceiveFrames = true
paperRecordApplyFrames = true
enableLegacyRecording = false
recordToSqlite = false
jointPosition = true
toolForce = true
jointVelocity = false
jointEffort = false
tcpPose = false
extraSignals = false
```

`TwinPresentationProfile.asset` 管显示和调试：

```text
enableRuntimeUI = false
enableDebugOverlay = false
enableEditorJointControl = false
enableEditorIkControl = false
```

## 3. RobotSignalSchema.asset

作用：说明 DartStudio UDP 状态包里的关节顺序和力信号名称。当前阶段只要求关节角和 6 轴力/力矩，其他信号不用单独打开。

推荐配置：

```text
jointNames:
  joint_1
  joint_2
  joint_3
  joint_4
  joint_5
  joint_6

forceSignalNames:
  fx
  fy
  fz
  tx
  ty
  tz

velocitySignalNames: []
torqueSignalNames: []
extraSignalNames: []
```

## 4. 关节映射放在哪里

当前关节绑定统一在 `RobotModelController`：

```text
RobotModelController
  Joint Root
  Joints[0..5]
    name
    ArticulationBody
    sign
    offsetDeg
    minDeg
    maxDeg
```

如果 URDF 导入正确：

```text
sign = 1
offsetDeg = 0
minDeg / maxDeg 按实验安全范围填写
```

如果后续发现某个关节方向或零位不一致，只改 `RobotModelController` 对应关节，不改通信协议，也不新增第二套 Mapping 资产。

## 5. 单位规则

```text
DartStudio joint_states.position: degree
DartStudio MOVE_JOINT target: degree
RobotStateFrame.JointPositionRad: radian
RobotModelController: radian -> degree -> ArticulationDrive.target
ForceVector: [Fx,Fy,Fz,Tx,Ty,Tz]
```

## 6. 后续 AI 修改规则

后续让 AI 改配置或接口时，优先给它这些文件：

```text
DigitalTwin/Config/TwinRuntimeProfile.asset
DigitalTwin/Config/Profiles/TwinSystemProfile.asset
DigitalTwin/Config/Profiles/TwinExperimentProfile.asset
DigitalTwin/Config/Profiles/TwinPresentationProfile.asset
DigitalTwin/Config/Scripts/TwinRuntimeProfile.cs
DigitalTwin/Config/Scripts/TwinRuntimeSettings.cs
DigitalTwin/Config/Scripts/TwinSystemProfile.cs
DigitalTwin/Config/Scripts/TwinExperimentProfile.cs
DigitalTwin/Config/Scripts/TwinPresentationProfile.cs
DigitalTwin/Config/Scripts/RobotSignalSchema.cs
DigitalTwin/Scripts/Runtime/DigitalTwinRuntime.cs
DigitalTwin/Scripts/RobotModel/RobotModelController.cs
DigitalTwin/Scripts/Communication/DartStudioProtocol.cs
```

硬规则：

- 不新增第二套关节 Mapping 资产。
- 通信解析只负责把 DartStudio 数据转成 `RobotStateFrame`。
- 关节方向、零点、限位只在 `RobotModelController` 处理。
- UI 滑块范围优先读取 `RobotModelController.GetJointLimitsDeg()`。
- `ExperimentRunProfile` / `PaperScenario` 先只保留架构预留，等真机实时通讯跑通后再做资产。
