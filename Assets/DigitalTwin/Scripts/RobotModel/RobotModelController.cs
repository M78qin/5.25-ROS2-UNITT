using System;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DigitalTwin
{
    /// <summary>
    /// 机械臂模型唯一姿态中心：负责 URDF/ArticulationBody 绑定、Zero 基准、绝对关节角应用、TCP 读取。
    /// 不处理 IK 求解、不处理 Socket/DB/UI、不发送真实机器人命令。
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    public sealed class RobotModelController : MonoBehaviour
    {
        public enum StartupPoseMode
        {
            [Tooltip("保持进入 Play 前编辑器里的当前显示姿态；不会把当前姿态当作新的 Zero。")]
            KeepCurrentEditorPose = 0,
            [Tooltip("进入 Play 时应用机械 Zero 姿态，即全部关节绝对角为 0。")]
            ApplyZeroPose = 1,
            [Tooltip("进入 Play 时应用 Home0 姿态。推荐用于让虚拟机械臂先对齐真实机械臂当前姿态。")]
            ApplyHome0Pose = 2
        }

        public enum ControlAuthority
        {
            [Tooltip("实时反馈控制。DigitalTwinRuntime / DartStudio / ROS2 可以更新模型。")]
            LiveFeedback = 0,
            [Tooltip("Play 模式检查器手动关节控制。开启后 RuntimeLive 不会覆盖模型。")]
            RuntimeManualJoint = 1,
            [Tooltip("Play 模式 IK 预览控制。开启后 RuntimeLive 不会覆盖模型。")]
            RuntimeIkPreview = 2,
            [Tooltip("后续真实命令前的预览控制。")]
            CommandPreview = 3,
            [Tooltip("禁用所有运行时外部姿态写入。")]
            Disabled = 4
        }

        public enum PoseSource
        {
            Unknown = 0,
            [Tooltip("运行时实时反馈源，等价于 RuntimeLive。")]
            Runtime = 1,
            [Tooltip("运行时实时反馈源。")]
            RuntimeLive = 1,
            [Tooltip("Play 模式检查器手动关节控制。")]
            RuntimeManual = 2,
            [Tooltip("非 Play 编辑态关节滑条，等价于 Slider。")]
            EditorSlider = 3,
            [Tooltip("非 Play 编辑态关节滑条。")]
            Slider = 3,
            Home = 4,
            Target = 5,
            IkPreview = 6,
            CommandPreview = 7,
            Startup = 8,
            Evaluation = 9
        }

        [Serializable]
        public sealed class JointBinding
        {
            [Tooltip("关节显示名。建议使用 joint_1 ~ joint_N；自动绑定时也会尝试 link_1 ~ link_N。")]
            public string name = "joint_1";

            [Tooltip("Unity URDF Importer 生成的 ArticulationBody。H2515 通常绑定 link_1 ~ link_6。")]
            public ArticulationBody body;

            [Tooltip("方向修正。1 表示外部机器人角度与 Unity 显示方向一致；-1 表示方向相反。不要在通信解析器里改方向。")]
            public float sign = 1f;

            [Tooltip("零点偏移，单位 degree。UnityDeg = DisplayDeg * Sign + OffsetDeg。真实 0 度和 Unity 导入 0 度不一致时才改。")]
            public float offsetDeg;

            [Tooltip("关节最小显示角，单位 degree。这里的角度是相对于 Zero 的绝对角，支持小数。")]
            public float minDeg = -360f;

            [Tooltip("关节最大显示角，单位 degree。这里的角度是相对于 Zero 的绝对角，支持小数。")]
            public float maxDeg = 360f;

            [Tooltip("是否已经捕获该关节的 Zero localRotation。Zero 是机械 0 点基准，不会被 Home、Target、IK 或 Runtime 数据自动修改。")]
            public bool hasZeroLocalRotation;

            [Tooltip("该关节 Zero 姿态下的本地旋转。通常由 URDF 初始向上姿态 Capture 得到。")]
            public Quaternion zeroLocalRotation = Quaternion.identity;
        }

        [Header("Joint Bindings / 关节绑定")]
        [SerializeField, Tooltip("Optional signal schema used for offline calibration offsets.")]
        private RobotSignalSchema signalSchema;

        [SerializeField, Tooltip("关节搜索根节点。通常填机器人 base_link 或机械臂根节点；Auto Bind 会在它的子级里找 ArticulationBody。")]
        private Transform jointRoot;

        [SerializeField, Tooltip("关节绑定表。H2515 默认 6 轴，但系统不把关节数量写死，可按数组长度扩展。")]
        private JointBinding[] joints =
        {
            new JointBinding { name = "joint_1", minDeg = -360f, maxDeg = 360f },
            new JointBinding { name = "joint_2", minDeg = -125f, maxDeg = 125f },
            new JointBinding { name = "joint_3", minDeg = -160f, maxDeg = 160f },
            new JointBinding { name = "joint_4", minDeg = -360f, maxDeg = 360f },
            new JointBinding { name = "joint_5", minDeg = -360f, maxDeg = 360f },
            new JointBinding { name = "joint_6", minDeg = -360f, maxDeg = 360f }
        };

        [Header("Zero Calibration / 零点校准")]
        [SerializeField, Tooltip("Auto Bind 成功后，只对尚未捕获 Zero 的关节自动记录当前导入姿态。不会覆盖已经捕获过的 Zero。")]
        private bool captureMissingZeroOnBind = true;

        [Header("Runtime Startup Pose / 运行启动姿态")]
        [SerializeField, Tooltip("进入 Play 时是否应用启动姿态。注意：这不会改变 Zero，只是设置当前显示姿态。")]
        private bool applyStartupPoseOnPlay = true;

        [SerializeField, Tooltip("进入 Play 时使用什么启动姿态。推荐 ApplyHome0Pose：用 Home0 对齐真实机械臂当前姿态，然后等待真实数据。")]
        private StartupPoseMode startupPoseMode = StartupPoseMode.ApplyHome0Pose;

        [SerializeField, Tooltip("Runtime 初始 Home0 姿态，单位 degree，小数有效。它是相对于 Zero 的绝对角，不是新的 Zero。")]
        private float[] runtimeHome0Deg = new float[6];

        [SerializeField, Tooltip("应用启动姿态时清理 ArticulationBody 的速度，避免历史物理速度影响进入 Play 后的第一帧。")]
        private bool clearVelocityOnStartup = true;

        [Header("Control Authority / 控制权")]
        [SerializeField, Tooltip("当前 Play 模式模型控制权。LiveFeedback 表示实时反馈控制；RuntimeManualJoint 表示检查器手动控制；RuntimeIkPreview 表示 IK 预览控制。")]
        private ControlAuthority controlAuthority = ControlAuthority.LiveFeedback;

        [SerializeField, Tooltip("进入 Play 时强制把控制权重置为 LiveFeedback，然后再应用 Home0/Zero 启动姿态。")]
        private bool resetAuthorityToLiveOnPlayStart = true;

        [Header("Drive / 驱动参数")]
        [SerializeField, Tooltip("是否把下面的刚度、阻尼、力限制写入 xDrive。关闭后只改 target，不覆盖 URDF Importer 的 drive 参数。")]
        private bool applyDriveSettings = true;

        [SerializeField, Tooltip("xDrive stiffness，值越大越接近目标角。只在 Apply Drive Settings 开启时写入。")]
        private float stiffness = 10000f;

        [SerializeField, Tooltip("xDrive damping，抑制关节振荡。只在 Apply Drive Settings 开启时写入。")]
        private float damping = 600f;

        [SerializeField, Tooltip("xDrive forceLimit，驱动力限制。只在 Apply Drive Settings 开启时写入。")]
        private float forceLimit = 1000f;

        [SerializeField, Tooltip("是否把 minDeg/maxDeg 也写入 xDrive.lowerLimit/upperLimit。若 URDF 限位已正确，可关闭。")]
        private bool applyLimitsToDrive = false;

        [SerializeField, Tooltip("非 Play 模式下，为了让 Inspector 滑条立即可见，按 Unity URDF Importer 的 Articulation 轴刷新 Transform 预览。运行时不使用。")]
        private bool enableEditModeTransformPreview = true;

        [Header("Live View / 运行时显示")]
        [SerializeField, Tooltip("是否允许运行时真实/仿真数据驱动 Live Robot。关闭后 ApplyStateFrame 不会更新模型。")]
        private bool enableLiveView = true;

        [Header("Ghost View / 影子模型")]
        [SerializeField, Tooltip("是否启用 Ghost 机器人显示。当前只保留轻量材质/Renderer 开关，轨迹规划后续扩展。")]
        private bool enableGhostView;

        [SerializeField, Tooltip("Ghost 模型的 Renderer 列表。")]
        private Renderer[] ghostRenderers;

        [SerializeField, Tooltip("Ghost 模型材质。")]
        private Material ghostMaterial;

        private bool[] _missingJointWarned;
        private float[] _currentDisplayDegCache;
        private float[] _lastTargetDisplayDegCache;
        private float[] _runtimeDisplayDegBuffer;
        private bool _startupPoseAppliedThisPlay;
        private bool _suppressPoseChanged;
        private PoseSource _lastCommandSource = PoseSource.Unknown;

        public event Action<PoseSource, float[]> OnPoseChanged;

        public int JointCount => joints == null ? 0 : joints.Length;
        public JointBinding[] Joints => joints;
        public Transform JointRoot => jointRoot != null ? jointRoot : transform;
        public StartupPoseMode CurrentStartupPoseMode => startupPoseMode;
        public bool ApplyStartupPoseOnPlay => applyStartupPoseOnPlay;
        public float[] RuntimeHome0Deg => runtimeHome0Deg;
        public ControlAuthority CurrentControlAuthority => controlAuthority;
        public PoseSource LastCommandSource => _lastCommandSource;

        public void SetSignalSchema(RobotSignalSchema schema)
        {
            signalSchema = schema;
        }
        public bool IsRuntimeManualControlActive => Application.isPlaying && controlAuthority == ControlAuthority.RuntimeManualJoint;
        public bool IsRuntimeIkControlActive => Application.isPlaying && controlAuthority == ControlAuthority.RuntimeIkPreview;

        private void Reset()
        {
            jointRoot = transform;
            EnsureJointArray();
            EnsureRuntimeArrays();
            CaptureMissingZeroFromCurrent();
        }

        private void OnValidate()
        {
            EnsureJointArray();
            EnsureRuntimeArrays();

            stiffness = Mathf.Max(0f, stiffness);
            damping = Mathf.Max(0f, damping);
            forceLimit = Mathf.Max(0f, forceLimit);

            for (int i = 0; i < joints.Length; i++)
            {
                NormalizeJoint(joints[i], i);
            }
        }

        private void Awake()
        {
            EnsureJointArray();
            EnsureRuntimeArrays();
            if (Application.isPlaying)
            {
                PrepareRuntimeStartupPose();
            }
        }

        private void OnEnable()
        {
            EnsureJointArray();
            EnsureRuntimeArrays();
            if (Application.isPlaying)
            {
                PrepareRuntimeStartupPose();
            }
            else
            {
                _startupPoseAppliedThisPlay = false;
            }
        }

        private void OnDisable()
        {
            if (!Application.isPlaying)
            {
                _startupPoseAppliedThisPlay = false;
            }
        }

        public void PrepareRuntimeStartupPose()
        {
            if (!Application.isPlaying || _startupPoseAppliedThisPlay || !applyStartupPoseOnPlay)
            {
                return;
            }

            EnsureJointArray();
            EnsureRuntimeArrays();

            if (resetAuthorityToLiveOnPlayStart)
            {
                controlAuthority = ControlAuthority.LiveFeedback;
            }

            switch (startupPoseMode)
            {
                case StartupPoseMode.ApplyZeroPose:
                    ApplyZeroPose(false, PoseSource.Startup, true);
                    break;
                case StartupPoseMode.ApplyHome0Pose:
                    ApplyHome0Pose(false, PoseSource.Startup, true);
                    break;
                case StartupPoseMode.KeepCurrentEditorPose:
                    UpdateCurrentDisplayCacheFromModel();
                    CopyCurrentToTargetCache();
                    RaisePoseChanged(PoseSource.Startup);
                    break;
            }

            if (clearVelocityOnStartup)
            {
                ClearArticulationVelocities();
            }

            _startupPoseAppliedThisPlay = true;
        }

        public void SetStartupPoseMode(StartupPoseMode mode) => startupPoseMode = mode;

        public void SetControlAuthority(ControlAuthority authority)
        {
            controlAuthority = authority;
        }

        public void ReleaseToLiveFeedback()
        {
            controlAuthority = ControlAuthority.LiveFeedback;
        }

        public bool CanAcceptPoseSource(PoseSource source)
        {
            if (source == PoseSource.Evaluation || source == PoseSource.Startup)
            {
                return true;
            }

            if (!Application.isPlaying)
            {
                return source != PoseSource.Runtime && source != PoseSource.RuntimeLive && source != PoseSource.RuntimeManual;
            }

            if (controlAuthority == ControlAuthority.Disabled)
            {
                return false;
            }

            if (source == PoseSource.Runtime || source == PoseSource.RuntimeLive)
            {
                return enableLiveView && controlAuthority == ControlAuthority.LiveFeedback;
            }

            if (source == PoseSource.RuntimeManual || source == PoseSource.Slider || source == PoseSource.EditorSlider || source == PoseSource.Home || source == PoseSource.Target)
            {
                return controlAuthority == ControlAuthority.RuntimeManualJoint || controlAuthority == ControlAuthority.CommandPreview;
            }

            if (source == PoseSource.IkPreview)
            {
                return controlAuthority == ControlAuthority.RuntimeIkPreview || controlAuthority == ControlAuthority.CommandPreview;
            }

            if (source == PoseSource.CommandPreview)
            {
                return controlAuthority == ControlAuthority.CommandPreview;
            }

            return controlAuthority != ControlAuthority.LiveFeedback;
        }

        public void SetRuntimeHome0Degrees(float[] degrees)
        {
            EnsureJointArray();
            EnsureRuntimeArrays();
            if (degrees == null) return;

            for (int i = 0; i < runtimeHome0Deg.Length; i++)
            {
                runtimeHome0Deg[i] = i < degrees.Length ? ClampDisplayDeg(i, degrees[i]) : 0f;
            }
        }

        public float[] GetRuntimeHome0DegreesCopy()
        {
            EnsureRuntimeArrays();
            float[] copy = new float[runtimeHome0Deg.Length];
            Array.Copy(runtimeHome0Deg, copy, runtimeHome0Deg.Length);
            return copy;
        }

        public bool AutoBindJoints()
        {
            EnsureJointArray();
            Transform root = JointRoot;
            if (root == null) return false;

            List<ArticulationBody> orderedMovableBodies = CollectOrderedMovableBodies(root);
            HashSet<ArticulationBody> used = new HashSet<ArticulationBody>();

            for (int i = 0; i < joints.Length; i++)
            {
                ArticulationBody body = FindJointByCandidates(root, i, orderedMovableBodies, used);
                joints[i].body = body;
                if (body != null) used.Add(body);
            }

            _missingJointWarned = new bool[joints.Length];
            EnsureRuntimeArrays();

            if (captureMissingZeroOnBind)
            {
                CaptureMissingZeroFromCurrent();
            }

            UpdateCurrentDisplayCacheFromModel();
            return GetMissingJointCount() == 0;
        }

        public string GetBindingReport()
        {
            EnsureJointArray();
            int missing = GetMissingJointCount();
            return missing == 0
                ? $"绑定完整：{joints.Length}/{joints.Length} 个关节已绑定。"
                : $"绑定不完整：缺少 {missing}/{joints.Length} 个 ArticulationBody。请检查 Joint Root 或点击 Auto Bind。";
        }

        public int GetMissingJointCount()
        {
            EnsureJointArray();
            int missing = 0;
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null || joints[i].body == null) missing++;
            }
            return missing;
        }

        public int GetCapturedZeroCount()
        {
            EnsureJointArray();
            int count = 0;
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] != null && joints[i].hasZeroLocalRotation) count++;
            }
            return count;
        }

        public void CaptureCurrentPoseAsZero()
        {
            EnsureJointArray();
            for (int i = 0; i < joints.Length; i++)
            {
                JointBinding joint = joints[i];
                if (joint == null || joint.body == null) continue;
                joint.zeroLocalRotation = joint.body.transform.localRotation;
                joint.hasZeroLocalRotation = true;
            }
        }

        public void CaptureMissingZeroFromCurrent()
        {
            EnsureJointArray();
            for (int i = 0; i < joints.Length; i++)
            {
                JointBinding joint = joints[i];
                if (joint == null || joint.body == null || joint.hasZeroLocalRotation) continue;
                joint.zeroLocalRotation = joint.body.transform.localRotation;
                joint.hasZeroLocalRotation = true;
            }
        }

        public void ClearCapturedZero()
        {
            EnsureJointArray();
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null) continue;
                joints[i].hasZeroLocalRotation = false;
                joints[i].zeroLocalRotation = Quaternion.identity;
            }
        }

        public bool ApplyStateFrame(RobotStateFrame frame)
        {
            if (!enableLiveView || frame == null || frame.JointPositionRad == null) return false;
            bool applied = ApplyJointRadians(frame.JointPositionRad, PoseSource.RuntimeLive);
            if (applied) frame.MarkAppliedNow();
            return applied;
        }

        public bool ApplyJointRadians(float[] jointRadians)
        {
            return ApplyJointRadians(jointRadians, PoseSource.Runtime);
        }

        public bool ApplyJointRadians(float[] jointRadians, PoseSource source)
        {
            if (jointRadians == null) return false;
            EnsureRuntimeArrays();
            int count = Mathf.Min(jointRadians.Length, JointCount);
            if (count <= 0) return false;

            EnsureFloatArray(ref _runtimeDisplayDegBuffer, JointCount);
            float[] displayDeg = _runtimeDisplayDegBuffer;
            for (int i = 0; i < count; i++)
            {
                float compensatedRad = jointRadians[i] + (signalSchema == null ? 0f : signalSchema.GetCalibrationOffsetRad(i));
                displayDeg[i] = ConvertRobotRadToDisplayDeg(i, compensatedRad);
            }
            return ApplyJointDisplayDegrees(displayDeg, false, source, true, false);
        }

        public bool ApplyJointDisplayDegrees(float[] displayDeg, bool editModePreview)
        {
            return ApplyJointDisplayDegrees(displayDeg, editModePreview, editModePreview ? PoseSource.Slider : PoseSource.Unknown, true, editModePreview);
        }

        public bool ApplyJointDegrees(float[] displayDeg, PoseSource source, bool editModePreview)
        {
            return ApplyJointDisplayDegrees(displayDeg, editModePreview, source, true, editModePreview);
        }

        public bool ApplyJointDisplayDegrees(float[] displayDeg, bool editModePreview, PoseSource source, bool notify)
        {
            return ApplyJointDisplayDegrees(displayDeg, editModePreview, source, notify, editModePreview);
        }

        public bool ApplyJointDisplayDegrees(float[] displayDeg, bool editModePreview, PoseSource source, bool notify, bool immediateState)
        {
            if (displayDeg == null) return false;
            EnsureJointArray();
            EnsureRuntimeArrays();

            if (!CanAcceptPoseSource(source))
            {
                return false;
            }

            bool applied = false;
            int count = Mathf.Min(displayDeg.Length, joints.Length);
            for (int i = 0; i < count; i++)
            {
                JointBinding binding = joints[i];
                if (binding == null || binding.body == null)
                {
                    WarnMissingJointOnce(i);
                    continue;
                }

                float clampedDisplayDeg = ClampDisplayDeg(i, displayDeg[i]);
                float unityDeg = ConvertDisplayDegToUnityDeg(i, clampedDisplayDeg);
                ApplyUnityDegToBody(binding, unityDeg, editModePreview, immediateState);
                _currentDisplayDegCache[i] = clampedDisplayDeg;
                _lastTargetDisplayDegCache[i] = clampedDisplayDeg;
                applied = true;
            }

#if UNITY_EDITOR
            if (editModePreview && !Application.isPlaying)
            {
                SceneView.RepaintAll();
                EditorApplication.QueuePlayerLoopUpdate();
            }
#endif
            if (applied)
            {
                _lastCommandSource = source;
            }
            if (applied && notify && !_suppressPoseChanged)
            {
                RaisePoseChanged(source);
            }
            return applied;
        }

        public bool ApplyJointDisplayDegreesForEditMode(float[] displayDeg)
        {
            return ApplyJointDisplayDegrees(displayDeg, true, PoseSource.EditorSlider, true, true);
        }

        public void ApplyZeroPose(bool editModePreview = true)
        {
            ApplyZeroPose(editModePreview, PoseSource.Home, editModePreview || Application.isPlaying);
        }

        public void ApplyZeroPose(bool editModePreview, PoseSource source)
        {
            ApplyZeroPose(editModePreview, source, editModePreview || source == PoseSource.Startup);
        }

        public void ApplyZeroPose(bool editModePreview, PoseSource source, bool immediateState)
        {
            EnsureRuntimeArrays();
            float[] zeros = new float[joints.Length];
            ApplyJointDisplayDegrees(zeros, editModePreview, source, true, immediateState);
        }

        public void ApplyHome0Pose(bool editModePreview = true)
        {
            ApplyHome0Pose(editModePreview, PoseSource.Home, editModePreview || Application.isPlaying);
        }

        public void ApplyHome0Pose(bool editModePreview, PoseSource source)
        {
            ApplyHome0Pose(editModePreview, source, editModePreview || source == PoseSource.Startup);
        }

        public void ApplyHome0Pose(bool editModePreview, PoseSource source, bool immediateState)
        {
            EnsureRuntimeArrays();
            ApplyJointDisplayDegrees(runtimeHome0Deg, editModePreview, source, true, immediateState);
        }

        public float[] ReadCurrentDisplayDegrees()
        {
            EnsureRuntimeArrays();
            UpdateCurrentDisplayCacheFromModel();
            float[] copy = new float[_currentDisplayDegCache.Length];
            Array.Copy(_currentDisplayDegCache, copy, _currentDisplayDegCache.Length);
            return copy;
        }

        public float[] GetCurrentJointDegreesCopy() => ReadCurrentDisplayDegrees();

        public float[] GetTargetJointDegreesCopy()
        {
            EnsureRuntimeArrays();
            float[] copy = new float[_lastTargetDisplayDegCache.Length];
            Array.Copy(_lastTargetDisplayDegCache, copy, copy.Length);
            return copy;
        }

        public bool TryGetCurrentJointDegrees(float[] buffer)
        {
            if (buffer == null) return false;
            float[] current = ReadCurrentDisplayDegrees();
            Array.Copy(current, buffer, Mathf.Min(current.Length, buffer.Length));
            return true;
        }

        public bool TryGetTargetJointDegrees(float[] buffer)
        {
            if (buffer == null) return false;
            EnsureRuntimeArrays();
            Array.Copy(_lastTargetDisplayDegCache, buffer, Mathf.Min(_lastTargetDisplayDegCache.Length, buffer.Length));
            return true;
        }

        public float[] ReadCurrentJointRadians()
        {
            float[] deg = ReadCurrentDisplayDegrees();
            float[] rad = new float[deg.Length];
            for (int i = 0; i < deg.Length; i++) rad[i] = ConvertDisplayDegToRobotRad(i, deg[i]);
            return rad;
        }

        public float GetCurrentDisplayDeg(int index)
        {
            EnsureRuntimeArrays();
            return index >= 0 && index < _currentDisplayDegCache.Length ? _currentDisplayDegCache[index] : 0f;
        }

        public Vector2 GetJointLimitsDeg(int index)
        {
            JointBinding binding = ResolveJoint(index);
            return binding != null && binding.maxDeg > binding.minDeg
                ? new Vector2(binding.minDeg, binding.maxDeg)
                : new Vector2(-360f, 360f);
        }

        public float ClampDisplayDeg(int index, float displayDeg)
        {
            JointBinding binding = ResolveJoint(index);
            if (binding == null || binding.maxDeg <= binding.minDeg) return displayDeg;
            return Mathf.Clamp(displayDeg, binding.minDeg, binding.maxDeg);
        }

        public float ConvertRobotRadToDisplayDeg(int index, float robotRad) => ClampDisplayDeg(index, robotRad * Mathf.Rad2Deg);
        public float ConvertDisplayDegToRobotRad(int index, float displayDeg) => ClampDisplayDeg(index, displayDeg) * Mathf.Deg2Rad;

        public float ConvertDisplayDegToUnityDeg(int index, float displayDeg)
        {
            JointBinding binding = ResolveJoint(index);
            float clamped = ClampDisplayDeg(index, displayDeg);
            return binding == null ? clamped : clamped * SafeSign(binding.sign) + binding.offsetDeg;
        }

        public float ConvertUnityDegToDisplayDeg(int index, float unityDeg)
        {
            JointBinding binding = ResolveJoint(index);
            if (binding == null) return unityDeg;
            return ClampDisplayDeg(index, (unityDeg - binding.offsetDeg) / SafeSign(binding.sign));
        }

        public float ConvertRobotRadToUnityDeg(int index, float robotRad) => ConvertDisplayDegToUnityDeg(index, ConvertRobotRadToDisplayDeg(index, robotRad));
        public float ConvertUnityDegToRobotRad(int index, float unityDeg) => ConvertDisplayDegToRobotRad(index, ConvertUnityDegToDisplayDeg(index, unityDeg));

        public Transform GetDefaultTcpTransform()
        {
            EnsureJointArray();
            for (int i = joints.Length - 1; i >= 0; i--)
            {
                if (joints[i] != null && joints[i].body != null) return joints[i].body.transform;
            }
            Transform root = JointRoot;
            if (root != null)
            {
                Transform link6 = FindChildRecursive(root, "link_6");
                if (link6 != null) return link6;
            }
            return root != null ? root : transform;
        }

        public bool TryGetTcpPose(Transform tcpReference, Vector3 localOffsetPosition, Vector3 localOffsetEulerDeg, out Vector3 position, out Quaternion rotation)
        {
            Transform tcp = tcpReference != null ? tcpReference : GetDefaultTcpTransform();
            if (tcp == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }

            Quaternion offsetRot = Quaternion.Euler(localOffsetEulerDeg);
            position = tcp.TransformPoint(localOffsetPosition);
            rotation = tcp.rotation * offsetRot;
            return true;
        }

        public bool EvaluateTcpPoseDegrees(float[] displayDeg, Transform tcpReference, Vector3 localOffsetPosition, Vector3 localOffsetEulerDeg, out Vector3 position, out Quaternion rotation)
        {
            // 优先使用纯运动学评估，不改动场景模型。这样运行时 IK 不依赖物理步，也不会干扰 DartStudio 同步。
            if (TryEvaluateTcpPoseKinematic(displayDeg, tcpReference, localOffsetPosition, localOffsetEulerDeg, out position, out rotation))
            {
                return true;
            }

            // 兜底：如果 TCP 不在 JointRoot 子树中，则临时应用到模型再恢复。
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (displayDeg == null) return false;

            float[] restore = ReadCurrentDisplayDegrees();
            bool oldSuppress = _suppressPoseChanged;
            _suppressPoseChanged = true;
            bool ok = false;
            try
            {
                ApplyJointDisplayDegrees(displayDeg, !Application.isPlaying, PoseSource.Evaluation, false);
                ok = TryGetTcpPose(tcpReference, localOffsetPosition, localOffsetEulerDeg, out position, out rotation);
            }
            finally
            {
                ApplyJointDisplayDegrees(restore, !Application.isPlaying, PoseSource.Evaluation, false);
                _suppressPoseChanged = oldSuppress;
            }
            return ok;
        }

        private bool TryEvaluateTcpPoseKinematic(float[] displayDeg, Transform tcpReference, Vector3 localOffsetPosition, Vector3 localOffsetEulerDeg, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (displayDeg == null) return false;

            Transform tcp = tcpReference != null ? tcpReference : GetDefaultTcpTransform();
            Transform root = JointRoot;
            if (tcp == null || root == null) return false;

            List<Transform> path = new List<Transform>();
            Transform cursor = tcp;
            while (cursor != null && cursor != root)
            {
                path.Add(cursor);
                cursor = cursor.parent;
            }
            if (cursor != root) return false;

            path.Reverse();
            Matrix4x4 matrix = Matrix4x4.TRS(root.position, root.rotation, Vector3.one);
            for (int i = 0; i < path.Count; i++)
            {
                Transform t = path[i];
                Quaternion localRot = t.localRotation;
                int jointIndex = FindJointIndexByTransform(t);
                if (jointIndex >= 0)
                {
                    JointBinding binding = joints[jointIndex];
                    float deg = jointIndex < displayDeg.Length ? ClampDisplayDeg(jointIndex, displayDeg[jointIndex]) : 0f;
                    float unityDeg = ConvertDisplayDegToUnityDeg(jointIndex, deg);
                    Quaternion zero = binding.hasZeroLocalRotation ? binding.zeroLocalRotation : binding.body.transform.localRotation;
                    Vector3 axis = binding.body.anchorRotation * Vector3.right;
                    if (axis.sqrMagnitude < 1e-8f) axis = Vector3.right;
                    axis.Normalize();
                    localRot = zero * Quaternion.AngleAxis(unityDeg, axis);
                }
                matrix = matrix * Matrix4x4.TRS(t.localPosition, localRot, Vector3.one);
            }

            Quaternion offsetRot = Quaternion.Euler(localOffsetEulerDeg);
            position = matrix.MultiplyPoint3x4(localOffsetPosition);
            rotation = matrix.rotation * offsetRot;
            return true;
        }

        private int FindJointIndexByTransform(Transform transformToFind)
        {
            if (transformToFind == null || joints == null) return -1;
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] != null && joints[i].body != null && joints[i].body.transform == transformToFind) return i;
            }
            return -1;
        }

        public void SetGhostVisible(bool visible)
        {
            if (!enableGhostView) visible = false;
            if (ghostMaterial != null && ghostRenderers != null)
            {
                for (int i = 0; i < ghostRenderers.Length; i++)
                {
                    if (ghostRenderers[i] != null) ghostRenderers[i].sharedMaterial = ghostMaterial;
                }
            }
            if (ghostRenderers == null) return;
            for (int i = 0; i < ghostRenderers.Length; i++)
            {
                if (ghostRenderers[i] != null) ghostRenderers[i].enabled = visible;
            }
        }

        private void ApplyUnityDegToBody(JointBinding binding, float unityDeg, bool editModePreview, bool immediateState)
        {
            ArticulationBody body = binding.body;
            ArticulationDrive drive = body.xDrive;
            if (applyDriveSettings)
            {
                drive.stiffness = stiffness;
                drive.damping = damping;
                drive.forceLimit = forceLimit;
            }
            if (applyLimitsToDrive)
            {
                int index = Array.IndexOf(joints, binding);
                drive.lowerLimit = ConvertDisplayDegToUnityDeg(index, binding.minDeg);
                drive.upperLimit = ConvertDisplayDegToUnityDeg(index, binding.maxDeg);
            }

            drive.target = unityDeg;
            body.xDrive = drive;

            if (immediateState && body.dofCount > 0)
            {
                try { body.jointPosition = new ArticulationReducedSpace(unityDeg * Mathf.Deg2Rad); }
                catch { }
            }

#if UNITY_EDITOR
            if (editModePreview && !Application.isPlaying && enableEditModeTransformPreview)
            {
                ApplyEditorTransformPreview(binding, unityDeg);
            }
#endif
        }

