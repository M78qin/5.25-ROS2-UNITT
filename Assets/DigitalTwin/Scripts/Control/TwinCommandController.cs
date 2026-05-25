using System;
using UnityEngine;

namespace DigitalTwin
{
    public enum TwinMode
    {
        Sync = 0,
        Plan = 1,
        Execute = 2
    }

    public enum RobotCommandType
    {
        MoveJoint = 0,
        Halt = 1,
        Pause = 2,
        Resume = 3
    }

    public readonly struct RobotCommandResult
    {
        public readonly bool Success;
        public readonly bool DryRun;
        public readonly string Status;
        public readonly string ErrorMessage;

        public RobotCommandResult(bool success, bool dryRun, string status, string errorMessage)
        {
            Success = success;
            DryRun = dryRun;
            Status = status;
            ErrorMessage = errorMessage;
        }
    }

    [DisallowMultipleComponent]
    public sealed class TwinCommandController : MonoBehaviour
    {
        [Header("Mode / 模式")]
        [SerializeField, Tooltip("Unity 端当前控制状态。Sync=只跟随真实反馈；Plan=调整目标；Execute=允许发送 MOVE_JOINT。")]
        private TwinMode currentMode = TwinMode.Sync;

        [SerializeField, Tooltip("是否允许进入 Execute。通常由 Profile 的 enableBidirectionalControl 自动写入。")]
        private bool allowExecuteMode;

        [Header("Safety / 安全")]
        [SerializeField, Tooltip("双向控制总开关。关闭时 UI 可看数据，但不会发送 MOVE_JOINT。")]
        private bool enableBidirectionalControl;

        [SerializeField, Tooltip("真实机械臂命令开关。关闭时 MOVE_JOINT/HALT 默认只 dry-run，不会发真实 TCP。")]
        private bool enableRealRobotCommand;

        [SerializeField, Tooltip("干跑模式。开启时只打印将要发送的命令，不控制真实机械臂。第一次联调建议保持开启。")]
        private bool enableDryRun = true;

        [SerializeField, Tooltip("急停锁存状态。开启后 MOVE_JOINT 会被阻止，直到脚本或 Inspector 清除。")]
        private bool emergencyStopped;



        private readonly PlanningControlService _planning = new PlanningControlService();
        private readonly SafetyCheckService _safety = new SafetyCheckService();
        private readonly DigitalTwinModeService _localModeService = new DigitalTwinModeService();
        private DartStudioBridge _bridge;
        private Ros2Bridge _ros2Bridge;
        private DigitalTwinRuntime _runtime;
        private DigitalTwinModeService _modeService;
        private TwinPaperRecorder _paperRecorder;

        public TwinMode CurrentMode => currentMode;
        public bool IsEmergencyStopped => emergencyStopped;
        public bool EnableBidirectionalControl => enableBidirectionalControl;
        public bool EnableRealRobotCommand => enableRealRobotCommand;
        public bool EnableDryRun => enableDryRun;
        public bool CanSendRealMove => enableBidirectionalControl && enableRealRobotCommand && !enableDryRun && !emergencyStopped;
        public RobotStateFrame PlanningStartFrame => _planning.PlanningStartFrame;
        public string LastPlanningStatus => _planning.LastStatus;
        public string LastPlanningError => _planning.LastError;
        public RobotCommandResult LastResult { get; private set; }

        public void Initialize(DigitalTwinRuntime runtime)
        {
            _runtime = runtime;
            _bridge = runtime == null ? GetComponent<DartStudioBridge>() : runtime.DartBridge;
            if (_bridge == null)
            {
                _bridge = FindObjectOfType<DartStudioBridge>();
            }
            _ros2Bridge = runtime == null ? GetComponent<Ros2Bridge>() : runtime.Ros2Bridge;
            if (_ros2Bridge == null)
            {
                _ros2Bridge = FindObjectOfType<Ros2Bridge>();
            }
            _modeService = runtime == null ? _localModeService : runtime.ModeService;
            _modeService.Initialize(runtime == null ? null : runtime.Settings);
            _safety.Initialize(runtime);
            _planning.Initialize(runtime, this, _modeService);
            _paperRecorder = runtime == null ? FindObjectOfType<TwinPaperRecorder>() : runtime.PaperRecorder;
            ApplySettings(runtime == null ? null : runtime.Settings);
        }

