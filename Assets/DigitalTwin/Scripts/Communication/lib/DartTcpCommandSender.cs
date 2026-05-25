using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// Thin command wrapper that only builds command strings and sends through DartStudioBridge.
    /// No extra TCP socket is created here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DartTcpCommandSender : MonoBehaviour
    {
        [SerializeField, Tooltip("复用已有 DartStudioBridge 的 TCP 发送链路。")]
        private DartStudioBridge bridge;

        [SerializeField, Tooltip("为空时是否自动查找场景里的 DartStudioBridge。")]
        private bool autoResolveBridge = true;



        [Header("Preset Commands")]
        [SerializeField, Tooltip("预设模式：空闲流。")]
        private string presetModeIdle = "idle_stream";

        [SerializeField, Tooltip("预设模式：模式一测试。")]
        private string presetModeTest = "mode1_test";

        [SerializeField, Tooltip("预设模式：模式二控制。")]
        private string presetModeControl = "mode2_ctrl";

        [SerializeField, Tooltip("预设关节目标 A（degree）。")]
        private float[] presetMoveTargetA = new float[] { 0f, 0f, 0f, 0f, 0f, 0f };

        [SerializeField, Tooltip("预设关节目标 B（degree）。")]
        private float[] presetMoveTargetB = new float[] { 15f, -10f, 20f, 0f, 10f, 0f };


        [TextArea(2, 6)]
        [SerializeField, Tooltip("预设 RAW JSON 命令，可在运行时从 Inspector 直接发送。")]
        private string presetRawJson = "{\"cmd\":\"GET_STATE\"}";

        [Header("Runtime Status (read-only)")]
        [SerializeField] private string lastAction = "none";
        [SerializeField] private string lastStatus = "none";
        [SerializeField] private string lastError = string.Empty;
        [SerializeField] private string lastCommandJson = string.Empty;

        [SerializeField] private bool verboseLog;

        public DartStudioBridge Bridge => bridge;

        public void Bind(DartStudioBridge target)
        {
            bridge = target;
        }

        public RobotCommandResult SendSetMode(string mode)
        {
            string json = DartTcpCommandBuilder.BuildSetMode(mode);
            if (string.IsNullOrEmpty(json))
            {
                return Remember("SET_MODE", json, new RobotCommandResult(false, false, "BLOCKED", "Mode is empty."));
            }

            return SendBuiltCommand("SET_MODE", json);
        }

        public RobotCommandResult SendMoveJointDeg(float[] targetDeg)
        {
            string json = DartTcpCommandBuilder.BuildMoveJoint(targetDeg);
            if (string.IsNullOrEmpty(json))
            {
                return Remember("MOVE_JOINT", json, new RobotCommandResult(false, false, "BLOCKED", "Target is empty."));
            }

            return SendBuiltCommand("MOVE_JOINT", json);
        }

        public RobotCommandResult SendHalt()
        {
            return SendBuiltCommand("HALT", DartTcpCommandBuilder.BuildHalt());
        }

        public RobotCommandResult SendGetState()
        {
            return SendBuiltCommand("GET_STATE", DartTcpCommandBuilder.BuildGetState());
        }

        public RobotCommandResult SendRaw(string json)
        {
            return SendBuiltCommand("RAW", json);
        }

        public RobotCommandResult SendPresetModeIdle()
        {
            return SendSetMode(presetModeIdle);
        }

        public RobotCommandResult SendPresetModeTest()
        {
            return SendSetMode(presetModeTest);
        }

        public RobotCommandResult SendPresetModeControl()
        {
            return SendSetMode(presetModeControl);
        }

        public RobotCommandResult SendPresetMoveA()
        {
            return SendMoveJointDeg(presetMoveTargetA);
        }

        public RobotCommandResult SendPresetMoveB()
        {
            return SendMoveJointDeg(presetMoveTargetB);
        }

        public RobotCommandResult SendPresetHalt()
        {
            return SendHalt();
        }

        public RobotCommandResult SendPresetGetState()
        {
            return SendGetState();
        }

        public RobotCommandResult SendPresetRaw()
        {
            return SendRaw(presetRawJson);
        }

        private RobotCommandResult SendBuiltCommand(string action, string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return Remember(action, json, new RobotCommandResult(false, false, "BLOCKED", "Command is empty."));
            }

            if (!TryResolveBridge(out DartStudioBridge target))
            {
                return Remember(action, json, new RobotCommandResult(false, false, "ERROR", "DartStudioBridge is unavailable."));
            }

            RobotCommandResult result = target.SendRawCommand(json);
            Remember(action, json, result);
            if (verboseLog)
            {
                Debug.Log($"[DartTcpCommandSender] {result.Status} -> {json}", this);
            }

            return result;
        }

        private RobotCommandResult Remember(string action, string json, RobotCommandResult result)
        {
            lastAction = string.IsNullOrEmpty(action) ? "none" : action;
            lastCommandJson = json ?? string.Empty;
            lastStatus = result.Status ?? string.Empty;
            lastError = result.ErrorMessage ?? string.Empty;
            return result;
        }

        private bool TryResolveBridge(out DartStudioBridge target)
        {
            if (bridge != null)
            {
                target = bridge;
                return true;
            }

            if (autoResolveBridge)
            {
                bridge = FindObjectOfType<DartStudioBridge>();
            }

            target = bridge;
            return target != null;
        }
    }
}