#if UNITY_EDITOR
        private void ApplyEditorTransformPreview(JointBinding binding, float unityDeg)
        {
            if (binding == null || binding.body == null) return;
            if (!binding.hasZeroLocalRotation)
            {
                binding.zeroLocalRotation = binding.body.transform.localRotation;
                binding.hasZeroLocalRotation = true;
            }
            Vector3 axis = binding.body.anchorRotation * Vector3.right;
            if (axis.sqrMagnitude < 1e-8f) axis = Vector3.right;
            axis.Normalize();
            binding.body.transform.localRotation = binding.zeroLocalRotation * Quaternion.AngleAxis(unityDeg, axis);
            EditorUtility.SetDirty(binding.body.transform);
            EditorUtility.SetDirty(binding.body);
        }
#endif

        private void UpdateCurrentDisplayCacheFromModel()
        {
            EnsureRuntimeArrays();
            for (int i = 0; i < joints.Length; i++)
            {
                JointBinding binding = joints[i];
                if (binding == null || binding.body == null)
                {
                    _currentDisplayDegCache[i] = i < _lastTargetDisplayDegCache.Length ? _lastTargetDisplayDegCache[i] : 0f;
                    continue;
                }

                float unityDeg = _lastTargetDisplayDegCache != null && i < _lastTargetDisplayDegCache.Length
                    ? ConvertDisplayDegToUnityDeg(i, _lastTargetDisplayDegCache[i])
                    : binding.body.xDrive.target;

                if (binding.body.jointPosition.dofCount > 0)
                {
                    unityDeg = binding.body.jointPosition[0] * Mathf.Rad2Deg;
                }

                _currentDisplayDegCache[i] = ConvertUnityDegToDisplayDeg(i, unityDeg);
            }
        }

        private void ClearArticulationVelocities()
        {
            if (joints == null) return;
            for (int i = 0; i < joints.Length; i++)
            {
                ArticulationBody body = joints[i] == null ? null : joints[i].body;
                if (body == null) continue;
                try
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                    body.jointVelocity = new ArticulationReducedSpace(0f);
                }
                catch { }
            }
        }

        private void CopyCurrentToTargetCache()
        {
            EnsureRuntimeArrays();
            if (_currentDisplayDegCache == null || _lastTargetDisplayDegCache == null) return;
            Array.Copy(_currentDisplayDegCache, _lastTargetDisplayDegCache, Mathf.Min(_currentDisplayDegCache.Length, _lastTargetDisplayDegCache.Length));
        }

        private void RaisePoseChanged(PoseSource source)
        {
            if (OnPoseChanged == null) return;
            float[] copy = new float[_lastTargetDisplayDegCache.Length];
            Array.Copy(_lastTargetDisplayDegCache, copy, copy.Length);
            OnPoseChanged.Invoke(source, copy);
        }

        private JointBinding ResolveJoint(int index)
        {
            EnsureJointArray();
            return index >= 0 && index < joints.Length ? joints[index] : null;
        }

        private void EnsureJointArray()
        {
            if (joints == null || joints.Length == 0) joints = new JointBinding[6];
            for (int i = 0; i < joints.Length; i++)
            {
                if (joints[i] == null) joints[i] = new JointBinding { name = $"joint_{i + 1}" };
                NormalizeJoint(joints[i], i);
            }
            if (_missingJointWarned == null || _missingJointWarned.Length != joints.Length)
            {
                _missingJointWarned = new bool[joints.Length];
            }
        }

        private void EnsureRuntimeArrays()
        {
            EnsureJointArray();
            EnsureFloatArray(ref runtimeHome0Deg, joints.Length);
            EnsureFloatArray(ref _currentDisplayDegCache, joints.Length);
            EnsureFloatArray(ref _lastTargetDisplayDegCache, joints.Length);
            EnsureFloatArray(ref _runtimeDisplayDegBuffer, joints.Length);
        }

        private static void EnsureFloatArray(ref float[] array, int count)
        {
            if (array != null && array.Length == count) return;
            float[] old = array;
            array = new float[count];
            if (old != null) Array.Copy(old, array, Mathf.Min(old.Length, array.Length));
        }

        private static void NormalizeJoint(JointBinding binding, int index)
        {
            if (binding == null) return;
            if (string.IsNullOrWhiteSpace(binding.name)) binding.name = $"joint_{index + 1}";
            binding.sign = Mathf.Approximately(binding.sign, 0f) ? 1f : Mathf.Sign(binding.sign);
            if (binding.maxDeg <= binding.minDeg)
            {
                binding.minDeg = -360f;
                binding.maxDeg = 360f;
            }
        }

        private ArticulationBody FindJointByCandidates(Transform root, int index, List<ArticulationBody> orderedMovableBodies, HashSet<ArticulationBody> used)
        {
            JointBinding binding = ResolveJoint(index);
            if (binding != null && !string.IsNullOrWhiteSpace(binding.name))
            {
                ArticulationBody mapped = FindJointByName(root, binding.name);
                if (mapped != null && !used.Contains(mapped)) return mapped;
            }
            ArticulationBody byJointName = FindJointByName(root, $"joint_{index + 1}");
            if (byJointName != null && !used.Contains(byJointName)) return byJointName;
            ArticulationBody byLinkName = FindJointByName(root, $"link_{index + 1}");
            if (byLinkName != null && !used.Contains(byLinkName)) return byLinkName;
            if (orderedMovableBodies != null)
            {
                for (int i = 0; i < orderedMovableBodies.Count; i++)
                {
                    ArticulationBody body = orderedMovableBodies[i];
                    if (body != null && !used.Contains(body)) return body;
                }
            }
            return null;
        }

        private static List<ArticulationBody> CollectOrderedMovableBodies(Transform root)
        {
            List<ArticulationBody> result = new List<ArticulationBody>();
            if (root == null) return result;
            ArticulationBody[] bodies = root.GetComponentsInChildren<ArticulationBody>(true);
            Array.Sort(bodies, CompareByHierarchyName);
            for (int i = 0; i < bodies.Length; i++)
            {
                ArticulationBody body = bodies[i];
                if (body == null || body.isRoot) continue;
                if (body.jointType == ArticulationJointType.RevoluteJoint || body.jointType == ArticulationJointType.PrismaticJoint)
                {
                    result.Add(body);
                }
            }
            return result;
        }

        private static int CompareByHierarchyName(ArticulationBody a, ArticulationBody b)
        {
            int ai = ExtractTrailingNumber(a == null ? string.Empty : a.name);
            int bi = ExtractTrailingNumber(b == null ? string.Empty : b.name);
            if (ai != bi) return ai.CompareTo(bi);
            return string.CompareOrdinal(GetPath(a == null ? null : a.transform), GetPath(b == null ? null : b.transform));
        }

        private static int ExtractTrailingNumber(string value)
        {
            if (string.IsNullOrEmpty(value)) return int.MaxValue;
            int end = value.Length - 1;
            while (end >= 0 && !char.IsDigit(value[end])) end--;
            if (end < 0) return int.MaxValue;
            int start = end;
            while (start >= 0 && char.IsDigit(value[start])) start--;
            string number = value.Substring(start + 1, end - start);
            return int.TryParse(number, out int parsed) ? parsed : int.MaxValue;
        }

        private static string GetPath(Transform transform)
        {
            if (transform == null) return string.Empty;
            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        private static ArticulationBody FindJointByName(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName)) return null;
            Transform found = FindChildRecursive(root, targetName);
            if (found == null) return null;
            return found.GetComponent<ArticulationBody>();
        }

        private static Transform FindChildRecursive(Transform root, string targetName)
        {
            if (root == null || string.IsNullOrWhiteSpace(targetName)) return null;
            if (string.Equals(root.name, targetName, StringComparison.OrdinalIgnoreCase)) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildRecursive(root.GetChild(i), targetName);
                if (found != null) return found;
            }
            return null;
        }

        private void WarnMissingJointOnce(int index)
        {
            if (_missingJointWarned == null || index < 0 || index >= _missingJointWarned.Length) return;
            if (_missingJointWarned[index]) return;
            _missingJointWarned[index] = true;
            Debug.LogWarning($"RobotModelController missing ArticulationBody for J{index + 1}.", this);
        }

        private static float SafeSign(float sign) => Mathf.Approximately(sign, 0f) ? 1f : Mathf.Sign(sign);
    }
}