        public bool EnterSync()
        {
            _planning.ReleasePreviewToLive();
            currentMode = TwinMode.Sync;
            _modeService?.EnterMirror();
            RobotCommandResult result = SendModeCommand("idle_stream");
            SetResult(result.Success, result.DryRun, result.Status, result.ErrorMessage);
            return true;
        }

        public bool EnterPlan()
        {
            return _planning.EnterPlan();
        }

        public bool EnterExecute()
        {
            if (!allowExecuteMode)
            {
                SetResult(false, enableDryRun || !enableRealRobotCommand, "BLOCKED", "Enable Bidirectional Control before Execute.");
                return false;
            }

            currentMode = TwinMode.Execute;
            _modeService?.EnterExecute();
            return true;
        }

        public RobotCommandResult StartMode1Test()
        {
            bool dryRun = enableDryRun || !enableRealRobotCommand;
            if (!enableBidirectionalControl || !enableRealRobotCommand || enableDryRun)
            {
                return SetResult(false, dryRun, "BLOCKED", "Mode1 test moves the real robot. Enable bidirectional control, real robot command, and disable dry-run first.");
            }

            _planning.ReleasePreviewToLive();
            SetLocalMode(TwinMode.Sync);
            RobotCommandResult result = SendModeCommand("mode1_test");
            return SetResult(result.Success, result.DryRun, result.Status, result.ErrorMessage);
        }

        public RobotCommandResult EnterDartControlMode()
        {
            SetLocalMode(TwinMode.Plan);
            RobotCommandResult result = SendModeCommand("mode2_ctrl");
            return SetResult(result.Success, result.DryRun, result.Status, result.ErrorMessage);
        }

        public RobotCommandResult StopDartTask()
        {
            SetLocalMode(TwinMode.Sync);
            RobotCommandResult result = SendModeCommand("idle_stream");
            return SetResult(result.Success, result.DryRun, result.Status, result.ErrorMessage);
        }

        public void SetEmergencyStopped(bool stopped)
        {
            emergencyStopped = stopped;
        }

        public void SetLocalMode(TwinMode mode)
        {
            currentMode = mode;
            switch (mode)
            {
                case TwinMode.Sync:
                    _modeService?.EnterMirror();
                    break;
                case TwinMode.Plan:
                    _modeService?.EnterPlan();
                    break;
                case TwinMode.Execute:
                    _modeService?.EnterExecute();
                    break;
            }
        }

        public void UpdatePlanTarget(float[] targetJointRad)
        {
            _planning.UpdateGhostTarget(targetJointRad);
        }

        public RobotCommandResult ExecuteTarget(float[] targetJointRad, float speedPercent)
        {
            return _planning.ExecuteTarget(targetJointRad, speedPercent);
        }

        public void CancelPlan()
        {
            _planning.CancelPlan();
        }

