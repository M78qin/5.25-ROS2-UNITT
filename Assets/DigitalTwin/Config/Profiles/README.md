# Runtime Profile 子资产说明

`TwinRuntimeProfile.asset` 仍然是场景里唯一需要绑定的总入口。这里的 3 个子资产只负责把配置按职责收拢，避免一个 Inspector 面板过长，也避免关闭的模块进入初始化和热路径。

```text
TwinSystemProfile.asset        数据源、机器人显示、控制安全、性能。
TwinExperimentProfile.asset    论文记录、旧版记录、主实验通道。
TwinPresentationProfile.asset  Runtime UI、调试面板、Editor 调试工具。
```

当前主实验建议只改这几个地方：

```text
TwinSystemProfile.asset
  enableDartStudioSource = true
  enableRos2Source = false
  enableReplayStateSource = false
  enableLiveRobotSync = true
  enableGhostRobot = false
  enableDryRun = true
  maxSourceDrainPerFrame = 256

TwinExperimentProfile.asset
  enablePaperRecorder = true
  paperRecordReceiveFrames = true
  paperRecordApplyFrames = true
  enableLegacyRecording = false
  jointPosition = true
  toolForce = true
  jointVelocity / jointEffort / tcpPose / extraSignals = false

TwinPresentationProfile.asset
  enableRuntimeUI = false
  enableDebugOverlay = false
  enableEditorJointControl = false
  enableEditorIkControl = false
```

`ExperimentRunProfile` 和 `PaperScenario` 先不做资产。等真机实时通讯、关节角 + 六轴力/力矩、paper recorder、`analyze.py` 端到端跑通后，再按论文实验需要落地。
