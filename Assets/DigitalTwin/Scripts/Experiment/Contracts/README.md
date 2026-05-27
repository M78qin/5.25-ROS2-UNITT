# Experiment/Contracts — 实验契约

> 上级：[Experiment/README.md](../README.md)

## 关键文件

| 文件 | 作用 |
|------|------|
| `ExperimentRecordingContracts.cs` | `IExperimentRecorder` |
| `PaperExperimentDefaults.cs` | 主实验默认通道/ID/频率常量 |

## IExperimentRecorder

```csharp
OnFrameReceived / OnFrameApplied
ConfigureSession / SetSessionRecordEnabled / SetSessionStreamEnabled
```

实现：`TwinPaperRecorder`（见 Recording/）

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| 主实验默认开关 | `PaperExperimentDefaults.cs` + Profile |
| 新记录器接口方法 | `ExperimentRecordingContracts.cs` + 所有实现类 |
