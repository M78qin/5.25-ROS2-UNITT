using System;
using System.Collections.Generic;
using System.Globalization;

namespace DigitalTwin
{
    [Serializable]
    public sealed class Ros2TelemetryChannels
    {
        public bool jointPosition = true;
        public bool jointVelocity = true;
        public bool jointEffort = true;
        public bool actualTcpPose = true;
        public bool actualFlangePose;
        public bool externalTcpForce = true;
        public bool externalJointTorque = true;
        public bool actualJointTorque;
        public bool actualMotorTorque;
        public bool rawForceTorque;
        public bool targetJointPosition;
        public bool targetTcpPosition;
        public bool robotMode = true;
        public bool robotState = true;
        public bool controlMode = true;
        public bool jointTemperature;
        public bool solutionSpace;
        public bool operationSpeedRate;

        public Dictionary<string, bool> ToRosChannelMap()
        {
            var map = new Dictionary<string, bool>
            {
                ["joint_position"] = jointPosition,
                ["joint_velocity"] = jointVelocity,
                ["joint_effort"] = jointEffort,
                ["actual_tcp_position"] = actualTcpPose,
                ["actual_flange_position"] = actualFlangePose,
                ["external_tcp_force"] = externalTcpForce,
                ["external_joint_torque"] = externalJointTorque,
                ["actual_joint_torque"] = actualJointTorque,
                ["actual_motor_torque"] = actualMotorTorque,
                ["raw_force_torque"] = rawForceTorque,
                ["target_joint_position"] = targetJointPosition,
                ["target_tcp_position"] = targetTcpPosition,
                ["robot_mode"] = robotMode,
                ["robot_state"] = robotState,
                ["control_mode"] = controlMode,
                ["joint_temperature"] = jointTemperature,
                ["solution_space"] = solutionSpace,
                ["operation_speed_rate"] = operationSpeedRate,
            };
            return map;
        }

        public static Ros2TelemetryChannels FromPanel(
            bool joints, bool vel, bool eff, bool force, bool tcp, bool extra)
        {
            return new Ros2TelemetryChannels
            {
                jointPosition = joints,
                jointVelocity = vel,
                jointEffort = eff,
                externalTcpForce = force,
                externalJointTorque = force,
                actualTcpPose = tcp,
                actualJointTorque = extra,
                actualMotorTorque = extra,
                rawForceTorque = extra,
                targetJointPosition = extra,
                targetTcpPosition = extra,
                robotMode = extra || force,
                robotState = extra || force,
                controlMode = extra || force,
                jointTemperature = extra,
                solutionSpace = extra,
                operationSpeedRate = extra,
            };
        }

        public string ToJsonNotes()
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, bool> kv in ToRosChannelMap())
            {
                parts.Add("\"" + kv.Key + "\":" + (kv.Value ? "true" : "false"));
            }

            return "{" + string.Join(",", parts) + "}";
        }
    }
}
