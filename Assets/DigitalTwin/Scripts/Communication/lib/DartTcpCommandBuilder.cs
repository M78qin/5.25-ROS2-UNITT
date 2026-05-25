using System.Globalization;
using System.Text;
using UnityEngine;

namespace DigitalTwin
{
    /// <summary>
    /// Builds DartStudio TCP command JSON strings.
    /// Keeps wire format in one place for Bridge/UI/tests.
    /// </summary>
    public static class DartTcpCommandBuilder
    {
        public static string BuildSetMode(string mode)
        {
            if (string.IsNullOrWhiteSpace(mode))
            {
                return string.Empty;
            }

            return "{\"cmd\":\"SET_MODE\",\"mode\":\"" + EscapeJson(mode) + "\"}";
        }

        public static string BuildMoveJoint(float[] targetDeg)
        {
            if (targetDeg == null || targetDeg.Length == 0)
            {
                return string.Empty;
            }

            // DRL owns MOVE_VEL/MOVE_ACC. DartStudio MOVE_JOINT must not carry speed.
            StringBuilder builder = new StringBuilder(64 + targetDeg.Length * 14);
            builder.Append("{\"cmd\":\"MOVE_JOINT\",\"target\":[");
            for (int i = 0; i < targetDeg.Length; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                builder.Append(targetDeg[i].ToString("0.######", CultureInfo.InvariantCulture));
            }

            builder.Append("]}");
            return builder.ToString();
        }

        public static string BuildHalt()
        {
            return "{\"cmd\":\"HALT\"}";
        }

        public static string BuildGetState()
        {
            return "{\"cmd\":\"GET_STATE\"}";
        }

        public static string BuildHeartbeat()
        {
            return "{\"cmd\":\"HEARTBEAT\"}";
        }

        public static string EscapeJson(string value)
        {
            return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }
}
