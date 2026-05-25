using UnityEngine;

namespace DigitalTwin
{
    public readonly struct SafetyCheckResult
    {
        public readonly bool Passed;
        public readonly bool DryRun;
        public readonly string Status;
        public readonly string Message;

        public SafetyCheckResult(bool passed, bool dryRun, string status, string message)
        {
            Passed = passed;
            DryRun = dryRun;
            Status = status;
            Message = message;
        }
    }

    public sealed class SafetyCheckService
    {
        private DigitalTwinRuntime _runtime;
        private TwinRuntimeSettings _settings;
        private RobotModelController _robotModel;
        private DartStudioBridge _bridge;
        private Ros2Bridge _ros2Bridge;

        public void Initialize(DigitalTwinRuntime runtime)
        {
            _runtime = runtime;
            _settings = runtime == null ? null : runtime.Settings;
            _robotModel = runtime == null ? null : runtime.RobotModel;
            _bridge = runtime == null ? null : runtime.DartBridge;
            _ros2Bridge = runtime == null ? null : runtime.Ros2Bridge;
        }

        public SafetyCheckResult ValidateMoveJoint(
            float[] targetJointRad,
            TwinCommandController command,
            RobotStateFrame planningStartFrame)
        {
            bool dryRun = command == null || command.EnableDryRun || !command.EnableRealRobotCommand;

            if (command == null)
            {
                return Block(dryRun, "ERROR", "TwinCommandController is unavailable.");
            }

            if (command.CurrentMode != TwinMode.Execute)
            {
                return Block(dryRun, "BLOCKED", "Unity is not in Execute mode.");
            }

            if (command.IsEmergencyStopped)
            {
                return Block(dryRun, "BLOCKED", "Emergency stop is active.");
            }

            if (!command.EnableBidirectionalControl)
            {
                return Block(dryRun, "BLOCKED", "Bidirectional control is disabled.");
            }

            int expectedCount = ResolveJointCount();
            if (targetJointRad == null || targetJointRad.Length != expectedCount)
            {
                return Block(dryRun, "BLOCKED", $"Target joint count must be {expectedCount}.");
            }

            for (int i = 0; i < targetJointRad.Length; i++)
            {
                if (float.IsNaN(targetJointRad[i]) || float.IsInfinity(targetJointRad[i]))
                {
                    return Block(dryRun, "BLOCKED", $"Target J{i + 1} is not finite.");
                }

                if (_robotModel != null)
                {
                    float targetDeg = targetJointRad[i] * Mathf.Rad2Deg;
                    Vector2 limits = _robotModel.GetJointLimitsDeg(i);
                    const float epsilonDeg = 0.001f;
                    if (targetDeg < limits.x - epsilonDeg || targetDeg > limits.y + epsilonDeg)
                    {
                        return Block(dryRun, "BLOCKED", $"Target J{i + 1}={targetDeg:F3} deg exceeds [{limits.x:F3}, {limits.y:F3}] deg.");
                    }
                }
            }

            if (!dryRun)
            {
                RuntimeSourceKind route = _runtime == null ? RuntimeSourceKind.None : _runtime.ActiveSourceKind;
                if (route == RuntimeSourceKind.Ros2)
                {
                    if (_ros2Bridge == null)
                    {
                        return Block(false, "ERROR", "Ros2Bridge is unavailable.");
                    }

                    if (!_ros2Bridge.IsConnected)
                    {
                        return Block(false, "BLOCKED", "Ros2Bridge is not connected.");
                    }
                }
                else if (route == RuntimeSourceKind.DartStudio)
                {
                    if (_bridge == null)
                    {
                        return Block(false, "ERROR", "DartStudioBridge is unavailable.");
                    }

                    if (!_bridge.IsConnected)
                    {
                        return Block(false, "BLOCKED", "DartStudioBridge is not connected.");
                    }
                }
                else
                {
                    return Block(false, "ERROR", "No active command source is available.");
                }
            }

            if (!dryRun && RequireStartMatch() && !StartStateMatches(planningStartFrame, out string startError))
            {
                return Block(false, "BLOCKED", startError);
            }

            return new SafetyCheckResult(true, dryRun, dryRun ? "DRY_RUN" : "OK", string.Empty);
        }

        private bool StartStateMatches(RobotStateFrame planningStartFrame, out string error)
        {
            error = string.Empty;
            if (planningStartFrame == null || planningStartFrame.JointPositionRad == null)
            {
                error = "Planning start frame is missing.";
                return false;
            }

            if (_runtime == null || !_runtime.TryGetLatestFrame(out RobotStateFrame latest) || latest.JointPositionRad == null)
            {
                error = "Latest robot frame is missing.";
                return false;
            }

            int count = Mathf.Min(planningStartFrame.JointPositionRad.Length, latest.JointPositionRad.Length);
            float tolerance = _settings == null ? 1f : Mathf.Max(0f, _settings.StartStateToleranceDeg);
            for (int i = 0; i < count; i++)
            {
                float deltaDeg = Mathf.Abs(Mathf.DeltaAngle(planningStartFrame.JointPositionRad[i] * Mathf.Rad2Deg, latest.JointPositionRad[i] * Mathf.Rad2Deg));
                if (deltaDeg > tolerance)
                {
                    error = $"Start-state mismatch: J{i + 1} drifted {deltaDeg:F3} deg > {tolerance:F3} deg.";
                    return false;
                }
            }

            return true;
        }

        private bool RequireStartMatch()
        {
            return _settings == null || _settings.RequireStartStateMatchBeforeExecute;
        }

        private int ResolveJointCount()
        {
            if (_runtime != null && _runtime.Schema != null && _runtime.Schema.JointCount > 0)
            {
                return _runtime.Schema.JointCount;
            }

            if (_robotModel != null && _robotModel.JointCount > 0)
            {
                return _robotModel.JointCount;
            }

            return 6;
        }

        private static SafetyCheckResult Block(bool dryRun, string status, string message)
        {
            return new SafetyCheckResult(false, dryRun, status, message);
        }
    }
}
