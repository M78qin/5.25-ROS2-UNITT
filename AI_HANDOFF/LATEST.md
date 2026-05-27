# AI Handoff Latest

Last updated: 2026-05-27 14:38 CST · **真机会话进行中**

## Read First（START HERE）

**本轮会话：** [`logs/2026-05-27-real-session-start.md`](logs/2026-05-27-real-session-start.md)

**WSL2 操作清单：**  
`/home/zyq/ros2_ws/src/docs/progress/2026-05-27_real_session_start.md`

**真机协调：** `.../2026-05-26_real_robot_unity_coordination.md`  
**联通对照：** [`LINK.md`](LINK.md)

---

## Current Goal

**进行中**：真机 100Hz → Unity 同步 → 待机采集 / 控制小动。

> Agent 已清残留；**请你在终端 A 起 robot_connection**，通过后 B/C + Unity。

---

## Status Summary

| Item | WSL2 | Win11 Unity |
|------|------|-------------|
| 环境清理 | **done** | — |
| robot_connection | **待启动 A** | — |
| Unity :10002 | **待 C** | `127.0.0.1:10002` |
| 100Hz 双端 CSV | **验收中** | **验收中** |
| 待机/控制/Capture | ready | **已编译** |

---

## 网络

- 真机：`192.168.137.100:12345`（WSL2 `mode:=real`）
- Unity：**仅** `127.0.0.1:10002` → WSL2 ROS-TCP

---

## WSL2 三终端（推进时）

```bash
# A 真机
ros2 launch dsr_moveit_config_h2515 robot_connection.launch.py mode:=real host:=192.168.137.100 ...

# B RViz
ros2 launch dsr_moveit_config_h2515 moveit_only.launch.py gui:=true

# C Unity 链路
/home/zyq/ros2_ws/src/ros2-unity-bridge/scripts/start_ros2_unity_link_real.sh
```

---

## Unity 流程

```text
Play → 解析绑定 → 连接 ROS2
→ 会话 → 通道 → 数据流 (Hz≈100)
→ [待机] Capture → 开启论文记录
→ (可选) 控制模式 → 真机/ dry-run execute
```

**待机**：可通道/流/记录/Capture；**禁** Home/A/B。  
菜单：`DigitalTwin/ROS2 Plan B/*`

---

## 挂载

**不需要** 新挂组件：`Ros2Experimen` + `4.24-Try/ROS2` 已就位。

---

## Known Issues

| ID | Note |
|----|------|
| R1–R2 | 勿开 emulator / virtual 僵尸 control |
| I008 | 真机小位移待复验 |
| P7 | 勿让 dryrun 与 real 同占 10002 |

---

## Do Not Overwrite

历史日志仅追加，见 `logs/` 各 dated 文件。