        public RobotCommandResult SendMoveJoint(float[] targetJointRad, float speedPercent)
        {
            bool dryRun = enableDryRun || !enableRealRobotCommand;
            SafetyCheckResult safety = _safety.ValidateMoveJoint(targetJointRad, this, _planning.PlanningStartFrame);
            if (!safety.Passed)
            {
                return SetResult(false, safety.DryRun, safety.Status, safety.Message);
            }
            dryRun = safety.DryRun;

            if (dryRun)
            {
                Debug.Log($"DRY_RUN MOVE_JOINT: {BuildMoveJointJson(targetJointRad)}", this);
                RobotCommandResult dryRunResult = SetResult(true, true, "DRY_RUN", string.Empty);
                LogPaperCommand("MOVE_JOINT", "LOCAL_DRY_RUN", BuildMoveJointJson(targetJointRad), dryRunResult, FormatTargetRad(targetJointRad), 0, 0);
                return dryRunResult;
            }

            CommandRoute route = ResolveCommandRoute();
            long sendWallMs = SystemClock.UtcUnixMs();
            long sendNs = SystemClock.NowNs();
            if (route == CommandRoute.Ros2)
            {
                if (_ros2Bridge == null)
                {
                    RobotCommandResult rosUnavailableResult = SetResult(false, false, "ERROR", "Ros2Bridge is unavailable.");
                    LogPaperCommand("MOVE_JOINT", "ROS2", string.Empty, rosUnavailableResult, FormatTargetRad(targetJointRad), sendWallMs, sendNs);
                    return rosUnavailableResult;
                }

                RobotCommandResult rosResult = _ros2Bridge.SendMoveJointRad(targetJointRad, speedPercent);
                RobotCommandResult rosCommandResult = SetResult(rosResult.Success, rosResult.DryRun, rosResult.Status, rosResult.ErrorMessage);
                LogPaperCommand("MOVE_JOINT", "ROS2", string.Empty, rosCommandResult, FormatTargetRad(targetJointRad), sendWallMs, sendNs);
                return rosCommandResult;
            }

            if (route == CommandRoute.DartStudio)
            {
                if (_bridge == null)
                {
                    RobotCommandResult dartUnavailableResult = SetResult(false, false, "ERROR", "DartStudioBridge is unavailable.");
                    LogPaperCommand("MOVE_JOINT", "DartStudio", string.Empty, dartUnavailableResult, FormatTargetRad(targetJointRad), sendWallMs, sendNs);
                    return dartUnavailableResult;
                }

                float[] targetDeg = new float[targetJointRad.Length];
                for (int i = 0; i < targetJointRad.Length; i++)
                {
                    targetDeg[i] = targetJointRad[i] * Mathf.Rad2Deg;
                }

                RobotCommandResult bridgeResult = _bridge.SendMoveJoint(targetDeg);
                RobotCommandResult dartCommandResult = SetResult(bridgeResult.Success, bridgeResult.DryRun, bridgeResult.Status, bridgeResult.ErrorMessage);
                LogPaperCommand("MOVE_JOINT", "DartStudio", DartTcpCommandBuilder.BuildMoveJoint(targetDeg), dartCommandResult, FormatTargetRad(targetJointRad), sendWallMs, sendNs);
                return dartCommandResult;
            }

            RobotCommandResult noRoute = SetResult(false, false, "ERROR", "No active command source is available.");
            LogPaperCommand("MOVE_JOINT", "None", string.Empty, noRoute, FormatTargetRad(targetJointRad), sendWallMs, sendNs);
            return noRoute;
        }

        public RobotCommandResult EmergencyStop()
        {
            emergencyStopped = true;

            CommandRoute route = ResolveCommandRoute();
            long sendWallMs = SystemClock.UtcUnixMs();
            long sendNs = SystemClock.NowNs();
            if (route == CommandRoute.Ros2)
            {
                if (_ros2Bridge == null)
                {
                    RobotCommandResult rosUnavailableResult = SetResult(false, false, "ERROR", "Ros2Bridge is unavailable.");
                    LogPaperCommand("HALT", "ROS2", string.Empty, rosUnavailableResult, string.Empty, sendWallMs, sendNs);
                    return rosUnavailableResult;
                }

                RobotCommandResult rosResult = _ros2Bridge.SendHalt();
                RobotCommandResult rosHaltResult = SetResult(rosResult.Success, rosResult.DryRun, rosResult.Status, rosResult.ErrorMessage);
                LogPaperCommand("HALT", "ROS2", string.Empty, rosHaltResult, string.Empty, sendWallMs, sendNs);
                return rosHaltResult;
            }

            if (route == CommandRoute.DartStudio)
            {
                if (_bridge == null)
                {
                    RobotCommandResult dartUnavailableResult = SetResult(false, false, "ERROR", "DartStudioBridge is unavailable.");
                    LogPaperCommand("HALT", "DartStudio", string.Empty, dartUnavailableResult, string.Empty, sendWallMs, sendNs);
                    return dartUnavailableResult;
                }

                // HALT is allowed to bypass dry-run because it only asks source side to stop motion.
                RobotCommandResult bridgeResult = _bridge.SendHalt();
                RobotCommandResult dartHaltResult = SetResult(bridgeResult.Success, bridgeResult.DryRun, bridgeResult.Status, bridgeResult.ErrorMessage);
                LogPaperCommand("HALT", "DartStudio", DartTcpCommandBuilder.BuildHalt(), dartHaltResult, string.Empty, sendWallMs, sendNs);
                return dartHaltResult;
            }

            RobotCommandResult noRoute = SetResult(false, false, "ERROR", "No active command source is available.");
            LogPaperCommand("HALT", "None", string.Empty, noRoute, string.Empty, sendWallMs, sendNs);
            return noRoute;
        }

        private void ApplySettings(TwinRuntimeSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            enableBidirectionalControl = settings.EnableBidirectionalControl;
            enableRealRobotCommand = settings.EnableRealRobotCommand;
            enableDryRun = settings.EnableDryRun;
            allowExecuteMode = settings.EnableBidirectionalControl;
        }

