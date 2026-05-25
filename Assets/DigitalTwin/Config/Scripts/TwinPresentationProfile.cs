using UnityEngine;

namespace DigitalTwin
{
    [CreateAssetMenu(menuName = "Digital Twin/Profiles/Presentation Profile", fileName = "TwinPresentationProfile")]
    public sealed class TwinPresentationProfile : ScriptableObject
    {
        [Header("运行时界面 / Runtime UI")]
        [Tooltip("是否启用游戏运行时 UI。当前主实验默认关闭，减少刷新和布局开销。")]
        [InspectorName("启用 Runtime UI")]
        public bool enableRuntimeUI;
        [Tooltip("运行时 UI 刷新频率，单位 Hz。只在 enableRuntimeUI 开启时生效。")]
        [InspectorName("UI 刷新频率 Hz")]
        [Min(0.1f)] public float uiRefreshRateHz = 10f;
        [Tooltip("显示关节角面板。")]
        [InspectorName("显示关节面板")]
        public bool showJointPanel = true;
        [Tooltip("显示六轴力/力矩面板。")]
        [InspectorName("显示力传感器面板")]
        public bool showForcePanel = true;
        [Tooltip("显示频率、延迟、丢帧等指标面板。")]
        [InspectorName("显示指标面板")]
        public bool showMetricsPanel = true;
        [Tooltip("显示记录状态面板。")]
        [InspectorName("显示记录面板")]
        public bool showRecordPanel = true;
        [Tooltip("显示 DartStudio 连接状态面板。")]
        [InspectorName("显示连接面板")]
        public bool showConnectionPanel = true;
        [Tooltip("显示实验状态面板。")]
        [InspectorName("显示实验面板")]
        public bool showExperimentPanel = true;

        [Header("调试工具 / Debug")]
        [Tooltip("开启详细日志。高频通讯时建议关闭。")]
        [InspectorName("启用详细日志")]
        public bool enableVerboseLog;
        [Tooltip("启用 Editor 关节调试入口。当前主实验默认关闭。")]
        [InspectorName("启用 Editor 关节调试")]
        public bool enableEditorJointControl;
        [Tooltip("启用 Editor IK 调试入口。当前主实验默认关闭。")]
        [InspectorName("启用 Editor IK 调试")]
        public bool enableEditorIkControl;
        [Tooltip("显示运行时调试 Overlay。当前主实验默认关闭。")]
        [InspectorName("启用 Debug Overlay")]
        public bool enableDebugOverlay;

        private void OnValidate()
        {
            uiRefreshRateHz = Mathf.Max(0.1f, uiRefreshRateHz);
        }

        public void ApplyTo(TwinRuntimeSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.EnableRuntimeUi = enableRuntimeUI;
            settings.UiRefreshRateHz = Mathf.Max(0.1f, uiRefreshRateHz);
            settings.ShowJointPanel = showJointPanel;
            settings.ShowForcePanel = showForcePanel;
            settings.ShowMetricsPanel = showMetricsPanel;
            settings.ShowRecordPanel = showRecordPanel;
            settings.ShowConnectionPanel = showConnectionPanel;
            settings.ShowExperimentPanel = showExperimentPanel;
            settings.EnableVerboseLog = enableVerboseLog;
            settings.EnableEditorJointControl = enableEditorJointControl;
            settings.EnableEditorIkControl = enableEditorIkControl;
            settings.EnableDebugOverlay = enableDebugOverlay;
        }
    }
}
