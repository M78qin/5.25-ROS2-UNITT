using System;
using UnityEngine;

namespace DigitalTwin
{
    [CreateAssetMenu(menuName = "Digital Twin/Robot Signal Schema", fileName = "RobotSignalSchema")]
    public sealed class RobotSignalSchema : ScriptableObject
    {
        [Header("Required / 当前必需")]
        [Tooltip("DartStudio joint_states.position 的关节顺序。当前实时同步只要求 6 个关节角和 6 轴力；如果 UDP 包里没有 name 字段，就按这里的顺序写入 Unity。")]
        [SerializeField] private string[] jointNames = { "joint_1", "joint_2", "joint_3", "joint_4", "joint_5", "joint_6" };

        [Tooltip("6 轴力/力矩显示与记录名称，顺序固定为 Fx,Fy,Fz,Tx,Ty,Tz。")]
        [SerializeField] private string[] forceSignalNames = { "fx", "fy", "fz", "tx", "ty", "tz" };

        [Header("Optional / 当前可为空")]
        [Tooltip("可选关节速度名称。DartStudio 未发送 velocity 时保持空即可，不需要额外开关。")]
        [SerializeField] private string[] velocitySignalNames = Array.Empty<string>();

        [Tooltip("可选关节力矩名称。DartStudio 未发送 effort 时保持空即可，不需要额外开关。")]
        [SerializeField] private string[] torqueSignalNames = Array.Empty<string>();

        [Tooltip("可选扩展信号名称。温度、电流、报警等后续论文指标需要时再添加。")]
        [SerializeField] private string[] extraSignalNames = Array.Empty<string>();

        [Header("Calibration / Compensation")]
        [SerializeField] private string calibrationVersion = "none";
        [SerializeField] private float[] calibrationOffsetDeg = Array.Empty<float>();

        public string[] JointNames => jointNames;
        public string[] ForceSignalNames => forceSignalNames;
        public string[] VelocitySignalNames => velocitySignalNames;
        public string[] TorqueSignalNames => torqueSignalNames;
        public string[] ExtraSignalNames => extraSignalNames;
        public string CalibrationVersion => calibrationVersion;
        public float[] CalibrationOffsetDeg => calibrationOffsetDeg;
        public int JointCount => jointNames == null ? 0 : jointNames.Length;

        private void OnValidate()
        {
            if (jointNames == null || jointNames.Length == 0)
            {
                jointNames = new[] { "joint_1", "joint_2", "joint_3", "joint_4", "joint_5", "joint_6" };
            }

            if (forceSignalNames == null) forceSignalNames = Array.Empty<string>();
            if (velocitySignalNames == null) velocitySignalNames = Array.Empty<string>();
            if (torqueSignalNames == null) torqueSignalNames = Array.Empty<string>();
            if (extraSignalNames == null) extraSignalNames = Array.Empty<string>();
            if (calibrationOffsetDeg == null) calibrationOffsetDeg = Array.Empty<float>();
        }

        public float GetCalibrationOffsetRad(int jointIndex)
        {
            if (calibrationOffsetDeg == null || jointIndex < 0 || jointIndex >= calibrationOffsetDeg.Length)
            {
                return 0f;
            }

            return calibrationOffsetDeg[jointIndex] * Mathf.Deg2Rad;
        }

        public int ResolveJointIndex(string jointName, int fallbackIndex)
        {
            if (!string.IsNullOrWhiteSpace(jointName) && jointNames != null)
            {
                for (int i = 0; i < jointNames.Length; i++)
                {
                    if (string.Equals(jointNames[i], jointName, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            return fallbackIndex >= 0 && fallbackIndex < JointCount ? fallbackIndex : -1;
        }
    }
}