        private int ResolveJointCount()
        {
            RobotSignalSchema schema = _runtime == null ? null : _runtime.Schema;
            if (schema != null && schema.JointCount > 0)
            {
                return schema.JointCount;
            }

            return 6;
        }

        private string BuildMoveJointJson(float[] targetJointRad)
        {
            if (targetJointRad == null || targetJointRad.Length == 0)
            {
                return string.Empty;
            }

            float[] targetDeg = new float[targetJointRad.Length];
            for (int i = 0; i < targetJointRad.Length; i++)
            {
                targetDeg[i] = targetJointRad[i] * Mathf.Rad2Deg;
            }

            return DartTcpCommandBuilder.BuildMoveJoint(targetDeg);
        }

        private RobotCommandResult SetResult(bool success, bool dryRun, string status, string error)
        {
            LastResult = new RobotCommandResult(success, dryRun, status, error);
            return LastResult;
        }

        private void LogPaperCommand(string action, string route, string commandJson, RobotCommandResult result, string targetSummary, long sendWallMs, long sendNs)
        {
            if (_paperRecorder == null || !_paperRecorder.IsRecording)
            {
                return;
            }

            if (sendWallMs <= 0) sendWallMs = SystemClock.UtcUnixMs();
            if (sendNs <= 0) sendNs = SystemClock.NowNs();
            _paperRecorder.RecordCommand(
                action,
                route,
                string.Empty,
                commandJson,
                result,
                sendWallMs,
                sendNs,
                SystemClock.UtcUnixMs(),
                SystemClock.NowNs(),
                targetSummary);
        }

        private static string FormatTargetRad(float[] targetJointRad)
        {
            if (targetJointRad == null || targetJointRad.Length == 0)
            {
                return string.Empty;
            }

            System.Text.StringBuilder b = new System.Text.StringBuilder(targetJointRad.Length * 12);
            for (int i = 0; i < targetJointRad.Length; i++)
            {
                if (i > 0) b.Append(';');
                b.Append(targetJointRad[i].ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));
            }

            return b.ToString();
        }

        private RobotCommandResult SendModeCommand(string mode)
        {
            CommandRoute route = ResolveCommandRoute();
            long sendWallMs = SystemClock.UtcUnixMs();
            long sendNs = SystemClock.NowNs();
            if (route == CommandRoute.Ros2)
            {
                if (_ros2Bridge == null)
                {
                    RobotCommandResult result = new RobotCommandResult(false, false, "ERROR", "Ros2Bridge is unavailable.");
                    LogPaperCommand("SET_MODE", "ROS2", string.Empty, result, mode, sendWallMs, sendNs);
                    return result;
                }

                RobotCommandResult rosResult = _ros2Bridge.SetMode(mode);
                LogPaperCommand("SET_MODE", "ROS2", string.Empty, rosResult, mode, sendWallMs, sendNs);
                return rosResult;
            }

            if (route == CommandRoute.DartStudio)
            {
                if (_bridge == null)
                {
                    RobotCommandResult result = new RobotCommandResult(false, false, "ERROR", "DartStudioBridge is unavailable.");
                    LogPaperCommand("SET_MODE", "DartStudio", string.Empty, result, mode, sendWallMs, sendNs);
                    return result;
                }

                string json = DartTcpCommandBuilder.BuildSetMode(mode);
                RobotCommandResult bridgeResult = _bridge.SetMode(mode);
                LogPaperCommand("SET_MODE", "DartStudio", json, bridgeResult, mode, sendWallMs, sendNs);
                return bridgeResult;
            }

            RobotCommandResult noRoute = new RobotCommandResult(false, false, "ERROR", "No active command source is available.");
            LogPaperCommand("SET_MODE", "None", string.Empty, noRoute, mode, sendWallMs, sendNs);
            return noRoute;
        }

        private CommandRoute ResolveCommandRoute()
        {
            if (_runtime != null)
            {
                if (_runtime.ActiveSourceKind == RuntimeSourceKind.Ros2)
                {
                    return CommandRoute.Ros2;
                }

                if (_runtime.ActiveSourceKind == RuntimeSourceKind.DartStudio)
                {
                    return CommandRoute.DartStudio;
                }
            }

            if (_ros2Bridge != null)
            {
                return CommandRoute.Ros2;
            }

            if (_bridge != null)
            {
                return CommandRoute.DartStudio;
            }

            return CommandRoute.None;
        }

        private enum CommandRoute
        {
            None = 0,
            DartStudio = 1,
            Ros2 = 2
        }

    }
}
