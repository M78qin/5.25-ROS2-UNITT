# AI Handoff Progress Index

**Read order:** [`LATEST.md`](LATEST.md) → [`logs/2026-05-26-ready-to-push.md`](logs/2026-05-26-ready-to-push.md) → [`LINK.md`](LINK.md)

## One-line status（2026-05-26 晚 · READY TO PUSH）

| Item | Status |
|------|--------|
| WSL2 真机 + RViz | **已通** |
| Unity 代码合入（待机/Capture/session/record） | **DONE** |
| Unity 编译 | **0 error / 0 warning** |
| TCP Link + Plan B 菜单 | **DONE** |
| **100Hz 双端 CSV 联合验收** | **NEXT — 开始推进** |

---

## Progress logs

| Date | Log | Topic |
|------|-----|-------|
| 2026-05-26 | [logs/2026-05-26-ready-to-push.md](logs/2026-05-26-ready-to-push.md) | **准备推进 · 真机 100Hz 验收清单** |
| — | WSL2 `docs/progress/2026-05-26_handoff_real_robot_tomorrow.md` | 三终端 + 真机协调 |
| — | WSL2 `docs/progress/2026-05-26_real_robot_unity_coordination.md` | 真机细节 |

---

## Checklist — 推进阶段

### Phase 0 代码（已完成）
- [x] WSL2 真机脚本 + session gate + unified record
- [x] Unity 待机/控制、Capture、session status
- [x] Ros2Bridge TCP 显式连接 + Reconnect 菜单
- [x] 编译修复（LastError、CapturedPosePersistence public、session topic）
- [x] TwinPaperRecorder flags 对齐

### Phase 1 环境（下一步）
- [ ] WSL2 三终端：真机 + RViz + `start_ros2_unity_link_real.sh`
- [ ] `ros2 topic hz /dsr01/joint_states` ≈ 100
- [ ] `Test-NetConnection 127.0.0.1 -Port 10002` True

### Phase 2 Unity 面板
- [ ] Play → Plan B Steps 1-5 或手动四步
- [ ] Link=ON, Data=ON, Hz≈100
- [ ] Capture → 退出 Play 写回 Inspector（可选验）

### Phase 3 论文
- [ ] 待机下开记录 → `record_enabled` → RECORDING
- [ ] Unity + WSL2 CSV 同 session 时间窗
- [ ] 控制模式 + 真机小动（I008 复验）

### 历史（虚拟联调，非当前主线）
- [x] TCP dry-run `/dt/cmd/*`
- [x] Plan A sim / Plan B 菜单（虚拟环境部分验证）

---

## Win11 changed files（累计 · 真机推进相关）

| Path | Change |
|------|--------|
| `Ros2ExperimentPanel.cs` | 待机/控制、Capture、record_pending、session |
| `Ros2Bridge.cs` | TCP、session 订阅、`ApplySessionStatusTopic` |
| `Ros2SessionStatus.cs` | session JSON DTO |
| `Ros2CapturedPosePersistence.cs` | Capture 缓冲（public） |
| `Editor/Ros2ExperimentPanelPlayModePersistence.cs` | Play 退出写回 |
| `Editor/Ros2ExperimentPanelEditor.cs` | 待机 UI、Plan B、session topic |
| `TwinPaperRecorder.cs` | flags 列 |
| `Ros2TelemetryChannels.cs` | 通道 map |

## WSL2 changed files（累计）

| Path | Change |
|------|--------|
| `scripts/start_ros2_unity_link_real.sh` | **真机 Unity 链路主入口** |
| `scripts/start_ros2_unity_link_sim.sh` | 虚拟 sim（非当前主线） |
| `session_gate.py`, `session_controller_node.py` | stream/record 门控 |
| `cleanup_virtual_residuals.sh` | 清 emulator 残留 |

## Scene (SampleScene)

| Hierarchy | Component |
|-----------|-----------|
| `4.24-Try/DigitalTwin_System` | Runtime + Command + PaperRecorder |
| `4.24-Try/ROS2` | `Ros2Bridge`（127.0.0.1:10002） |
| `Ros2Experimen` | `Ros2ExperimentPanel` |
| `4.24-Try/DART` | `DartStudioBridge`（默认关 debug GUI） |
