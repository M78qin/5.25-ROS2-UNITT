using System;

namespace DigitalTwin
{
    public enum DigitalTwinMode
    {
        Mirror = 0,
        Plan = 1,
        Execute = 2,
        Replay = 3,
        Fault = 4
    }

    public sealed class DigitalTwinModeService
    {
        public DigitalTwinMode CurrentMode { get; private set; } = DigitalTwinMode.Mirror;
        public string FaultReason { get; private set; } = string.Empty;
        public bool IsMirror => CurrentMode == DigitalTwinMode.Mirror;
        public bool IsPlan => CurrentMode == DigitalTwinMode.Plan;
        public bool IsExecute => CurrentMode == DigitalTwinMode.Execute;
        public bool IsFault => CurrentMode == DigitalTwinMode.Fault;

        public event Action<DigitalTwinMode, DigitalTwinMode> ModeChanged;

        public void Initialize(TwinRuntimeProfile profile)
        {
            Initialize(TwinRuntimeSettings.FromProfile(profile));
        }

        public void Initialize(TwinRuntimeSettings settings)
        {
            if (!IsFault)
            {
                EnterMirror();
            }
        }

        public void EnterMirror() => SetMode(DigitalTwinMode.Mirror, string.Empty);
        public void EnterPlan() => SetMode(DigitalTwinMode.Plan, string.Empty);
        public void EnterExecute() => SetMode(DigitalTwinMode.Execute, string.Empty);
        public void EnterReplay() => SetMode(DigitalTwinMode.Replay, string.Empty);
        public void EnterFault(string reason) => SetMode(DigitalTwinMode.Fault, reason);
        public void ClearFaultToMirror() => EnterMirror();

        private void SetMode(DigitalTwinMode nextMode, string reason)
        {
            DigitalTwinMode previous = CurrentMode;
            CurrentMode = nextMode;
            FaultReason = nextMode == DigitalTwinMode.Fault ? (reason ?? string.Empty) : string.Empty;

            if (previous != nextMode)
            {
                ModeChanged?.Invoke(previous, nextMode);
            }
        }
    }

    public sealed class FeatureSwitchService
    {
        private TwinRuntimeSettings _settings;

        public bool UseDartStudio => _settings == null || _settings.UseDartStudio;
        public bool UseRos2 => _settings != null && _settings.UseRos2;
        public bool UseSqliteReplay => _settings != null && _settings.UseSqliteReplay;
        public bool UseReplay => _settings != null && _settings.UseReplay;
        public bool EnableLiveRobotSync => _settings == null || _settings.EnableLiveRobotSync;
        public bool EnableGhostRobot => _settings != null && _settings.EnableGhostRobot;
        public bool EnableRuntimeUi => _settings == null || _settings.EnableRuntimeUi;
        public bool EnableMetrics => _settings == null || _settings.EnableMetrics;
        public bool EnableRecording => _settings == null || _settings.EnableRecording;
        public bool EnableBidirectionalControl => _settings != null && _settings.EnableBidirectionalControl;
        public bool EnableRealRobotCommand => _settings != null && _settings.EnableRealRobotCommand;
        public bool EnableDryRun => _settings == null || _settings.EnableDryRun;
        public bool RequireStartStateMatchBeforeExecute => _settings == null || _settings.RequireStartStateMatchBeforeExecute;
        public float StartStateToleranceDeg => _settings == null ? 1f : _settings.StartStateToleranceDeg;

        public void Initialize(TwinRuntimeProfile profile)
        {
            Initialize(TwinRuntimeSettings.FromProfile(profile));
        }

        public void Initialize(TwinRuntimeSettings settings)
        {
            _settings = settings;
        }
    }
}
