# Win11 ↔ WSL2 联通对照表

Last updated: 2026-05-26 ~23:00 CST · **READY TO PUSH**

**推进入口：** [`logs/2026-05-26-ready-to-push.md`](logs/2026-05-26-ready-to-push.md)  
**WSL2 主文档：** `docs/progress/2026-05-26_handoff_real_robot_tomorrow.md` · Unity [`LATEST.md`](LATEST.md)

---

## 1. 网络

| 项 | Win11 Unity | WSL2 |
|----|-------------|------|
| 连接地址 | **`127.0.0.1`** | `0.0.0.0` (listen) |
| 端口 | **10002** | **10002** |
| 不可用 | `192.168.31.98:10002` 从 Win11 TCP **不通** | — |
| Win11 自检 | `Test-NetConnection 127.0.0.1 -Port 10002` | — |
| WSL2 自检 | — | `ss -lntp \| grep 10002` |

WSL2 网络模式：`.wslconfig` → `networkingMode=mirrored`（localhost 转发可用）。

Unity 侧 TCP 配置：`Ros2Bridge.rosTcpHost=127.0.0.1`，`rosTcpPort=10002`（显式 `Connect()`）。

---

## 2. ROS Topics（Unity ↔ WSL2 契约）

| Topic | 方向 | 消息类型 | Unity 侧 | WSL2 侧（Plan A sim） |
|-------|------|----------|----------|------------------------|
| `/dsr01/joint_states` | WSL2 → Unity | `sensor_msgs/JointState` | `Ros2Bridge` Subscribe（Stream 后） | **emulator / MoveIt 虚拟** @~100Hz |
| `/dt/cmd/move_joint` | Unity → WSL2 | `trajectory_msgs/JointTrajectory` | Publish | adapter → FJT（**非 dry-run**） |
| `/dt/cmd/halt` | Unity → WSL2 | `std_msgs/Bool` | Publish | adapter |
| `/dt/cmd/set_mode` | Unity → WSL2 | `std_msgs/String` | Publish | adapter |
| `/dt/cmd/session` | Unity → WSL2 | `std_msgs/String` JSON | Publish | session_controller |
| `/dt/status/mode` | WSL2 → Unity | `std_msgs/String` | Subscribe | adapter |
| `/dt/status/motion` | WSL2 → Unity | `std_msgs/String` | Subscribe | session_controller **1Hz** `LINKED/STREAMING/RECORDING` |
| `/dt/status/session` | WSL2 → Unity | `std_msgs/String` JSON | Subscribe（Link 后） | session_controller；含 `record_enabled` |

**采集通道：** 论文阶段目标 `joint_position` @**100Hz**（Panel 可只开此项）；待机模式下亦可开流/记录。

---

## 3. 关键文件对照

### Win11 — `E:\new_2026-\unity\project\Unity_Ros_Try1`

| 用途 | 路径 |
|------|------|
| ROS2 桥 | `Assets/DigitalTwin/Scripts/Communication/Ros2Bridge.cs` |
| 实验面板 | `Assets/DigitalTwin/Scripts/Experiment/Ros2/Ros2ExperimentPanel.cs` |
| Plan B 菜单 | `Assets/DigitalTwin/Scripts/Experiment/Ros2/Editor/Ros2ExperimentPanelEditor.cs` |
| 场景 | `Assets/Scenes/SampleScene.unity` |
| Handoff 入口 | `AI_HANDOFF/LATEST.md` |

### WSL2 — `/home/zyq/ros2_ws`

