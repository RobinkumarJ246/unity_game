using System;
using UnityEngine;

namespace ContentHouse.Core
{
    /// <summary>
    /// Global noise event bus. Anything that makes noise reports here; the
    /// enemy AI subscribes and reacts based on distance vs. radius.
    /// </summary>
    public static class NoiseSystem
    {
        /// <summary>Position of the noise, audible radius in meters.</summary>
        public static event Action<Vector3, float> NoiseReported;

        public static void ReportNoise(Vector3 position, float audibleRadius)
        {
            NoiseReported?.Invoke(position, audibleRadius);
        }
    }
}
