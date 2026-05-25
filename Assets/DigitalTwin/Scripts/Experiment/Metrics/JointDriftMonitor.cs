using System;
using UnityEngine;

namespace DigitalTwin
{
    [DisallowMultipleComponent]
    public sealed class JointDriftMonitor : MonoBehaviour
    {
        [SerializeField] private bool enableMonitor;
        [SerializeField] private RobotSignalSchema schema;
        [SerializeField] private int minSamples = 30;

        private double[] _sumErrorDeg;
        private long _sampleCount;

        public bool Enabled => enableMonitor;
        public long SampleCount => _sampleCount;

        private void OnValidate()
        {
            minSamples = Mathf.Max(1, minSamples);
        }

        public void ResetSamples()
        {
            _sumErrorDeg = null;
            _sampleCount = 0;
        }

        public void AddSample(float[] expectedRad, float[] actualRad)
        {
            if (!enableMonitor || expectedRad == null || actualRad == null)
            {
                return;
            }

            int count = Math.Min(expectedRad.Length, actualRad.Length);
            if (count <= 0)
            {
                return;
            }

            if (_sumErrorDeg == null || _sumErrorDeg.Length != count)
            {
                _sumErrorDeg = new double[count];
                _sampleCount = 0;
            }

            for (int i = 0; i < count; i++)
            {
                _sumErrorDeg[i] += (expectedRad[i] - actualRad[i]) * Mathf.Rad2Deg;
            }

            _sampleCount++;
        }

        public bool TryGetMeanOffsetDeg(out float[] offsetDeg)
        {
            offsetDeg = null;
            if (!enableMonitor || _sumErrorDeg == null || _sampleCount < minSamples)
            {
                return false;
            }

            offsetDeg = new float[_sumErrorDeg.Length];
            for (int i = 0; i < offsetDeg.Length; i++)
            {
                offsetDeg[i] = (float)(_sumErrorDeg[i] / _sampleCount);
            }

            return true;
        }
    }
}
