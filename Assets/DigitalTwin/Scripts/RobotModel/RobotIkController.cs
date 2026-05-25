using System;
using System.Diagnostics;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// 独立 IK 模块：负责 IK Target、TCP 位姿、阻尼最小二乘求解、Preview 输出。
    /// 默认不发送真实机器人命令；真实控制必须通过 TwinCommandController。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-150)]
    public sealed class RobotIkController : MonoBehaviour
    {
        public enum IkStatus
        {
            Disabled,
            Idle,
            Solved,
            BestEffort,
            MaxIterations,
            Timeout,
            InvalidTarget,
            SingularOrIllConditioned,
            MissingRobotModel
        }

        [Header("Enable / 开关")]
        [SerializeField, Tooltip("是否启用 IK 模块。关闭后不求解、不绘制 Scene Handle、不影响其他模块。")]
        private bool enableIk = true;

        [SerializeField, Tooltip("是否允许 Play 模式下使用 IK。默认关闭，只有 UI 或调试明确开启时才工作。")]
        private bool enableRuntimeIk;

        [SerializeField, Tooltip("求解成功后是否把结果预览到虚拟模型。关闭后只计算 lastSolution，不动模型。")]
        private bool previewSolutionOnModel = true;

        [SerializeField, Tooltip("求解未完全达到容差但误差下降时，是否应用最佳努力解。适合拖动目标时让模型持续跟随。")]
        private bool applyBestEffortWhenUnsolved = true;

        [Header("References / 引用")]
        [SerializeField, Tooltip("机器人模型控制器。通常与本 IK 脚本挂在同一个 base_link 上。")]
        private RobotModelController robotModel;

        [SerializeField, Tooltip("手动指定 TCP；为空时默认使用 RobotModelController 的 link_6 末端法兰。")]
        private Transform tcpTransform;

        [SerializeField, Tooltip("可选外部 IK Target Transform。为空时使用内部 Target 数据，并由 Scene 圆标拖动。")]
        private Transform ikTargetTransform;

        [Header("TCP Offset / TCP 偏移")]
        [SerializeField, Tooltip("TCP 相对法兰中心的位置偏移，单位 meter。若真实 TCP 不在 link_6 原点，可在这里设置。")]
        private Vector3 tcpLocalOffsetPosition;

        [SerializeField, Tooltip("TCP 相对法兰中心的姿态偏移，单位 degree。")]
        private Vector3 tcpLocalOffsetEulerDeg;

        [Header("Internal Target / 内部目标")]
        [SerializeField, Tooltip("内部 IK Target 世界坐标。未指定 IK Target Transform 时使用。")]
        private Vector3 internalTargetPosition;

        [SerializeField, Tooltip("内部 IK Target 世界欧拉角。未指定 IK Target Transform 时使用。")]
        private Vector3 internalTargetEulerDeg;

        [Header("Scene Handle / 场景拖动圆标")]
        [SerializeField, Tooltip("是否在 Scene 中显示可拖动 IK Target 圆标。关闭后不绘制、不拖动。")]
        private bool showIkHandle = true;

        [SerializeField, Tooltip("IK Target 圆标大小。")]
        private float ikHandleSize = 0.06f;

        [SerializeField, Tooltip("当前 TCP 圆标颜色。")]
        private Color tcpHandleColor = new Color(0.2f, 0.65f, 1f, 1f);

        [SerializeField, Tooltip("IK Target 圆标颜色。")]
        private Color ikTargetHandleColor = new Color(0.1f, 1f, 0.35f, 1f);

        [SerializeField, Tooltip("TCP 到 IK Target 连线颜色。")]
        private Color ikLineColor = Color.yellow;

        [SerializeField, Tooltip("拖动 IK Target 时是否自动求解。关闭后只移动 Target，用户点击 Solve IK 才求解。")]
        private bool autoSolveOnDrag = true;

        [SerializeField, Tooltip("是否显示 TCP / IK Target / error 标签。")]
        private bool showSceneLabels = true;

        [SerializeField, Tooltip("是否绘制 Gizmo。")]
        private bool drawGizmo = true;

        [Header("Solver / 求解器")]
        [SerializeField, Tooltip("是否求解 TCP 位置。通常保持开启。")]
        private bool solvePosition = true;

        [SerializeField, Tooltip("是否同时求解 TCP 姿态。第一阶段建议关闭，只解位置更稳定。")]
        private bool solveRotation;

        [SerializeField, Tooltip("位置误差权重。")]
        private float positionWeight = 1f;

        [SerializeField, Tooltip("姿态误差权重。建议小于位置权重。")]
        private float rotationWeight = 0.2f;

        [SerializeField, Tooltip("最大迭代次数。")]
        private int maxIterations = 120;

        [SerializeField, Tooltip("位置误差阈值，单位 meter。")]
        private float positionToleranceMeters = 0.003f;

        [SerializeField, Tooltip("姿态误差阈值，单位 degree。仅 Solve Rotation 开启时使用。")]
        private float rotationToleranceDeg = 2f;

        [SerializeField, Tooltip("有限差分步长，单位 degree。")]
        private float finiteStepDeg = 0.5f;

        [SerializeField, Tooltip("阻尼最小二乘阻尼。越大越稳但收敛更慢。")]
        private float damping = 0.02f;

        [SerializeField, Tooltip("关节更新增益。")]
        private float gain = 0.75f;

        [SerializeField, Tooltip("每次迭代单关节最大变化，单位 degree。")]
        private float maxStepDeg = 4f;

        [SerializeField, Tooltip("单次求解最大耗时，单位毫秒。防止 Play 模式长时间卡顿。")]
        private float maxSolveTimeMs = 8f;

        [SerializeField, Tooltip("Play 模式自动求解频率上限。只有 Enable Runtime IK 和 Auto Solve On Drag/目标变化时才会用到。")]
        private float runtimeSolveRateHz = 15f;

        private IkStatus _lastStatus = IkStatus.Idle;
        private float _lastPositionError = float.PositiveInfinity;
        private float _lastRotationErrorDeg = float.PositiveInfinity;
        private int _lastIterations;
        private float[] _lastSolutionDeg = Array.Empty<float>();
        private bool _hasLastSolution;
        private Vector3 _lastObservedTargetPosition;
        private Quaternion _lastObservedTargetRotation = Quaternion.identity;
        private float _nextRuntimeSolveTime;

        public bool EnableIk => enableIk;
        public bool EnableRuntimeIk => enableRuntimeIk;
        public bool PreviewSolutionOnModel => previewSolutionOnModel;
        public bool ShowIkHandle => showIkHandle;
        public bool AutoSolveOnDrag => autoSolveOnDrag;
        public bool ShowSceneLabels => showSceneLabels;
        public float IkHandleSize => Mathf.Max(0.01f, ikHandleSize);
        public Color TcpHandleColor => tcpHandleColor;
        public Color IkTargetHandleColor => ikTargetHandleColor;
        public Color IkLineColor => ikLineColor;
        public IkStatus LastStatus => _lastStatus;
        public float LastPositionError => _lastPositionError;
        public float LastRotationErrorDeg => _lastRotationErrorDeg;
        public int LastIterations => _lastIterations;
        public bool HasLastSolution => _hasLastSolution;
        public RobotModelController RobotModel => robotModel;

        private void Reset()
        {
            robotModel = GetComponent<RobotModelController>();
            InitializeTargetAtTcp();
        }

        private void OnEnable()
        {
            ResolveRobotModel();
            EnsureSolutionArray();
            if (internalTargetPosition == Vector3.zero)
            {
                InitializeTargetAtTcp();
            }
        }

        private void OnValidate()
        {
            ResolveRobotModel();
            ikHandleSize = Mathf.Max(0.01f, ikHandleSize);
            maxIterations = Mathf.Max(1, maxIterations);
            positionToleranceMeters = Mathf.Max(0.00001f, positionToleranceMeters);
            rotationToleranceDeg = Mathf.Max(0.01f, rotationToleranceDeg);
            finiteStepDeg = Mathf.Max(0.001f, finiteStepDeg);
            damping = Mathf.Max(0.000001f, damping);
            gain = Mathf.Clamp(gain, 0.01f, 2f);
            maxStepDeg = Mathf.Max(0.001f, maxStepDeg);
            maxSolveTimeMs = Mathf.Max(0.25f, maxSolveTimeMs);
            runtimeSolveRateHz = Mathf.Max(0.1f, runtimeSolveRateHz);
            positionWeight = Mathf.Max(0f, positionWeight);
            rotationWeight = Mathf.Max(0f, rotationWeight);
            EnsureSolutionArray();
        }

        private void Update()
        {
            if (!Application.isPlaying || !enableIk || !enableRuntimeIk || !autoSolveOnDrag)
            {
                return;
            }

            if (Time.unscaledTime < _nextRuntimeSolveTime)
            {
                return;
            }

            if (!HasTargetMovedSinceLastSolve())
            {
                return;
            }

            _nextRuntimeSolveTime = Time.unscaledTime + 1f / runtimeSolveRateHz;
            TrySolve(previewSolutionOnModel);
        }

        public void ResolveRobotModel()
        {
            if (robotModel == null)
            {
                robotModel = GetComponent<RobotModelController>();
            }
        }

        public Transform GetEffectiveTcpTransform()
        {
            ResolveRobotModel();
            if (tcpTransform != null) return tcpTransform;
            return robotModel != null ? robotModel.GetDefaultTcpTransform() : transform;
        }

        public string GetEffectiveTcpLabel()
        {
            Transform tcp = GetEffectiveTcpTransform();
            if (tcp == null) return "None";
            return tcpTransform != null ? tcp.name : $"{tcp.name}（默认末端法兰）";
        }

        public bool TryGetCurrentTcpPose(out Vector3 position, out Quaternion rotation)
        {
            ResolveRobotModel();
            if (robotModel == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return false;
            }
            return robotModel.TryGetTcpPose(tcpTransform, tcpLocalOffsetPosition, tcpLocalOffsetEulerDeg, out position, out rotation);
        }

        public void GetTargetPose(out Vector3 position, out Quaternion rotation)
        {
            if (ikTargetTransform != null)
            {
                position = ikTargetTransform.position;
                rotation = ikTargetTransform.rotation;
                return;
            }
            position = internalTargetPosition;
            rotation = Quaternion.Euler(internalTargetEulerDeg);
        }

        public void SetTargetPose(Vector3 position, Quaternion rotation)
        {
            if (ikTargetTransform != null)
            {
                ikTargetTransform.position = position;
                ikTargetTransform.rotation = rotation;
            }
            else
            {
                internalTargetPosition = position;
                internalTargetEulerDeg = rotation.eulerAngles;
            }
        }

        public void SetTargetPosition(Vector3 position)
        {
            GetTargetPose(out _, out Quaternion rot);
            SetTargetPose(position, rot);
        }

        public void InitializeTargetAtTcp()
        {
            if (TryGetCurrentTcpPose(out Vector3 pos, out Quaternion rot))
            {
                SetTargetPose(pos, rot);
                MarkTargetObserved();
            }
        }

        public void ResetTargetToTcp()
        {
            InitializeTargetAtTcp();
        }

        public bool TrySolve(bool applyPreview)
        {
            ResolveRobotModel();
            if (!enableIk)
            {
                _lastStatus = IkStatus.Disabled;
                return false;
            }
            if (robotModel == null)
            {
                _lastStatus = IkStatus.MissingRobotModel;
                return false;
            }
            if (Application.isPlaying && !enableRuntimeIk)
            {
                _lastStatus = IkStatus.Disabled;
                return false;
            }

            GetTargetPose(out Vector3 targetPos, out Quaternion targetRot);
            if (!IsFinite(targetPos))
            {
                _lastStatus = IkStatus.InvalidTarget;
                return false;
            }

            float[] seed = robotModel.ReadCurrentDisplayDegrees();
            bool solved = SolveDampedLeastSquares(seed, targetPos, targetRot, out float[] solution, out IkStatus status, out float posErr, out float rotErr, out int iterations);

            _lastStatus = status;
            _lastPositionError = posErr;
            _lastRotationErrorDeg = rotErr;
            _lastIterations = iterations;
            EnsureSolutionArray();
            Array.Copy(solution, _lastSolutionDeg, Mathf.Min(solution.Length, _lastSolutionDeg.Length));
            _hasLastSolution = solution.Length > 0;
            MarkTargetObserved();

            bool shouldApply = applyPreview && previewSolutionOnModel && (solved || applyBestEffortWhenUnsolved);
            if (shouldApply)
            {
                bool editPreview = !Application.isPlaying;
                if (Application.isPlaying)
                {
                    robotModel.SetControlAuthority(RobotModelController.ControlAuthority.RuntimeIkPreview);
                }
                robotModel.ApplyJointDisplayDegrees(solution, editPreview, RobotModelController.PoseSource.IkPreview, true, Application.isPlaying || editPreview);
            }

            return solved;
        }

        public bool ApplyLastSolutionPreview()
        {
            if (!_hasLastSolution || robotModel == null) return false;
            bool editPreview = !Application.isPlaying;
            if (Application.isPlaying)
            {
                if (!enableRuntimeIk) return false;
                robotModel.SetControlAuthority(RobotModelController.ControlAuthority.RuntimeIkPreview);
            }
            return robotModel.ApplyJointDisplayDegrees(_lastSolutionDeg, editPreview, RobotModelController.PoseSource.IkPreview, true, Application.isPlaying || editPreview);
        }

        public void ReleaseBackToLiveFeedback()
        {
            ResolveRobotModel();
            robotModel?.ReleaseToLiveFeedback();
        }

        public float[] GetLastSolutionDegreesCopy()
        {
            EnsureSolutionArray();
            float[] copy = new float[_lastSolutionDeg.Length];
            Array.Copy(_lastSolutionDeg, copy, copy.Length);
            return copy;
        }

        private bool SolveDampedLeastSquares(float[] seedDeg, Vector3 targetPos, Quaternion targetRot, out float[] solutionDeg, out IkStatus status, out float finalPosError, out float finalRotErrorDeg, out int iterations)
        {
            int n = robotModel.JointCount;
            int m = solveRotation ? 6 : 3;
            solutionDeg = new float[n];
            iterations = 0;
            status = IkStatus.MaxIterations;
            finalPosError = float.PositiveInfinity;
            finalRotErrorDeg = float.PositiveInfinity;

            if (n <= 0 || seedDeg == null)
            {
                status = IkStatus.MissingRobotModel;
                return false;
            }

            float[] q = new float[n];
            for (int i = 0; i < n; i++) q[i] = robotModel.ClampDisplayDeg(i, i < seedDeg.Length ? seedDeg[i] : 0f);

            float[] best = new float[n];
            Array.Copy(q, best, n);
            float bestScore = float.PositiveInfinity;
            float[] error = new float[m];
            float[,] jacobian = new float[m, n];
            float[,] a = new float[m, m];
            float[] y = new float[m];
            float[] deltaDeg = new float[n];
            float[] candidate = new float[n];
            float[] sample = new float[n];
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (iterations = 0; iterations < maxIterations; iterations++)
            {
                if (stopwatch.Elapsed.TotalMilliseconds > maxSolveTimeMs)
                {
                    status = IkStatus.Timeout;
                    break;
                }

                if (!EvaluatePose(q, out Vector3 currentPos, out Quaternion currentRot))
                {
                    status = IkStatus.InvalidTarget;
                    break;
                }

                FillError(error, currentPos, currentRot, targetPos, targetRot, out float posErr, out float rotErrDeg);
                float score = ComputeScore(posErr, rotErrDeg);
                if (score < bestScore)
                {
                    bestScore = score;
                    finalPosError = posErr;
                    finalRotErrorDeg = rotErrDeg;
                    Array.Copy(q, best, n);
                }

                if (posErr <= positionToleranceMeters && (!solveRotation || rotErrDeg <= rotationToleranceDeg))
                {
                    status = IkStatus.Solved;
                    Array.Copy(q, solutionDeg, n);
                    return true;
                }

                BuildJacobian(q, currentPos, currentRot, jacobian, sample);
                BuildNormalMatrix(jacobian, a, m, n, damping);
                if (!SolveLinearSystem(a, error, y, m))
                {
                    status = IkStatus.SingularOrIllConditioned;
                    break;
                }

                bool anyStep = false;
                for (int j = 0; j < n; j++)
                {
                    float deltaRad = 0f;
                    for (int row = 0; row < m; row++) deltaRad += jacobian[row, j] * y[row];
                    float stepDeg = Mathf.Clamp(deltaRad * Mathf.Rad2Deg * gain, -maxStepDeg, maxStepDeg);
                    deltaDeg[j] = stepDeg;
                    if (Mathf.Abs(stepDeg) > 1e-5f) anyStep = true;
                }

                if (!anyStep)
                {
                    status = IkStatus.SingularOrIllConditioned;
                    break;
                }

                bool accepted = false;
                float[] scales = { 1f, 0.5f, 0.25f };
                for (int s = 0; s < scales.Length; s++)
                {
                    for (int j = 0; j < n; j++) candidate[j] = robotModel.ClampDisplayDeg(j, q[j] + deltaDeg[j] * scales[s]);
                    if (!EvaluatePose(candidate, out Vector3 candPos, out Quaternion candRot)) continue;
                    float candPosErr = Vector3.Distance(targetPos, candPos);
                    float candRotErr = QuaternionAngleDeg(candRot, targetRot);
                    float candScore = ComputeScore(candPosErr, candRotErr);
                    if (candScore <= score || candScore < bestScore)
                    {
                        Array.Copy(candidate, q, n);
                        accepted = true;
                        break;
                    }
                }

                if (!accepted)
                {
                    for (int j = 0; j < n; j++) q[j] = robotModel.ClampDisplayDeg(j, q[j] + deltaDeg[j] * 0.1f);
                }
            }

            Array.Copy(best, solutionDeg, n);
            if (float.IsInfinity(finalPosError))
            {
                EvaluatePose(solutionDeg, out Vector3 p, out Quaternion r);
                finalPosError = Vector3.Distance(targetPos, p);
                finalRotErrorDeg = QuaternionAngleDeg(r, targetRot);
            }
            if (status == IkStatus.MaxIterations && bestScore < float.PositiveInfinity)
            {
                status = IkStatus.BestEffort;
            }
            return false;
        }

        private void BuildJacobian(float[] q, Vector3 currentPos, Quaternion currentRot, float[,] jacobian, float[] sample)
        {
            int n = robotModel.JointCount;
            float stepDeg = Mathf.Max(0.001f, finiteStepDeg);
            float stepRad = stepDeg * Mathf.Deg2Rad;
            int m = solveRotation ? 6 : 3;

            for (int j = 0; j < n; j++)
            {
                Array.Copy(q, sample, n);
                sample[j] = robotModel.ClampDisplayDeg(j, sample[j] + stepDeg);
                if (!EvaluatePose(sample, out Vector3 samplePos, out Quaternion sampleRot))
                {
                    for (int row = 0; row < m; row++) jacobian[row, j] = 0f;
                    continue;
                }

                Vector3 dp = (samplePos - currentPos) / stepRad * positionWeight;
                jacobian[0, j] = solvePosition ? dp.x : 0f;
                jacobian[1, j] = solvePosition ? dp.y : 0f;
                jacobian[2, j] = solvePosition ? dp.z : 0f;

                if (solveRotation)
                {
                    Vector3 drot = QuaternionToRotationVector(sampleRot * Quaternion.Inverse(currentRot)) / stepRad * rotationWeight;
                    jacobian[3, j] = drot.x;
                    jacobian[4, j] = drot.y;
                    jacobian[5, j] = drot.z;
                }
            }
        }

        private void FillError(float[] error, Vector3 currentPos, Quaternion currentRot, Vector3 targetPos, Quaternion targetRot, out float posErr, out float rotErrDeg)
        {
            Vector3 ep = targetPos - currentPos;
            posErr = ep.magnitude;
            if (solvePosition)
            {
                error[0] = ep.x * positionWeight;
                error[1] = ep.y * positionWeight;
                error[2] = ep.z * positionWeight;
            }
            else
            {
                error[0] = error[1] = error[2] = 0f;
            }

            rotErrDeg = QuaternionAngleDeg(currentRot, targetRot);
            if (solveRotation && error.Length >= 6)
            {
                Vector3 er = QuaternionToRotationVector(targetRot * Quaternion.Inverse(currentRot)) * rotationWeight;
                error[3] = er.x;
                error[4] = er.y;
                error[5] = er.z;
            }
        }

        private bool EvaluatePose(float[] jointDeg, out Vector3 position, out Quaternion rotation)
        {
            return robotModel.EvaluateTcpPoseDegrees(jointDeg, tcpTransform, tcpLocalOffsetPosition, tcpLocalOffsetEulerDeg, out position, out rotation);
        }

        private float ComputeScore(float positionError, float rotationErrorDeg)
        {
            float score = solvePosition ? positionError * Mathf.Max(0.0001f, positionWeight) : 0f;
            if (solveRotation) score += rotationErrorDeg * Mathf.Deg2Rad * Mathf.Max(0.0001f, rotationWeight);
            return score;
        }

        private static void BuildNormalMatrix(float[,] j, float[,] a, int m, int n, float dampingValue)
        {
            for (int r = 0; r < m; r++)
            {
                for (int c = 0; c < m; c++)
                {
                    float sum = 0f;
                    for (int k = 0; k < n; k++) sum += j[r, k] * j[c, k];
                    a[r, c] = sum + (r == c ? dampingValue * dampingValue : 0f);
                }
            }
        }

        private static bool SolveLinearSystem(float[,] matrix, float[] rhs, float[] result, int n)
        {
            float[,] a = new float[n, n + 1];
            for (int r = 0; r < n; r++)
            {
                for (int c = 0; c < n; c++) a[r, c] = matrix[r, c];
                a[r, n] = rhs[r];
            }

            for (int i = 0; i < n; i++)
            {
                int pivot = i;
                float pivotAbs = Mathf.Abs(a[i, i]);
                for (int r = i + 1; r < n; r++)
                {
                    float abs = Mathf.Abs(a[r, i]);
                    if (abs > pivotAbs)
                    {
                        pivotAbs = abs;
                        pivot = r;
                    }
                }
                if (pivotAbs < 1e-8f) return false;
                if (pivot != i)
                {
                    for (int c = i; c <= n; c++)
                    {
                        float tmp = a[i, c];
                        a[i, c] = a[pivot, c];
                        a[pivot, c] = tmp;
                    }
                }

                float div = a[i, i];
                for (int c = i; c <= n; c++) a[i, c] /= div;
                for (int r = 0; r < n; r++)
                {
                    if (r == i) continue;
                    float factor = a[r, i];
                    for (int c = i; c <= n; c++) a[r, c] -= factor * a[i, c];
                }
            }

            for (int i = 0; i < n; i++) result[i] = a[i, n];
            return true;
        }

        private bool HasTargetMovedSinceLastSolve()
        {
            GetTargetPose(out Vector3 pos, out Quaternion rot);
            return Vector3.Distance(pos, _lastObservedTargetPosition) > 0.0005f || Quaternion.Angle(rot, _lastObservedTargetRotation) > 0.25f;
        }

        private void MarkTargetObserved()
        {
            GetTargetPose(out _lastObservedTargetPosition, out _lastObservedTargetRotation);
        }

        private void EnsureSolutionArray()
        {
            int count = robotModel != null && robotModel.JointCount > 0 ? robotModel.JointCount : 6;
            if (_lastSolutionDeg != null && _lastSolutionDeg.Length == count) return;
            float[] old = _lastSolutionDeg;
            _lastSolutionDeg = new float[count];
            if (old != null) Array.Copy(old, _lastSolutionDeg, Mathf.Min(old.Length, _lastSolutionDeg.Length));
        }

        private static Vector3 QuaternionToRotationVector(Quaternion q)
        {
            if (q.w < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            q.ToAngleAxis(out float angleDeg, out Vector3 axis);
            if (!IsFinite(axis) || axis.sqrMagnitude < 1e-10f || float.IsNaN(angleDeg)) return Vector3.zero;
            if (angleDeg > 180f) angleDeg -= 360f;
            return axis.normalized * angleDeg * Mathf.Deg2Rad;
        }

        private static float QuaternionAngleDeg(Quaternion current, Quaternion target)
        {
            float angle = Quaternion.Angle(current, target);
            return float.IsNaN(angle) ? 0f : angle;
        }

        private static bool IsFinite(Vector3 v)
        {
            return !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
                   !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        }

        private void OnDrawGizmosSelected()
        {
            if (!enableIk || !drawGizmo) return;
            if (TryGetCurrentTcpPose(out Vector3 tcpPos, out _))
            {
                Gizmos.color = tcpHandleColor;
                Gizmos.DrawWireSphere(tcpPos, ikHandleSize * 0.75f);
            }
            GetTargetPose(out Vector3 targetPos, out _);
            Gizmos.color = ikTargetHandleColor;
            Gizmos.DrawWireSphere(targetPos, ikHandleSize);
            if (TryGetCurrentTcpPose(out Vector3 tcp, out _))
            {
                Gizmos.color = ikLineColor;
                Gizmos.DrawLine(tcp, targetPos);
            }
        }
    }
}
