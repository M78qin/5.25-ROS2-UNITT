# DigitalTwin 总索引

`Assets/DigitalTwin` 的根 README，用于**索引子文档**、约定协作方式，并保留运行时架构总览。

---

## 文档索引

### 总览与进度

| 文档 | 路径 | 内容 |
|------|------|------|
| **本文件** | [README.md](README.md) | 架构三平面、GitHub、AI 改进速查 |
| **接口总表** | [**INTERFACES.md**](INTERFACES.md) | **C# 契约、Topic/端口、TCP cmd、RobotStateFrame、场景挂载、调用链** |
| 进度与联调 | [PROGRESS.md](PROGRESS.md) | 阶段目标、DartStudio/ROS2 联调、问题与待办 |

### 配置

| 文档 | 路径 |
|------|------|
| 配置总览 | [Config/README.md](Config/README.md) |
| Profile 子资产 | [Config/Profiles/README.md](Config/Profiles/README.md) |

### 运行时模块（Scripts）

> 代码目录总览：[Scripts/README.md](Scripts/README.md)

| 模块 | README | 改什么先来这 |
|------|--------|----------------|
| 通信 | [Scripts/Communication/README.md](Scripts/Communication/README.md) | UDP/TCP、ROS Topic、协议解析 |
| TCP 命令 lib | [Scripts/Communication/lib/README.md](Scripts/Communication/lib/README.md) | JSON 命令格式、quick-send |
| 控制 | [Scripts/Control/README.md](Scripts/Control/README.md) | 安全门、Plan/Execute、急停 |
| 核心状态 | [Scripts/CoreState/README.md](Scripts/CoreState/README.md) | `RobotStateFrame`、Bus、时钟 |
| 运行时 | [Scripts/Runtime/README.md](Scripts/Runtime/README.md) | 数据源选择、主循环 |
| 机器人模型 | [Scripts/RobotModel/README.md](Scripts/RobotModel/README.md) | 关节映射、IK、模型不动 |
| 通用记录 | [Scripts/Recording/README.md](Scripts/Recording/README.md) | Legacy `frames_*.csv` |
| UI | [Scripts/UI/README.md](Scripts/UI/README.md) | HUD、REAL ON、解除急停 UI |

### 实验层（Scripts/Experiment）

| 子目录 | README |
|--------|--------|
| 总览 | [Scripts/Experiment/README.md](Scripts/Experiment/README.md) |
| Contracts | [Scripts/Experiment/Contracts/README.md](Scripts/Experiment/Contracts/README.md) |
| Session | [Scripts/Experiment/Session/README.md](Scripts/Experiment/Session/README.md) |
| Dart_E | [Scripts/Experiment/Dart_E/README.md](Scripts/Experiment/Dart_E/README.md) |
| Ros2 | [Scripts/Experiment/Ros2/README.md](Scripts/Experiment/Ros2/README.md) |
| Recording | [Scripts/Experiment/Recording/README.md](Scripts/Experiment/Recording/README.md) |
| Metrics | [Scripts/Experiment/Metrics/README.md](Scripts/Experiment/Metrics/README.md) |
| Replay | [Scripts/Experiment/Replay/README.md](Scripts/Experiment/Replay/README.md) |

### Editor

| 文档 | 路径 |
|------|------|
| 编辑器工具 | [Editor/README.md](Editor/README.md) |

---

## AI 改进速查（按需求跳转）

> 接口字段、Topic、TCP `cmd` 全表见 [**INTERFACES.md**](INTERFACES.md)。

| 用户需求 / 症状 | 先读 | INTERFACES 章节 | 关键 .cs |
|-----------------|------|-----------------|----------|
| 查全部接口/协议 | [INTERFACES.md](INTERFACES.md) | §2–§5 | — |
| Dart UDP 收不到 / 解析错 | Communication | §4.2 | `DartStudioProtocol` |
| TCP 命令 / 要 ACK | Communication | §4.3–§4.4, §12 P1 | `DartStudioBridge` |
| ROS2 Topic / 消息 | Communication, Experiment/Ros2 | §5 | `Ros2Bridge` |
| 模型不跟真机动 | RobotModel, PROGRESS | §10 | `RobotModelController` |
| 真实臂误动 / 干跑 | Control, Config | §6 | `TwinCommandController` |
| 论文 CSV / apply=0 | CoreState, Experiment/Recording | §3, §9 | `TwinPaperRecorder` |
| 实验会话 / SET_CHANNELS | Experiment/Session | §4.4 | `ExperimentSessionController` |
| Dart_E 面板 HELLO/PRESET | Experiment/Dart_E | §4.4 | `DartEExperimentPanel` |
| 200Hz / 队列爆 | Runtime, Communication | §7, §11 | `maxSourceDrainPerFrame` |
| HUD / 急停 UI | UI | §12 P2 | `TwinUIController` |
| Ghost / IK | RobotModel, Control | §12 P3 | `PlanningControlService` |
| 回放 | Experiment/Replay | §2.1 | `ReplayStateSource` |
| Profile 开关 | Config/Profiles | §10 + Config README | `Twin*Profile.asset` |

