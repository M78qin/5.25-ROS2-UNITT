# AI Handoff Protocol

## Purpose

Keep WSL2 and Win11 Unity agents synchronized without rereading long chat history.

## Directory Layout

- `LATEST.md`: short current state. Read this first every time.
- `PROGRESS.md`: quick index + changed files + checklist.
- `LINK.md`: **Win11 ↔ WSL2 联通对照** (ports, topics, file paths, pass/fail).
- `PROTOCOL.md`: stable collaboration rules.
- `logs/`: dated history. Read only when needed.
- `requests/`: tasks from one agent to the other.
- `responses/`: structured responses from one agent to the other.

## Update Rules

1. Keep `LATEST.md` under roughly 150 lines.
2. Put detailed logs in `logs/YYYY-MM-DD-*.md`.
3. Do not duplicate entire command outputs unless they are essential.
4. Use absolute paths when referencing files.
5. One agent must not silently overwrite the other agent's active code edits.
6. If a Unity scene or asset is changed through Editor/MCP, record the object path and changed fields.

## Ownership

WSL2 agent owns:

- ROS2 launch/scripts
- ROS-TCP endpoint process
- `ros2_unity_bridge`
- MoveIt2/Doosan ROS2 checks
- WSL2 docs

Win11 agent owns:

- Unity Console and Play Mode
- Scene object/Inspector verification
- Unity MCP interactions
- Screenshots and Unity logs
- ROSConnection instance checks

Shared files require explicit note before editing:

- `Assets/DigitalTwin/Scripts/Communication/Ros2Bridge.cs`
- `Assets/DigitalTwin/Scripts/Experiment/Ros2/Ros2ExperimentPanel.cs`
- `Assets/DigitalTwin/Config/TwinRuntimeProfile.asset`
- `Assets/Resources/ROSConnectionPrefab.prefab`

## Minimal Loop

1. WSL2 starts dry-run link.
2. Win11 presses Play and checks Console/ROSConnection.
3. WSL2 watches endpoint and adapter logs.
4. Win11 clicks Ros2ExperimentPanel buttons.
5. Both agents update `LATEST.md` only with the newest conclusion.
