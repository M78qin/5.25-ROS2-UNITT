using System;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// 轻量编辑态姿态管理工具：关节滑条、Home0、Saved Home、Target 点位。
    /// IK 已拆分到 RobotIkController；本工具只读/写 RobotModelController 的姿态，不做 IK 求解。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class EditorRobotPoseTool : MonoBehaviour
    {
        [Serializable]
        public sealed class SavedHomePose
        {
            [Tooltip("Home 名称，仅用于 Inspector 显示。")]
            public string name = "Home";

            [Tooltip("是否启用该 Home。禁用后仍保留数据。")]
            public bool enabled = true;

            [Tooltip("Home 关节角，单位 degree，小数有效。所有角度都是相对于 Zero 的绝对角，不是增量。")]
            public float[] jointDeg = new float[6];
        }

        [Serializable]
        public sealed class TargetPoint
        {
            [Tooltip("目标点名称，用于后续轨迹点、双向控制或论文实验记录。")]
            public string name = "Target";

            [Tooltip("是否启用该目标点。")]
            public bool enabled = true;

            [Tooltip("目标关节角，单位 degree，小数有效。所有角度都是相对于 Zero 的绝对角，不是增量。")]
            public float[] jointDeg = new float[6];

            [Tooltip("备注。")]
            public string notes;
        }

        [Header("Binding / 绑定")]
        [SerializeField, Tooltip("机器人模型控制器。通常与本工具挂在同一个 base_link 上。")]
        private RobotModelController robotModel;

        [SerializeField, Tooltip("是否允许非 Play 模式下通过 Inspector 控制模型。")]
        private bool enableInEditMode = true;

        [SerializeField, Tooltip("拖动关节滑条时是否立即应用到 ArticulationBody。")]
        private bool applyContinuously = true;

        [Header("Play Mode Manual Control / Play 模式手动控制")]
        [SerializeField, Tooltip("Play 模式下是否允许检查器关节滑条直接控制虚拟模型。开启后 RuntimeLive 实时反馈会暂时停止覆盖模型。")]
        private bool enableRuntimeManualJointControl;

        [SerializeField, Tooltip("关闭 Play 模式手动控制时，是否自动把模型控制权释放回 RuntimeLive 实时反馈。")]
        private bool releaseToLiveWhenManualOff = true;

        [Header("Joint Sliders / 关节滑条")]
        [SerializeField, Tooltip("当前调试目标角，单位 degree，小数有效。它是相对于 Zero 的绝对角。")]
        private float[] targetDeg = new float[6];

        [Header("Runtime Initial Home0 / 运行初始 Home0")]
        [SerializeField, Tooltip("运行开始可应用的 Home0 姿态，单位 degree。它不是 Zero，只是启动显示姿态。")]
        private float[] home0Deg = new float[6];

        [Header("Saved Homes / 其他 Home 姿态")]
        [SerializeField, Tooltip("编辑态常用 Home 姿态。不会自动作为 Runtime 初始姿态；Runtime 初始姿态只使用 Home0。")]
        private SavedHomePose[] savedHomes = Array.Empty<SavedHomePose>();

        [Header("Target Points / 目标点位")]
        [SerializeField, Tooltip("目标点位。只保存 6 轴关节角，后续可交给 RobotIkController / TwinCommandController 使用。")]
        private TargetPoint[] targetPoints = Array.Empty<TargetPoint>();

        private RobotIkController _ikController;
        private bool _subscribed;

        public RobotModelController RobotModel => robotModel;
        public RobotIkController IkController => _ikController != null ? _ikController : (_ikController = GetComponent<RobotIkController>());
        public bool EnableInEditMode => enableInEditMode;
        public bool ApplyContinuously => applyContinuously;
        public bool EnableRuntimeManualJointControl => enableRuntimeManualJointControl;
        public bool ReleaseToLiveWhenManualOff => releaseToLiveWhenManualOff;
        public float[] TargetDeg => targetDeg;
        public float[] Home0Deg => robotModel != null ? robotModel.RuntimeHome0Deg : home0Deg;
        public SavedHomePose[] SavedHomes => savedHomes;
        public TargetPoint[] TargetPoints => targetPoints;
        public int JointCount => robotModel == null ? 0 : robotModel.JointCount;

        private void Reset()
        {
            robotModel = GetComponent<RobotModelController>();
            _ikController = GetComponent<RobotIkController>();
            ResolveRobotModel();
            EnsureArrays();
            SetTargetFromCurrentModel();
            SyncHome0FromRuntimeModel();
        }

        private void OnEnable()
        {
            ResolveRobotModel();
            _ikController = GetComponent<RobotIkController>();
            EnsureArrays();
            SubscribePoseEvent();
            SyncHome0FromRuntimeModel();
            SyncRuntimeManualAuthority();
        }

        private void OnDisable()
        {
            if (Application.isPlaying && enableRuntimeManualJointControl && releaseToLiveWhenManualOff && robotModel != null)
            {
                robotModel.ReleaseToLiveFeedback();
            }
            UnsubscribePoseEvent();
        }

        private void OnValidate()
        {
            ResolveRobotModel();
            _ikController = GetComponent<RobotIkController>();
            EnsureArrays();
            ClampAllPoseArrays();
            SyncHome0FromRuntimeModel();
            SyncRuntimeManualAuthority();
        }

        public void ResolveRobotModel()
        {
            if (robotModel == null)
            {
                robotModel = GetComponent<RobotModelController>();
            }
        }

        public bool AutoBind()
        {
            ResolveRobotModel();
            bool ok = robotModel != null && robotModel.AutoBindJoints();
            EnsureArrays();
            SetTargetFromCurrentModel();
            SyncHome0FromRuntimeModel();
            return ok;
        }

        public void SetRuntimeManualJointControl(bool enabled)
        {
            enableRuntimeManualJointControl = enabled;
            SyncRuntimeManualAuthority();
        }

        public void SyncRuntimeManualAuthority()
        {
            ResolveRobotModel();
            if (!Application.isPlaying || robotModel == null) return;

            if (enableRuntimeManualJointControl)
            {
                robotModel.SetControlAuthority(RobotModelController.ControlAuthority.RuntimeManualJoint);
                SetTargetFromCurrentModel();
            }
            else if (releaseToLiveWhenManualOff && robotModel.CurrentControlAuthority == RobotModelController.ControlAuthority.RuntimeManualJoint)
            {
                robotModel.ReleaseToLiveFeedback();
            }
        }

        public void ReleaseBackToLiveFeedback()
        {
            enableRuntimeManualJointControl = false;
            ResolveRobotModel();
            robotModel?.ReleaseToLiveFeedback();
        }

        public void ApplyAll()
        {
            ResolveRobotModel();
            if (robotModel == null) return;

            if (Application.isPlaying)
            {
                if (!enableRuntimeManualJointControl) return;
                robotModel.SetControlAuthority(RobotModelController.ControlAuthority.RuntimeManualJoint);
                robotModel.ApplyJointDisplayDegrees(targetDeg, false, RobotModelController.PoseSource.RuntimeManual, true, true);
                return;
            }

            if (!enableInEditMode) return;
            robotModel.ApplyJointDisplayDegrees(targetDeg, true, RobotModelController.PoseSource.EditorSlider, true, true);
        }

        public void SetTargetDeg(int index, float value)
        {
            EnsureArrays();
            if (index < 0 || index >= targetDeg.Length) return;
            targetDeg[index] = ClampJoint(index, value);
        }

        public void SetTargetFromCurrentModel()
        {
            ResolveRobotModel();
            if (robotModel == null) return;
            CopyInto(ref targetDeg, robotModel.ReadCurrentDisplayDegrees(), robotModel.JointCount);
            ClampAllPoseArrays();
        }

        public void SetTargetFromPose(float[] jointDeg)
        {
            ResolveRobotModel();
            int count = robotModel != null ? robotModel.JointCount : (jointDeg == null ? 0 : jointDeg.Length);
            CopyInto(ref targetDeg, jointDeg, count);
            ClampAllPoseArrays();
        }

        public void ResetTargetToZero(bool apply)
        {
            EnsureArrays();
            Array.Clear(targetDeg, 0, targetDeg.Length);
            if (apply) ApplyAll();
        }

        public void CaptureCurrentAsZero()
        {
            ResolveRobotModel();
            robotModel?.CaptureCurrentPoseAsZero();
        }

        public void ClearZero()
        {
            ResolveRobotModel();
            robotModel?.ClearCapturedZero();
        }

        public string GetBindingReport()
        {
            ResolveRobotModel();
            return robotModel == null ? "未绑定 RobotModelController。" : robotModel.GetBindingReport();
        }

        public string GetZeroReport()
        {
            ResolveRobotModel();
            if (robotModel == null) return "未绑定 RobotModelController。";
            return $"Zero 状态：{robotModel.GetCapturedZeroCount()}/{robotModel.JointCount} 个关节已捕获 Zero。Zero 是机械 0 点基准，不会被 Home/Target/IK 修改。";
        }

        public Vector2 GetJointLimitsDeg(int index)
        {
            ResolveRobotModel();
            return robotModel == null ? new Vector2(-360f, 360f) : robotModel.GetJointLimitsDeg(index);
        }

        public void SetHome0FromCurrent()
        {
            ResolveRobotModel();
            if (robotModel == null) return;
            float[] pose = robotModel.ReadCurrentDisplayDegrees();
            robotModel.SetRuntimeHome0Degrees(pose);
            SyncHome0FromRuntimeModel();
        }

        public void SetHome0FromTarget()
        {
            ResolveRobotModel();
            if (robotModel == null)
            {
                CopyInto(ref home0Deg, targetDeg, targetDeg == null ? 6 : targetDeg.Length);
                ClampAllPoseArrays();
                return;
            }

            robotModel.SetRuntimeHome0Degrees(targetDeg);
            SyncHome0FromRuntimeModel();
        }

        public void SetHome0JointDeg(int index, float value)
        {
            ResolveRobotModel();
            if (robotModel == null)
            {
                EnsureArrays();
                if (index >= 0 && index < home0Deg.Length) home0Deg[index] = ClampJoint(index, value);
                return;
            }

            float[] pose = robotModel.GetRuntimeHome0DegreesCopy();
            if (index < 0 || index >= pose.Length) return;
            pose[index] = ClampJoint(index, value);
            robotModel.SetRuntimeHome0Degrees(pose);
            SyncHome0FromRuntimeModel();
        }

        public void ApplyHome0()
        {
            if (!CanApplyPose()) return;
            float[] pose = robotModel.GetRuntimeHome0DegreesCopy();
            bool editPreview = !Application.isPlaying;
            if (Application.isPlaying) robotModel.SetControlAuthority(RobotModelController.ControlAuthority.RuntimeManualJoint);
            robotModel.ApplyJointDisplayDegrees(pose, editPreview, RobotModelController.PoseSource.Home, true, true);
        }

        public void SyncHome0FromRuntimeModel()
        {
            ResolveRobotModel();
            if (robotModel == null)
            {
                EnsureArrays();
                return;
            }

            CopyInto(ref home0Deg, robotModel.GetRuntimeHome0DegreesCopy(), robotModel.JointCount);
            ClampPoseArray(home0Deg);
        }

        public void SyncHome0ToRuntimeModel()
        {
            ResolveRobotModel();
            if (robotModel == null) return;
            EnsureArrays();
            robotModel.SetRuntimeHome0Degrees(home0Deg);
            SyncHome0FromRuntimeModel();
        }

        public void AddSavedHome()
        {
            EnsureArrays();
            int oldLength = savedHomes == null ? 0 : savedHomes.Length;
            Array.Resize(ref savedHomes, oldLength + 1);
            savedHomes[oldLength] = new SavedHomePose { name = $"Home {oldLength + 1}", jointDeg = CopyPose(targetDeg, JointCount) };
        }

        public void RemoveSavedHome(int index)
        {
            if (savedHomes == null || index < 0 || index >= savedHomes.Length) return;
            for (int i = index; i < savedHomes.Length - 1; i++) savedHomes[i] = savedHomes[i + 1];
            Array.Resize(ref savedHomes, savedHomes.Length - 1);
        }

        public void SetSavedHomeFromCurrent(int index)
        {
            EnsureArrays();
            if (savedHomes == null || index < 0 || index >= savedHomes.Length) return;
            savedHomes[index].jointDeg = robotModel == null ? CopyPose(targetDeg, JointCount) : CopyPose(robotModel.ReadCurrentDisplayDegrees(), robotModel.JointCount);
            ClampAllPoseArrays();
        }

        public void SetSavedHomeFromTarget(int index)
        {
            EnsureArrays();
            if (savedHomes == null || index < 0 || index >= savedHomes.Length) return;
            savedHomes[index].jointDeg = CopyPose(targetDeg, JointCount);
            ClampAllPoseArrays();
        }

        public void ApplySavedHome(int index)
        {
            if (!CanApplyPose() || savedHomes == null || index < 0 || index >= savedHomes.Length || savedHomes[index] == null) return;
            bool editPreview = !Application.isPlaying;
            if (Application.isPlaying) robotModel.SetControlAuthority(RobotModelController.ControlAuthority.RuntimeManualJoint);
            robotModel.ApplyJointDisplayDegrees(savedHomes[index].jointDeg, editPreview, RobotModelController.PoseSource.Home, true, true);
        }

        public void AddTargetPoint()
        {
            AddTargetPointFromJointPose("Target", targetDeg, string.Empty);
        }

        public void AddTargetPointFromCurrent()
        {
            ResolveRobotModel();
            float[] pose = robotModel == null ? targetDeg : robotModel.ReadCurrentDisplayDegrees();
            AddTargetPointFromJointPose("Target From Current", pose, "Saved from current robot pose.");
        }

        public void AddTargetPointFromIkSolution()
        {
            RobotIkController ik = IkController;
            if (ik != null && ik.HasLastSolution)
            {
                AddTargetPointFromJointPose("Target From IK", ik.GetLastSolutionDegreesCopy(), "Saved from RobotIkController last solution.");
            }
        }

        public void AddTargetPointFromJointPose(string name, float[] jointDeg, string notes)
        {
            EnsureArrays();
            int oldLength = targetPoints == null ? 0 : targetPoints.Length;
            Array.Resize(ref targetPoints, oldLength + 1);
            targetPoints[oldLength] = new TargetPoint
            {
                name = string.IsNullOrWhiteSpace(name) ? $"Target {oldLength + 1}" : name,
                enabled = true,
                jointDeg = CopyPose(jointDeg, JointCount),
                notes = notes ?? string.Empty
            };
            ClampAllPoseArrays();
        }

        public void RemoveTargetPoint(int index)
        {
            if (targetPoints == null || index < 0 || index >= targetPoints.Length) return;
            for (int i = index; i < targetPoints.Length - 1; i++) targetPoints[i] = targetPoints[i + 1];
            Array.Resize(ref targetPoints, targetPoints.Length - 1);
        }

        public void SetTargetPointFromCurrent(int index)
        {
            EnsureArrays();
            if (targetPoints == null || index < 0 || index >= targetPoints.Length) return;
            targetPoints[index].jointDeg = robotModel == null ? CopyPose(targetDeg, JointCount) : CopyPose(robotModel.ReadCurrentDisplayDegrees(), robotModel.JointCount);
            ClampAllPoseArrays();
        }

        public void SetTargetPointFromIkSolution(int index)
        {
            EnsureArrays();
            RobotIkController ik = IkController;
            if (targetPoints == null || index < 0 || index >= targetPoints.Length || ik == null || !ik.HasLastSolution) return;
            targetPoints[index].jointDeg = ik.GetLastSolutionDegreesCopy();
            ClampAllPoseArrays();
        }

        public void SetTargetPointFromSliders(int index)
        {
            EnsureArrays();
            if (targetPoints == null || index < 0 || index >= targetPoints.Length) return;
            targetPoints[index].jointDeg = CopyPose(targetDeg, JointCount);
            ClampAllPoseArrays();
        }

        public void SetTargetPointJointDeg(int targetIndex, int jointIndex, float value)
        {
            EnsureArrays();
            if (targetPoints == null || targetIndex < 0 || targetIndex >= targetPoints.Length) return;
            TargetPoint point = targetPoints[targetIndex];
            EnsureFloatArray(ref point.jointDeg, JointCount);
            if (jointIndex < 0 || jointIndex >= point.jointDeg.Length) return;
            point.jointDeg[jointIndex] = ClampJoint(jointIndex, value);
        }

        public void ApplyTargetPoint(int index)
        {
            if (!CanApplyPose() || targetPoints == null || index < 0 || index >= targetPoints.Length || targetPoints[index] == null) return;
            bool editPreview = !Application.isPlaying;
            if (Application.isPlaying) robotModel.SetControlAuthority(RobotModelController.ControlAuthority.RuntimeManualJoint);
            robotModel.ApplyJointDisplayDegrees(targetPoints[index].jointDeg, editPreview, RobotModelController.PoseSource.Target, true, true);
        }

        private void SubscribePoseEvent()
        {
            if (_subscribed || robotModel == null) return;
            robotModel.OnPoseChanged += HandleRobotPoseChanged;
            _subscribed = true;
        }

        private void UnsubscribePoseEvent()
        {
            if (!_subscribed || robotModel == null) return;
            robotModel.OnPoseChanged -= HandleRobotPoseChanged;
            _subscribed = false;
        }

        private void HandleRobotPoseChanged(RobotModelController.PoseSource source, float[] jointDeg)
        {
            if (source == RobotModelController.PoseSource.Evaluation)
            {
                return;
            }

            if (jointDeg == null) return;
            CopyInto(ref targetDeg, jointDeg, jointDeg.Length);
            ClampAllPoseArrays();
        }

        private bool CanApplyPose()
        {
            ResolveRobotModel();
            if (robotModel == null) return false;
            if (Application.isPlaying) return enableRuntimeManualJointControl;
            return enableInEditMode;
        }

        private bool CanApplyEditPose()
        {
            ResolveRobotModel();
            if (robotModel == null) return false;
            if (Application.isPlaying) return false;
            return enableInEditMode;
        }

        private void EnsureArrays()
        {
            ResolveRobotModel();
            int count = robotModel != null && robotModel.JointCount > 0 ? robotModel.JointCount : 6;
            EnsureFloatArray(ref targetDeg, count);
            EnsureFloatArray(ref home0Deg, count);
            EnsureSavedHomes(count);
            EnsureTargetPoints(count);
        }

        private void EnsureSavedHomes(int count)
        {
            if (savedHomes == null) savedHomes = Array.Empty<SavedHomePose>();
            for (int i = 0; i < savedHomes.Length; i++)
            {
                if (savedHomes[i] == null) savedHomes[i] = new SavedHomePose { name = $"Home {i + 1}" };
                EnsureFloatArray(ref savedHomes[i].jointDeg, count);
            }
        }

        private void EnsureTargetPoints(int count)
        {
            if (targetPoints == null) targetPoints = Array.Empty<TargetPoint>();
            for (int i = 0; i < targetPoints.Length; i++)
            {
                if (targetPoints[i] == null) targetPoints[i] = new TargetPoint { name = $"Target {i + 1}" };
                EnsureFloatArray(ref targetPoints[i].jointDeg, count);
            }
        }

        private static void EnsureFloatArray(ref float[] array, int count)
        {
            if (array != null && array.Length == count) return;
            float[] old = array;
            array = new float[count];
            if (old != null) Array.Copy(old, array, Mathf.Min(old.Length, array.Length));
        }

        private void ClampAllPoseArrays()
        {
            EnsureFloatArray(ref targetDeg, JointCount > 0 ? JointCount : 6);
            EnsureFloatArray(ref home0Deg, JointCount > 0 ? JointCount : 6);
            ClampPoseArray(targetDeg);
            ClampPoseArray(home0Deg);
            if (savedHomes != null)
            {
                for (int i = 0; i < savedHomes.Length; i++) if (savedHomes[i] != null) ClampPoseArray(savedHomes[i].jointDeg);
            }
            if (targetPoints != null)
            {
                for (int i = 0; i < targetPoints.Length; i++) if (targetPoints[i] != null) ClampPoseArray(targetPoints[i].jointDeg);
            }
        }

        private void ClampPoseArray(float[] pose)
        {
            if (pose == null) return;
            for (int i = 0; i < pose.Length; i++) pose[i] = ClampJoint(i, pose[i]);
        }

        private float ClampJoint(int index, float value)
        {
            ResolveRobotModel();
            return robotModel == null ? value : robotModel.ClampDisplayDeg(index, value);
        }

        private static void CopyInto(ref float[] destination, float[] source, int count)
        {
            EnsureFloatArray(ref destination, count <= 0 ? 6 : count);
            Array.Clear(destination, 0, destination.Length);
            if (source != null) Array.Copy(source, destination, Mathf.Min(source.Length, destination.Length));
        }

        private static float[] CopyPose(float[] source, int count)
        {
            if (count <= 0) count = source == null ? 6 : source.Length;
            float[] copy = new float[count];
            if (source != null) Array.Copy(source, copy, Mathf.Min(source.Length, copy.Length));
            return copy;
        }
    }
}
