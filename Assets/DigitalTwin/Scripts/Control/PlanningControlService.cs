using System;
using UnityEngine;

namespace DigitalTwin
{
    public sealed class PlanningControlService
    {
        private DigitalTwinRuntime _runtime;
        private RobotModelController _robotModel;
        private TwinCommandController _command;
        private DigitalTwinModeService _modeService;
        private RobotStateFrame _planningStartFrame;
        private float[] _targetJointRad;
        private bool _freezeLiveRobotDuringPlan = true;
        private bool _showGhostDuringPlan = true;

        public RobotStateFrame PlanningStartFrame => _planningStartFrame == null ? null : _planningStartFrame.Clone();
        public bool HasPlanningStartFrame => _planningStartFrame != null;
        public string LastStatus { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;

        public void Initialize(DigitalTwinRuntime runtime, TwinCommandController command, DigitalTwinModeService modeService)
        {
            _runtime = runtime;
            _robotModel = runtime == null ? null : runtime.RobotModel;
            _command = command;
            _modeService = modeService;
        }

        public bool EnterPlan()
        {
            if (_runtime == null || !_runtime.TryGetLatestFrame(out RobotStateFrame latest) || latest.JointPositionRad == null)
            {
                SetStatus("BLOCKED", "Cannot enter Plan: latest robot frame is missing.");
                return false;
            }

            _planningStartFrame = latest.Clone();
            _targetJointRad = CloneArray(latest.JointPositionRad);

            if (_freezeLiveRobotDuringPlan && _robotModel != null)
            {
                _robotModel.SetControlAuthority(RobotModelController.ControlAuthority.CommandPreview);
            }

            if (_robotModel != null)
            {
                _robotModel.SetGhostVisible(_showGhostDuringPlan);
            }

            _modeService?.EnterPlan();
            RobotCommandResult modeResult = _command == null
                ? new RobotCommandResult(false, false, "ERROR", "TwinCommandController is unavailable.")
                : _command.EnterDartControlMode();
            SetStatus(modeResult.Success ? "PLAN" : modeResult.Status, modeResult.ErrorMessage);
            return modeResult.Success;
        }

        public void UpdateGhostTarget(float[] targetJointRad)
        {
            _targetJointRad = CloneArray(targetJointRad);
        }

        public RobotCommandResult ExecuteTarget(float[] targetJointRad, float speedPercent)
        {
            UpdateGhostTarget(targetJointRad);

            if (_command == null)
            {
                SetStatus("ERROR", "TwinCommandController is unavailable.");
                return new RobotCommandResult(false, false, "ERROR", LastError);
            }

            if (!_command.EnterExecute())
            {
                RobotCommandResult blocked = _command.LastResult;
                SetStatus(blocked.Status, blocked.ErrorMessage);
                return blocked;
            }

            _modeService?.EnterExecute();
            RobotCommandResult result = _command.SendMoveJoint(_targetJointRad, speedPercent);
            SetStatus(result.Status, result.ErrorMessage);

            if (!result.DryRun && result.Success)
            {
                ReleasePreviewToLive();
            }
            else
            {
                _command.SetLocalMode(TwinMode.Plan);
                _modeService?.EnterPlan();
            }

            return result;
        }

        public void CancelPlan()
        {
            _planningStartFrame = null;
            _targetJointRad = null;
            ReleasePreviewToLive();
            _command?.StopDartTask();
            _modeService?.EnterMirror();
            SetStatus("MIRROR", string.Empty);
        }

        public void ReleasePreviewToLive()
        {
            if (_robotModel != null)
            {
                _robotModel.SetGhostVisible(false);
                _robotModel.ReleaseToLiveFeedback();
            }
        }

        private void SetStatus(string status, string error)
        {
            LastStatus = status ?? string.Empty;
            LastError = error ?? string.Empty;
        }

        private static float[] CloneArray(float[] source)
        {
            if (source == null)
            {
                return null;
            }

            float[] copy = new float[source.Length];
            Array.Copy(source, copy, source.Length);
            return copy;
        }
    }
}
