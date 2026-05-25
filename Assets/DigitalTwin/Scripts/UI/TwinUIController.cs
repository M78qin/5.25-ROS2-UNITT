using System;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin
{
    [DisallowMultipleComponent]
    public sealed class TwinUIController : MonoBehaviour
    {
        [Header("Panels / 面板")]
        [SerializeField, Tooltip("是否显示关节角度面板。")]
        private bool showJointPanel = true;

        [SerializeField, Tooltip("是否显示六轴力/力矩面板。")]
        private bool showForcePanel = true;

        [SerializeField, Tooltip("是否显示帧率、延迟、抖动、丢帧等指标面板。")]
        private bool showMetricsPanel = true;

        [SerializeField, Tooltip("是否显示 CSV 记录状态面板。")]
        private bool showRecordPanel = true;

        [SerializeField, Tooltip("是否显示 DartStudio 连接状态面板。")]
        private bool showConnectionPanel = true;

        [SerializeField, Tooltip("是否显示实验状态面板。")]
        private bool showExperimentPanel = true;

        [Header("Auto UI / 自动生成UI")]
        [SerializeField, Tooltip("没有手动绑定 TMP 文本、按钮、滑块时，Play 模式自动创建 HUD。编辑器内建议用右键菜单预生成。")]
        private bool autoCreateRuntimeUI = true;

        [SerializeField, Tooltip("自动 UI 面板右上角位置，单位像素。")]
        private Vector2 autoPanelPosition = new Vector2(-28f, -28f);

        [SerializeField, Tooltip("自动 UI 面板尺寸，单位像素。")]
        private Vector2 autoPanelSize = new Vector2(520f, 920f);

        [SerializeField, Tooltip("ROS2 speed hint. DartStudio ignores Unity speed and uses DRL MOVE_VEL/MOVE_ACC.")]
        private float executeSpeedPercent = 20f;

        [Header("Text Targets / 文本绑定")]
        [SerializeField] private TMP_Text jointText;
        [SerializeField] private TMP_Text forceText;
        [SerializeField] private TMP_Text metricsText;
        [SerializeField] private TMP_Text recordText;
        [SerializeField] private TMP_Text connectionText;
        [SerializeField] private TMP_Text commandText;
        [SerializeField] private TMP_Text experimentText;

        [Header("Controls / 控件绑定")]
        [SerializeField] private Button idleButton;
        [SerializeField] private Button mode1Button;
        [SerializeField] private Button planButton;
        [SerializeField] private Button executeButton;
        [SerializeField] private Button haltButton;
        [SerializeField] private GameObject sliderGroup;
        [SerializeField] private Slider[] jointSliders;
        [SerializeField] private TMP_Text[] jointSliderLabels;

        private static readonly Color OnlineColor = new Color(0.38f, 0.82f, 0.42f, 1f);
        private static readonly Color StaleColor = new Color(0.52f, 0.58f, 0.62f, 1f);
        private static readonly Color ReconnectColor = new Color(1f, 0.78f, 0.24f, 1f);
        private static readonly Color InitColor = new Color(0.28f, 0.32f, 0.36f, 1f);
        private static readonly Color CyanColor = new Color(0.1f, 0.72f, 0.88f, 1f);
        private static readonly Color OrangeColor = new Color(1f, 0.58f, 0.18f, 1f);
        private static readonly Color RedColor = new Color(0.92f, 0.16f, 0.14f, 1f);
        private static readonly Color TextColor = new Color(0.9f, 0.95f, 1f, 1f);

        private const float JointRefreshInterval = 1f / 20f;
        private const float ForceRefreshInterval = 1f / 20f;
        private const float CommandRefreshInterval = 1f / 10f;
        private const float ConnectionRefreshInterval = 1f / 5f;
        private const float MetricsRefreshInterval = 1f / 3f;
        private const float CsvRefreshInterval = 1f;
        private const float ExperimentRefreshInterval = 1f / 2f;
        private const float SliderDebounceSeconds = 0.1f;

        private DigitalTwinRuntime _runtime;
        private TwinRecorder _recorder;
        private TwinCommandController _command;
        private RobotModelController _robotModel;
        private TwinRuntimeUIBindings _bindings;
        private TwinExperimentTracker _experimentTracker;
        private GameObject _autoCanvas;
        private bool _syncingSliders;
        private bool _buttonEventsBound;
        private bool _sliderDirty;
        private float _sliderDebounceTimer;
        private float _jointTimer;
        private float _forceTimer;
        private float _commandTimer;
        private float _connectionTimer;
        private float _metricsTimer;
        private float _csvTimer;
        private float _experimentTimer;

        public void Initialize(DigitalTwinRuntime runtime, TwinRecorder recorder)
        {
            Initialize(runtime, recorder, runtime == null ? null : runtime.CommandController);
        }

        public void Initialize(DigitalTwinRuntime runtime, TwinRecorder recorder, TwinCommandController command)
        {
            _runtime = runtime;
            _recorder = recorder;
            _command = command;
            _robotModel = runtime == null ? null : runtime.RobotModel;
            ApplyProfile(runtime == null ? null : runtime.Profile);
            EnsureRuntimeUI();
            ResolveBindingsFromHierarchy();
            WireButtonEvents();
            ApplySliderLimits();
            ApplyVisibility();
            RefreshSlidersFromLatestFrame();
            RefreshAllImmediate();
        }

        public void SetExperimentTracker(TwinExperimentTracker tracker)
        {
            _experimentTracker = tracker;
        }

        public void ApplyGeneratedBindings(TwinRuntimeUIBindings bindings)
        {
            if (bindings == null)
            {
                return;
            }

            _bindings = bindings;
            connectionText = bindings.ConnectionText;
            jointText = bindings.JointText;
            forceText = bindings.ForceText;
            metricsText = bindings.MetricsText;
            recordText = bindings.RecordText;
            commandText = bindings.CommandText;
            idleButton = bindings.IdleButton;
            mode1Button = bindings.Mode1Button;
            planButton = bindings.PlanButton;
            executeButton = bindings.ExecuteButton;
            haltButton = bindings.HaltButton;
            sliderGroup = bindings.SliderGroup;
            jointSliders = bindings.JointSliders;
            jointSliderLabels = bindings.JointSliderLabels;
        }

        public void Refresh()
        {
            RefreshControlVisibility();
            UpdateExecuteButtonVisual();
            RefreshHeader();
        }

        private void Update()
        {
            if (_runtime == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            TickRefresh(dt);
            TickSliderDebounce(dt);
        }

        private void ApplyProfile(TwinRuntimeProfile profile)
        {
            if (profile == null)
            {
                return;
            }

            TwinRuntimeSettings settings = profile.BuildRuntimeSettings();
            showJointPanel = settings.ShowJointPanel;
            showForcePanel = settings.ShowForcePanel;
            showMetricsPanel = settings.ShowMetricsPanel;
            showRecordPanel = settings.ShowRecordPanel;
            showConnectionPanel = settings.ShowConnectionPanel;
            showExperimentPanel = settings.ShowExperimentPanel;
        }

        private void EnsureRuntimeUI()
        {
            if (!Application.isPlaying || !autoCreateRuntimeUI || connectionText != null)
            {
                return;
            }

            TwinRuntimeUIBindings bindings = TwinRuntimeUIFactory.CreateRuntimeCanvas(autoPanelPosition, autoPanelSize);
            _autoCanvas = bindings.CanvasRoot;
            ApplyGeneratedBindings(bindings);
        }

        private void ResolveBindingsFromHierarchy()
        {
            if (_bindings != null)
            {
                return;
            }

            Transform anchor = connectionText != null ? connectionText.transform : transform;
            _bindings = anchor.GetComponentInParent<TwinRuntimeUIBindings>();
        }

        private void WireButtonEvents()
        {
            if (_buttonEventsBound)
            {
                return;
            }

            AddClick(idleButton, OnIdleClicked);
            AddClick(mode1Button, () => _command?.StartMode1Test());
            AddClick(planButton, OnPlanClicked);
            AddClick(executeButton, OnExecuteClicked);
            AddClick(haltButton, () => _command?.EmergencyStop());
            AddClick(_bindings == null ? null : _bindings.ConfirmExecuteButton, OnConfirmRealExecute);
            AddClick(_bindings == null ? null : _bindings.CancelExecuteButton, HideRealExecuteConfirm);

            if (jointSliders != null)
            {
                for (int i = 0; i < jointSliders.Length; i++)
                {
                    int index = i;
                    if (jointSliders[i] != null)
                    {
                        jointSliders[i].onValueChanged.AddListener(_ => OnSliderChanged(index));
                    }
                }
            }

            _buttonEventsBound = true;
        }

        private static void AddClick(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null && action != null)
            {
                button.onClick.AddListener(action);
            }
        }

        private void ApplyVisibility()
        {
            SetCardState(_bindings == null ? null : _bindings.JointsCard, showJointPanel);
            SetCardState(_bindings == null ? null : _bindings.ForceCard, showForcePanel);
            SetCardState(_bindings == null ? null : _bindings.MetricsCard, showMetricsPanel || showRecordPanel);
            SetCardState(_bindings == null ? null : _bindings.ConnectionCard, showConnectionPanel);
            SetActive(jointText, showJointPanel);
            SetActive(forceText, showForcePanel);
            SetActive(metricsText, showMetricsPanel);
            SetActive(recordText, showRecordPanel);
            SetActive(connectionText, showConnectionPanel);
            SetActive(experimentText, showExperimentPanel && _experimentTracker != null && _experimentTracker.IsExperimentActive);
        }

        private void TickRefresh(float dt)
        {
            RefreshControlVisibility();
            UpdateExecuteButtonVisual();

            _jointTimer += dt;
            if (_jointTimer >= JointRefreshInterval)
            {
                _jointTimer = 0f;
                if (showJointPanel && CardAllowsBody(_bindings == null ? null : _bindings.JointsCard)) RefreshJointPanel();
            }

            _forceTimer += dt;
            if (_forceTimer >= ForceRefreshInterval)
            {
                _forceTimer = 0f;
                if (showForcePanel && CardAllowsBody(_bindings == null ? null : _bindings.ForceCard)) RefreshForcePanel();
            }

            _commandTimer += dt;
            if (_commandTimer >= CommandRefreshInterval)
            {
                _commandTimer = 0f;
                if (CardAllowsBody(_bindings == null ? null : _bindings.CommandCard)) RefreshCommandPanel();
            }

            _connectionTimer += dt;
            if (_connectionTimer >= ConnectionRefreshInterval)
            {
                _connectionTimer = 0f;
                RefreshHeader();
                if (showConnectionPanel && CardAllowsBody(_bindings == null ? null : _bindings.ConnectionCard)) RefreshConnectionPanel();
            }

            _metricsTimer += dt;
            if (_metricsTimer >= MetricsRefreshInterval)
            {
                _metricsTimer = 0f;
                if (showMetricsPanel && CardAllowsBody(_bindings == null ? null : _bindings.MetricsCard)) RefreshMetricsPanel();
            }

            _csvTimer += dt;
            if (_csvTimer >= CsvRefreshInterval)
            {
                _csvTimer = 0f;
                if (showRecordPanel && CardAllowsBody(_bindings == null ? null : _bindings.MetricsCard)) RefreshRecordPanel();
            }

            _experimentTimer += dt;
            if (_experimentTimer >= ExperimentRefreshInterval)
            {
                _experimentTimer = 0f;
                if (showExperimentPanel) RefreshExperimentPanel();
            }
        }

        private void TickSliderDebounce(float dt)
        {
            if (!_sliderDirty)
            {
                return;
            }

            _sliderDebounceTimer += dt;
            if (_sliderDebounceTimer < SliderDebounceSeconds)
            {
                return;
            }

            _sliderDirty = false;
            _sliderDebounceTimer = 0f;
            _command?.UpdatePlanTarget(BuildSliderTargetRad());
        }

        private void RefreshAllImmediate()
        {
            RefreshHeader();
            RefreshConnectionPanel();
            RefreshJointPanel();
            RefreshForcePanel();
            RefreshMetricsPanel();
            RefreshRecordPanel();
            RefreshCommandPanel();
            RefreshExperimentPanel();
            RefreshControlVisibility();
            UpdateExecuteButtonVisual();
        }

        private void RefreshHeader()
        {
            if (_bindings == null)
            {
                return;
            }

            SourceSnapshot snapshot = BuildSourceSnapshot();
            Color stateColor = ResolveConnectionColor(snapshot, out string label);
            SetPill(_bindings.HeaderStatusText, $"{snapshot.SourceLabel} {label}", stateColor);
            SetPill(_bindings.HeaderModeText, $"mode: {snapshot.Mode}", CyanColor);
            SetPill(_bindings.HeaderMotionText, $"motion: {snapshot.Motion}", ResolveMotionColor(snapshot.Motion));
            bool showRec = _recorder != null && _recorder.CsvReady && _recorder.PendingCount > 0;
            if (_bindings.RecordBadgeText != null)
            {
                _bindings.RecordBadgeText.gameObject.SetActive(showRec);
                SetPill(_bindings.RecordBadgeText, "● REC", RedColor);
            }
        }

        private void RefreshJointPanel()
        {
            StateSnapshot snapshot = _runtime == null ? null : _runtime.LatestSnapshot;
            if (snapshot == null || !snapshot.HasFrame || snapshot.JointPositionDeg == null)
            {
                SetText(jointText, "Joints\nno frame");
                return;
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("Source ").Append(snapshot.SourceName).Append("   #").Append(snapshot.SequenceId);
            for (int i = 0; i < snapshot.JointPositionDeg.Length; i++)
            {
                builder.Append('\n')
                    .Append("J").Append(i + 1).Append("  ")
                    .Append(snapshot.JointPositionDeg[i].ToString("+000.00;-000.00;000.00", CultureInfo.InvariantCulture))
                    .Append(" deg");
            }

            SetText(jointText, builder.ToString());
        }

        private void RefreshForcePanel()
        {
            StateSnapshot snapshot = _runtime == null ? null : _runtime.LatestSnapshot;
            if (snapshot == null || !snapshot.HasFrame || snapshot.ForceVector == null)
            {
                SetText(forceText, "Force / Torque\nno frame");
                return;
            }

            string[] names = { "Fx", "Fy", "Fz", "Tx", "Ty", "Tz" };
            StringBuilder builder = new StringBuilder("Force[N]        Torque[Nm]");
            for (int row = 0; row < 3; row++)
            {
                float f = row < snapshot.ForceVector.Length ? snapshot.ForceVector[row] : 0f;
                float t = row + 3 < snapshot.ForceVector.Length ? snapshot.ForceVector[row + 3] : 0f;
                builder.Append('\n')
                    .Append(names[row]).Append(" ")
                    .Append(f.ToString("F3", CultureInfo.InvariantCulture).PadLeft(9))
                    .Append("    ")
                    .Append(names[row + 3]).Append(" ")
                    .Append(t.ToString("F3", CultureInfo.InvariantCulture).PadLeft(9));
            }

            SetText(forceText, builder.ToString());
        }

        private void RefreshMetricsPanel()
        {
            if (_runtime == null)
            {
                return;
            }

            MetricsSample sample = _runtime.LatestMetrics;
            SetText(
                metricsText,
                $"Metrics #{sample.SequenceId}\n" +
                $"Rate      {sample.UpdateRateHz:F1} Hz\n" +
                $"Interval  {sample.UpdateIntervalMs:F2} ms\n" +
                $"Jitter    {sample.JitterMs:F2} ms\n" +
                $"Apply     {sample.ReceiveToApplyMs:F2} ms\n" +
                $"Dropped   {sample.DroppedFrameCount}\n" +
                $"OoO       {sample.OutOfOrderCount}");
            if (metricsText != null)
            {
                metricsText.color = sample.JitterMs > 5f || sample.ReceiveToApplyMs > 20f ? ReconnectColor : TextColor;
            }
        }

        private void RefreshRecordPanel()
        {
            if (_recorder == null)
            {
                SetText(recordText, "CSV\nunavailable");
                return;
            }

            SetText(
                recordText,
                $"CSV\n" +
                $"Session {_recorder.SessionId}\n" +
                $"Queue   {_recorder.PendingCount}\n" +
                $"State   {(_recorder.CsvReady ? "ready" : "idle")}\n" +
                $"{_recorder.LastError}");
        }

        private void RefreshExperimentPanel()
        {
            if (experimentText == null)
            {
                return;
            }

            if (_experimentTracker == null || !_experimentTracker.IsExperimentActive)
            {
                SetActive(experimentText, false);
                return;
            }

            SetActive(experimentText, true);
            StringBuilder builder = new StringBuilder();
            builder.Append("EXPERIMENT\n");
            builder.Append("ID   ").Append(_experimentTracker.ExperimentId).Append('\n');
            builder.Append("Pkts ").Append(_experimentTracker.PacketsReceived);
            builder.Append("  Pongs ").Append(_experimentTracker.PongsSent).Append('\n');

            if (_experimentTracker.RttSampleCount > 0)
            {
                builder.Append("RTT  ").Append(_experimentTracker.LastRttMs.ToString("F2", CultureInfo.InvariantCulture));
                builder.Append("ms  mean ").Append(_experimentTracker.MeanRttMs.ToString("F2", CultureInfo.InvariantCulture));
                builder.Append("ms\n");
                builder.Append("p95  ").Append(_experimentTracker.P95RttMs.ToString("F2", CultureInfo.InvariantCulture));
                builder.Append("ms  max ").Append(_experimentTracker.MaxRttMs.ToString("F2", CultureInfo.InvariantCulture));
                builder.Append("ms\n");
            }

            if (_experimentTracker.LastOneWayMs > 0)
            {
                builder.Append("OneWay ").Append(_experimentTracker.LastOneWayMs.ToString("F2", CultureInfo.InvariantCulture)).Append("ms\n");
            }

            if (_experimentTracker.LastJointErrorDeg != null)
            {
                builder.Append("JointErr max ").Append(_experimentTracker.MaxJointErrorDeg.ToString("F3", CultureInfo.InvariantCulture));
                builder.Append("  rms ").Append(_experimentTracker.RmsJointErrorDeg.ToString("F3", CultureInfo.InvariantCulture));
            }

            SetText(experimentText, builder.ToString());
        }

        private void RefreshConnectionPanel()
        {
            if (_runtime == null)
            {
                SetText(connectionText, "Source\nRuntime unavailable");
                SetCardAccent(_bindings == null ? null : _bindings.ConnectionCard, InitColor);
                return;
            }

            SourceSnapshot snapshot = BuildSourceSnapshot();
            Color color = ResolveConnectionColor(snapshot, out _);
            SetCardAccent(_bindings == null ? null : _bindings.ConnectionCard, color);
            SetText(
                connectionText,
                $"Source     {snapshot.SourceLabel}\n" +
                $"Connected  {(snapshot.Connected ? "YES" : "NO")}\n" +
                $"Seq        {snapshot.Seq}\n" +
                $"Rate       {snapshot.RateHz:F1} Hz\n" +
                $"Latency    {snapshot.LatencyMs:F1} ms\n" +
                $"Dropped    {snapshot.Dropped}  OoO {snapshot.OutOfOrder}\n" +
                $"Last recv  {snapshot.ReceiveAgeMs:F1} ms\n" +
                $"{snapshot.Error}");
        }

        private void RefreshCommandPanel()
        {
            if (_command == null)
            {
                SetText(commandText, "Command\nunavailable");
                return;
            }

            RobotCommandResult result = _command.LastResult;
            string twinMode = _runtime != null && _runtime.ModeService != null
                ? _runtime.ModeService.CurrentMode.ToString()
                : _command.CurrentMode.ToString();
            string planningStatus = string.IsNullOrEmpty(_command.LastPlanningStatus)
                ? string.Empty
                : $"\nPlan      {_command.LastPlanningStatus} {_command.LastPlanningError}";
            SetText(
                commandText,
                $"Twin      {twinMode} / {_command.CurrentMode}\n" +
                $"BI        {OnOff(_command.EnableBidirectionalControl)}\n" +
                $"REAL      {OnOff(_command.EnableRealRobotCommand)}\n" +
                $"DRY-RUN   {OnOff(_command.EnableDryRun)}\n" +
                $"Emergency {OnOff(_command.IsEmergencyStopped)}\n" +
                $"LastCmd   {result.Status} {(result.DryRun ? "(DRY)" : string.Empty)} {result.ErrorMessage}" +
                planningStatus);
            SetCardAccent(_bindings == null ? null : _bindings.CommandCard, _command.IsEmergencyStopped ? RedColor : (_command.CurrentMode == TwinMode.Plan || _command.CurrentMode == TwinMode.Execute ? OrangeColor : CyanColor));
        }

        private void RefreshControlVisibility()
        {
            bool inPlan = _command != null && _command.CurrentMode == TwinMode.Plan;
            if (sliderGroup != null)
            {
                sliderGroup.SetActive(inPlan);
            }

            SetInteractable(executeButton, inPlan);
            if (_bindings != null && _bindings.PlanCard != null)
            {
                _bindings.PlanCard.SetTag(inPlan ? "PREVIEW" : string.Empty);
            }
        }

        private void OnIdleClicked()
        {
            HideRealExecuteConfirm();
            if (_command != null && _command.CurrentMode != TwinMode.Sync)
            {
                _command.CancelPlan();
                return;
            }

            _command?.EnterSync();
        }

        private void OnPlanClicked()
        {
            HideRealExecuteConfirm();
            RefreshSlidersFromLatestFrame();
            _command?.EnterPlan();
            RefreshControlVisibility();
        }

        private void OnExecuteClicked()
        {
            if (_command == null || jointSliders == null)
            {
                return;
            }

            if (_command.EnableRealRobotCommand && !_command.EnableDryRun)
            {
                ShowRealExecuteConfirm();
                return;
            }

            ExecuteCurrentTarget();
        }

        private void OnConfirmRealExecute()
        {
            HideRealExecuteConfirm();
            ExecuteCurrentTarget();
        }

        private void ExecuteCurrentTarget()
        {
            _command?.ExecuteTarget(BuildSliderTargetRad(), executeSpeedPercent);
        }

        private void ShowRealExecuteConfirm()
        {
            if (_bindings != null && _bindings.ConfirmPanel != null)
            {
                _bindings.ConfirmPanel.SetActive(true);
            }
        }

        private void HideRealExecuteConfirm()
        {
            if (_bindings != null && _bindings.ConfirmPanel != null)
            {
                _bindings.ConfirmPanel.SetActive(false);
            }
        }

        private void RefreshSlidersFromLatestFrame()
        {
            if (jointSliders == null)
            {
                return;
            }

            _syncingSliders = true;
            try
            {
                float[] deg = null;
                StateSnapshot snapshot = _runtime == null ? null : _runtime.LatestSnapshot;
                if (snapshot != null && snapshot.HasFrame && snapshot.JointPositionDeg != null)
                {
                    deg = new float[Mathf.Min(snapshot.JointPositionDeg.Length, jointSliders.Length)];
                    Array.Copy(snapshot.JointPositionDeg, deg, deg.Length);
                }
                else if (_robotModel != null)
                {
                    deg = _robotModel.GetCurrentJointDegreesCopy();
                }

                for (int i = 0; i < jointSliders.Length; i++)
                {
                    if (jointSliders[i] == null)
                    {
                        continue;
                    }

                    float value = deg != null && i < deg.Length ? deg[i] : 0f;
                    jointSliders[i].SetValueWithoutNotify(Mathf.Clamp(value, jointSliders[i].minValue, jointSliders[i].maxValue));
                    UpdateSliderLabel(i);
                }
            }
            finally
            {
                _syncingSliders = false;
            }
        }

        private void ApplySliderLimits()
        {
            if (jointSliders == null)
            {
                return;
            }

            for (int i = 0; i < jointSliders.Length; i++)
            {
                if (jointSliders[i] == null)
                {
                    continue;
                }

                Vector2 limits = GetJointLimits(i);
                jointSliders[i].minValue = limits.x;
                jointSliders[i].maxValue = limits.y;
            }
        }

        private Vector2 GetJointLimits(int index)
        {
            return _robotModel != null ? _robotModel.GetJointLimitsDeg(index) : new Vector2(-360f, 360f);
        }

        private void OnSliderChanged(int index)
        {
            if (_syncingSliders)
            {
                return;
            }

            UpdateSliderLabel(index);
            _sliderDirty = true;
            _sliderDebounceTimer = 0f;
        }

        private float[] BuildSliderTargetRad()
        {
            if (jointSliders == null)
            {
                return null;
            }

            float[] targetRad = new float[jointSliders.Length];
            for (int i = 0; i < jointSliders.Length; i++)
            {
                targetRad[i] = jointSliders[i] == null ? 0f : jointSliders[i].value * Mathf.Deg2Rad;
            }

            return targetRad;
        }

        private void UpdateSliderLabel(int index)
        {
            if (jointSliderLabels == null || jointSliders == null || index < 0 || index >= jointSliders.Length)
            {
                return;
            }

            if (index < jointSliderLabels.Length)
            {
                SetText(jointSliderLabels[index], $"J{index + 1}: {jointSliders[index].value:F1} deg");
            }
        }

        private void UpdateExecuteButtonVisual()
        {
            if (executeButton == null || _command == null)
            {
                return;
            }

            bool real = _command.EnableRealRobotCommand && !_command.EnableDryRun;
            Image image = executeButton.GetComponent<Image>();
            if (image != null)
            {
                image.color = real ? new Color(RedColor.r, RedColor.g, RedColor.b, 0.82f) : new Color(OrangeColor.r, OrangeColor.g, OrangeColor.b, 0.48f);
            }

            TMP_Text label = executeButton.GetComponentInChildren<TMP_Text>();
            if (label != null)
            {
                label.SetIfChanged(real ? "⚠ EXECUTE (REAL)" : "执行目标");
                label.fontSize = real ? 10 : 12;
            }
        }

        private static bool CardAllowsBody(TwinRuntimeUICard card)
        {
            return card == null || card.BodyRefreshAllowed;
        }

        private static void SetCardState(TwinRuntimeUICard card, bool visible)
        {
            card?.SetState(visible ? CardState.Expanded : CardState.Hidden);
        }

        private static void SetCardAccent(TwinRuntimeUICard card, Color color)
        {
            card?.SetAccentColor(color);
        }

        private SourceSnapshot BuildSourceSnapshot()
        {
            SourceSnapshot snapshot = new SourceSnapshot
            {
                SourceLabel = _runtime == null ? "NONE" : _runtime.ActiveSourceKind.ToString().ToUpperInvariant(),
                Connected = false,
                Seq = -1,
                RateHz = 0f,
                LatencyMs = 0d,
                ReceiveAgeMs = -1d,
                Dropped = 0,
                OutOfOrder = 0,
                Mode = "--",
                Motion = "--",
                Error = string.Empty
            };

            if (_runtime == null)
            {
                snapshot.Error = "Runtime not assigned.";
                return snapshot;
            }

            StateSnapshot state = _runtime.LatestSnapshot;
            IRobotStateSource activeSource = _runtime.ActiveSource;
            RobotSourceStatus status = activeSource == null
                ? new RobotSourceStatus(snapshot.SourceLabel, false, string.Empty, 0, 0)
                : activeSource.GetStatus();

            if (_runtime.ActiveSourceKind == RuntimeSourceKind.Ros2)
            {
                snapshot.SourceLabel = "ROS2";
            }
            else if (_runtime.ActiveSourceKind == RuntimeSourceKind.DartStudio)
            {
                snapshot.SourceLabel = "DART";
            }
            else if (_runtime.ActiveSourceKind == RuntimeSourceKind.Mock)
            {
                snapshot.SourceLabel = "MOCK";
            }

            snapshot.Connected = _runtime.ActiveSourceKind == RuntimeSourceKind.Mock
                ? state != null && state.HasFrame
                : status.IsConnected;
            snapshot.Seq = state != null && state.HasFrame ? state.SequenceId : -1;
            snapshot.RateHz = state == null ? 0f : (float)state.UpdateRateHz;
            snapshot.LatencyMs = state == null ? 0d : state.ReceiveToApplyMs;
            snapshot.ReceiveAgeMs = state != null && state.HasFrame ? state.ReceiveAgeMs : -1d;
            snapshot.Dropped = state == null ? 0 : state.DroppedFrameCount;
            snapshot.OutOfOrder = state == null ? 0 : state.OutOfOrderCount;
            snapshot.Mode = state == null || string.IsNullOrEmpty(state.Mode) ? "--" : state.Mode;
            snapshot.Motion = state == null || string.IsNullOrEmpty(state.MotionState) ? "--" : state.MotionState;
            snapshot.Error = string.IsNullOrEmpty(status.LastError) ? string.Empty : status.LastError;
            if (_runtime.ActiveSourceKind == RuntimeSourceKind.None)
            {
                snapshot.Error = "Active source is not ready.";
            }
            return snapshot;
        }

        private static Color ResolveConnectionColor(SourceSnapshot snapshot, out string label)
        {
            if (snapshot.SourceLabel == "NONE")
            {
                label = "INIT";
                return InitColor;
            }

            double ageMs = snapshot.ReceiveAgeMs;
            if (snapshot.Connected && ageMs >= 0d && ageMs <= 1000d)
            {
                label = "ONLINE";
                return OnlineColor;
            }

            if (snapshot.Seq > 0)
            {
                label = ageMs > 1000d ? "STALE" : "RECONNECT";
                return ageMs > 1000d ? StaleColor : ReconnectColor;
            }

            label = "INIT";
            return InitColor;
        }

        private static Color ResolveMotionColor(string motion)
        {
            if (string.Equals(motion, "MOVING", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(motion, "DONE", System.StringComparison.OrdinalIgnoreCase))
            {
                return OrangeColor;
            }

            if (string.Equals(motion, "HALT", System.StringComparison.OrdinalIgnoreCase) ||
                string.Equals(motion, "ERROR", System.StringComparison.OrdinalIgnoreCase))
            {
                return RedColor;
            }

            return OnlineColor;
        }

        private static void SetPill(TMP_Text target, string value, Color color)
        {
            if (target == null)
            {
                return;
            }

            target.SetIfChanged(value);
            target.color = color;
            Image image = target.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color(color.r, color.g, color.b, 0.22f);
            }
        }

        private static void SetText(TMP_Text target, string value)
        {
            target.SetIfChanged(value ?? string.Empty);
        }

        private static void SetActive(TMP_Text target, bool active)
        {
            if (target != null)
            {
                target.gameObject.SetActive(active);
            }
        }

        private static void SetInteractable(Button target, bool interactable)
        {
            if (target != null)
            {
                target.interactable = interactable;
            }
        }

        private static string OnOff(bool value)
        {
            return value ? "ON" : "OFF";
        }

        private struct SourceSnapshot
        {
            public string SourceLabel;
            public bool Connected;
            public long Seq;
            public float RateHz;
            public double LatencyMs;
            public double ReceiveAgeMs;
            public long Dropped;
            public long OutOfOrder;
            public string Mode;
            public string Motion;
            public string Error;
        }

        private void OnDestroy()
        {
            if (_autoCanvas != null)
            {
                Destroy(_autoCanvas);
            }
        }
    }
}
