# Communication/lib — TCP 命令薄层

> 上级：[Communication/README.md](../README.md)

## 职责

把 DartStudio TCP 命令 JSON **集中在一处**构造，供 Bridge、UI、Experiment 复用。**不创建**独立 TCP 连接。

## 关键文件

| 文件 | 作用 |
|------|------|
| `DartTcpCommandBuilder.cs` | `SET_MODE` / `MOVE_JOINT` / `HALT` / `GET_STATE` / `HEARTBEAT` |
| `DartTcpCommandSender.cs` | MonoBehaviour 包装，调用 `DartStudioBridge.SendRawCommand` |
| `Editor/DartTcpCommandSenderEditor.cs` | Inspector 预设按钮（Mode Idle/Test/Ctrl、Move A/B 等） |

## 命令格式

```json
{"cmd":"SET_MODE","mode":"idle_stream"}
{"cmd":"MOVE_JOINT","target":[0,0,0,0,0,0]}
{"cmd":"HALT"}
```

`MOVE_JOINT` **不得**带 `speed/vel/acc`（DRL 固定速度）；Bridge 会 BLOCK。

## 改进定位

| 想改什么 | 文件 |
|----------|------|
| 新增 TCP 命令类型 | `DartTcpCommandBuilder.cs` + DRL 协议对齐 |
| Inspector 测试按钮 | `Editor/DartTcpCommandSenderEditor.cs` |
| 发送路径 / 错误状态 | `DartStudioBridge.SendJsonCommand`（父目录） |

## 硬规则

- 所有 TCP 发送必须经 `DartStudioBridge`，Sender 不得 new `TcpClient`
