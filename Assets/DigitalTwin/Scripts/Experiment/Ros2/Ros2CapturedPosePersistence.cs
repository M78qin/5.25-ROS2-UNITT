using System;

namespace DigitalTwin
{
    /// <summary>Play 模式 Capture 的位姿缓冲，退出 Play 后写回 Inspector 序列化字段。</summary>
    public static class Ros2CapturedPosePersistence
    {
        public static float[] HomeDeg;
        public static float[] PresetADeg;
        public static float[] PresetBDeg;
        public static string Summary = string.Empty;
        public static bool HasCapture;

        public static void Save(float[] home, float[] presetA, float[] presetB, string summary)
        {
            HomeDeg = CloneSix(home);
            PresetADeg = CloneSix(presetA);
            PresetBDeg = CloneSix(presetB);
            Summary = summary ?? string.Empty;
            HasCapture = HomeDeg != null;
        }

        private static float[] CloneSix(float[] source)
        {
            float[] copy = new float[6];
            if (source != null)
            {
                Array.Copy(source, copy, Math.Min(source.Length, copy.Length));
            }

            return copy;
        }
    }
}
