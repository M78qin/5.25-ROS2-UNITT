# Scripts 代码目录索引

> 上级：[DigitalTwin 总索引](../README.md) · **接口总表**：[INTERFACES.md](../INTERFACES.md) · AI 速查见总 README

```text
Scripts/
├── Communication/   外部 IO、协议、TCP 命令 lib
├── Control/         命令安全门、Plan/Execute
├── CoreState/       RobotStateFrame、RobotStateBus
├── Runtime/         DigitalTwinRuntime 主循环
├── RobotModel/      Articulation 唯一写入点
├── Recording/       Legacy TwinRecorder
├── UI/              Runtime HUD
└── Experiment/      论文实验（Session / Paper CSV / Replay / Ros2 / Dart_E）
```

各子目录均有独立 `README.md`，含：**职责、关键文件、改进定位表、已知缺口、硬规则**。

## 数据平面速记

```text
Bridge → RobotStateFrame → DigitalTwinRuntime → RobotStateBus → Model / UI / Recorder
```

## 控制平面速记

```text
UI/Experiment → TwinCommandController → Bridge (Dart TCP / ROS Topic)
```

## 实验平面速记

```text
ExperimentSessionController → TwinPaperRecorder → analyze.py（工程外）
```