完整待办：[PROGRESS.md](PROGRESS.md) · 缺口编号：[INTERFACES.md §12](INTERFACES.md#12-待实现--已知缺口与-progress-同步)

---

## GitHub 代码备份

| 项 | 内容 |
|----|------|
| 仓库 | https://github.com/M78qin/5.25-ROS2-UNITT |
| 克隆 | `https://github.com/M78qin/5.25-ROS2-UNITT.git` |
| 分支 | `main` |
| 工程根目录 | `E:\new_2026-\unity\project\Unity_Ros_Try1` |
| 备份范围 | 仅自写 **`.cs` / `.py` / `.md`** 等；不含 `Library/`、场景、模型、贴图（见根目录 `.gitignore`） |

GitHub 上是**源码备份**，不是完整 Unity 工程；场景与模型仍在本地。

### 与 Cursor 协作

在对话中说：

- **「上传下 GitHub 仓库」** / **「推一下代码」** → `git add` → `commit`（含日期时间）→ `push`，并更新下文 **操作记录**
- **「从 GitHub 拉一下」** / **「拉取仓库」** → `git pull`，并更新 **操作记录**

首次推送前在本机配置 Git 身份（仅需一次）：

```powershell
git config --global user.name "M78qin"
git config --global user.email "你的GitHub邮箱"
```

### 手动命令（PowerShell）

```powershell
cd "E:\new_2026-\unity\project\Unity_Ros_Try1"

# 查看状态
git status
git log -3 --oneline

# 上传
$ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
git add .
git commit -m "[$ts] 说明本次改了什么"
git push

# 拉取
git pull origin main

# 新目录克隆
git clone https://github.com/M78qin/5.25-ROS2-UNITT.git
```

拉取后若 Unity 已打开，建议 Refresh 项目。提交信息格式示例：`[2026-05-25 22:30:00] 修复 Ros2Bridge 重连`

### 上传前自检

- [ ] `.cs` / `.py` / `.md` 已保存
- [ ] 未误加 `Library/`、`Temp/`
- [ ] 提交说明写清改动内容

### 操作记录

| 日期时间 | 操作 | 说明 | 提交 |
|----------|------|------|------|
| 2026-05-25 22:35:52 | 推送 `main` | 首次代码备份：仅 C#/Python/文档 | `a1d34c0` |

<!-- 每次上传/拉取追加一行，格式 yyyy-MM-dd HH:mm:ss -->

---

## 2026-05-15 DartStudio 联调状态

Unity 侧 `Dart_E` / `ExperimentSessionController` / `DartStudioBridge` 已能把会话、通道、数据流、记录和模式命令发到 DartStudio。当前阻塞点在 `E:\zyq\DRL_3.12-\5.10-TW\dartstudio\digital_twin_robot_main_v1_rc.drl` 的 H2515 `movej` 运动 API 签名，暂不通过 Unity 协议层解决。

下一步先在 DartStudio 做最小 `posj + movej` 运动签名验证；通过后再回到 Unity 测 `mode1_test`、`mode2_ctrl` 抢占和 `HALT`。

---

## Architecture

This Unity digital twin is organized around three runtime planes.

### Data Plane

`Bridge -> Protocol -> DigitalTwinRuntime -> RobotStateBus -> RobotModel/UI/Recorder`

- `DartStudioBridge`, `Ros2Bridge`, and `ReplayStateSource` implement `IRobotStateSource`.
- `DartStudioProtocol` converts transport payloads into `RobotStateFrame`.
- `DigitalTwinRuntime` drains the active source, publishes to `RobotStateBus`, applies the latest frame to `RobotModelController`, and posts raw facts to recorders.
- `RobotModelController` is the only runtime writer to `ArticulationBody`.
- `TwinUIController` reads low-frequency snapshots, not transport queues.

### Control Plane

`UI/Experiment -> TwinCommandController -> Safety -> CommandSender`

- UI and experiment scripts must not open sockets directly.
- `TwinCommandController` owns command safety gates and mode state.
- `DartTcpCommandSender` is the high-level Inspector/experiment command wrapper.
- `DartStudioBridge.SendRawCommand` is the protocol-level TCP send path.

### Experiment Plane

`Experiment/Session -> Experiment/Recording + Experiment/Metrics -> CSV/Manifest -> analyze.py`

- Unity records raw facts only.
- Paper metrics are computed offline by `analyze.py`.
- `Experiment/Recording/TwinPaperRecorder` writes `session_manifest.json`, `manifest.json`, `unity_receive.csv`, `unity_apply.csv`, and `unity_event.csv`.
- CSV schema changes must update `TwinPaperRecorder.SchemaVersion` and `analyze.py`.

### Default Priorities

- `TwinRuntimeProfile.asset` is the single scene-facing entry; live settings are split into sub assets under `Config/Profiles/`.
- Runtime builds a `TwinRuntimeSettings` snapshot on `OnEnable`, then uses that snapshot for module resolution and hot-path switches.
- Keep `TwinRecorder`, `RobotIkController`, SQLite replay, Ghost robot, and `Ros2Bridge` out of the main experiment hot path unless explicitly enabled.
- Keep Unity-side paper experiment scripts under `DigitalTwin/Scripts/Experiment`.
- Prefer `Experiment/Replay/ReplayStateSource` for reproducible demos and offline UI/model validation.
- Keep new top-level folders limited to `Config`, `Communication`, `Control`, `CoreState`, `Experiment`, `Recording`, `RobotModel`, `Runtime`, and `UI`.
