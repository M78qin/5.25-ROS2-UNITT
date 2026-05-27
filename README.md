# 5.25-ROS2-UNITT

Unity + ROS2 数字孪生实验项目（代码备份仓库）。

本仓库**仅上传自写源码**（C#、Python、文档等），不包含 `Library/`、场景、模型、贴图等大文件。完整工程请在本地 Unity 打开原项目目录。

## 本地项目路径

`e:\new_2026-\unity\project\Unity_Ros_Try1`

## 主要代码目录

- [`Assets/DigitalTwin/README.md`](Assets/DigitalTwin/README.md) — 数字孪生总索引（子模块 README + AI 改进速查）
- [`Assets/DigitalTwin/INTERFACES.md`](Assets/DigitalTwin/INTERFACES.md) — **接口/协议/场景挂载总表**（改接口先看）
- [`Assets/DigitalTwin/PROGRESS.md`](Assets/DigitalTwin/PROGRESS.md) — 联调进度与待办
- `Assets/Scripts/` — 通用脚本（如 `GameCameraRayController`）
- `Tools/` — ROS/模拟相关 Python 工具（如 `dart_mock_100hz.py`）

### DigitalTwin 子文档速链

| 模块 | 文档 |
|------|------|
| 配置 | `Assets/DigitalTwin/Config/README.md` |
| 通信 | `Assets/DigitalTwin/Scripts/Communication/README.md` |
| 控制 | `Assets/DigitalTwin/Scripts/Control/README.md` |
| 实验 | `Assets/DigitalTwin/Scripts/Experiment/README.md` |
| 运行时 | `Assets/DigitalTwin/Scripts/Runtime/README.md` |