| 用途 | 路径 |
|------|------|
| **真机 Unity 链路（当前主线）** | `src/ros2-unity-bridge/scripts/start_ros2_unity_link_real.sh` |
| 虚拟 sim | `src/ros2-unity-bridge/scripts/start_ros2_unity_link_sim.sh` |
| 仅命令调试 dryrun | `src/ros2-unity-bridge/scripts/start_ros2_unity_link_dryrun.sh`（勿与 real 同占 10002） |
| 清理 | `src/ros2-unity-bridge/scripts/cleanup_ros2_unity.sh` |
| **真机 Unity 链路（明日）** | `src/ros2-unity-bridge/scripts/start_ros2_unity_link_real.sh` |
| **清虚拟/Docker 残留** | `src/ros2-unity-bridge/scripts/cleanup_virtual_residuals.sh` |
| 读关节角 deg | `src/ros2-unity-bridge/scripts/read_current_joints.sh` |
| 真机协调 / 明日交接 | `src/docs/progress/2026-05-26_real_robot_unity_coordination.md` · `..._handoff_real_robot_tomorrow.md` |
| 会话日志 | `/tmp/ros2_unity_session/` |
| Plan A 实现文档 | `src/docs/progress/2026-05-26_plan_a_sim_implementation.md` |
| Endpoint patch | `src/ROS-TCP-Endpoint/ros_tcp_endpoint/server.py` |

---

## 4. 标准联调步骤（Plan A + B）

```text
[WSL2] cleanup → start_ros2_unity_link_sim.sh（前台，勿与 dryrun 并存）
[Win11] Test-NetConnection 127.0.0.1 -Port 10002
[Unity] Play → DigitalTwin/ROS2 Plan B/Run Steps 1-5
[期望] Link=ON, Data=ON, Hz≈100
[Unity] 6 Home / 7 Preset A / 8 Halt
[WSL2] ros2 topic hz /dsr01/joint_states
[WSL2] RViz 虚拟臂应随 Home/A 运动
[暂缓] Unity Record + ./arm_wsl2_record.sh arm
```

断线：`DigitalTwin/ROS2 Plan B/2b Reconnect ROS2`

---

## 5. 成功 / 失败判据

| 现象 | 含义 |
|------|------|
| `RegisterSubscriber(/dsr01/joint_states) OK` | Stream 阶段 TCP 注册成功 |
| `Link=ON, Data=OFF, Hz=0` | sim 未发 joint_states **或** dryrun 仍在跑 |
| `Link=ON, Data=ON, Hz≈100` | **A+B 联合成功** |
| `RegisterPublisher(/dt/cmd/move_joint) OK` | 命令通道 OK |
| FJT 执行 + RViz 动 | Plan A 非 dry-run 正常 |
| `Connection reset by peer` | 10002 被旧进程占用 / 重复 Connect |
| `RemoveSubscriber OK` + `No more data available` | **正常断开** |
| `Publisher count: 0` on `/dsr01/joint_states` | MoveIt/emulator 未就绪 |

---

## 6. Agent 回传模板

### [Win11 Unity Report]

```text
Time:
TcpTestSucceeded 127.0.0.1:10002:
Panel: Running / Link / Data / Hz / Stream / Record:
Menu used: Plan B step ...
Console errors/warnings:
Home/A/Halt result:
```

### [WSL2 ROS2 Report]

```text
Time:
Script: start_ros2_unity_link_sim.sh (foreground?)
ss -lntp | grep 10002:
ros2 topic info /dsr01/joint_states:
ros2 topic hz /dsr01/joint_states (3s):
/dt/status/motion echo:
Adapter dry_run param:
RViz motion on Home/A:
Logs: /tmp/ros2_unity_session/
```

---

## 7. 真机联调（明日 100Hz 论文采集）

### 网络

| 路径 | 地址 |
|------|------|
| 真机 → WSL2 | `192.168.137.100:12345`，`mode:=real` |
| Unity → WSL2 | `127.0.0.1:10002`（非真机 IP） |
| `joint_states` | `/dsr01/joint_states` **~100Hz**（真机） |

### 步骤

```text
[WSL2] cleanup_virtual_residuals.sh（可选）
[WSL2] 终端A robot_connection mode:=real
[WSL2] 终端B moveit_only → Update Start State
[WSL2] 终端C start_ros2_unity_link_real.sh（dry_run）
[Win11] Unity Recompile → Play → 连接 → 会话 → 流 → [待机] 论文记录
[期望] mode=real, host=192.168.137.100, Hz≈100, 双端 CSV 同 session
```

**勿开** Docker `dsr01_emulator` · **勿用** `start_ros2_unity_link_sim.sh` 与真机并行占 10002。
