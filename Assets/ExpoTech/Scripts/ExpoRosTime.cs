using RosMessageTypes.BuiltinInterfaces;
using UnityEngine;

namespace ExpoTech
{
    /// <summary>Carimbo de tempo ROS (funciona nos modos ROS1 e ROS2 do ROS-TCP-Connector).</summary>
    public static class ExpoRosTime
    {
        public static TimeMsg Now(bool useSimTime)
        {
            double s = useSimTime ? Time.timeAsDouble : (System.DateTime.UtcNow - System.DateTime.UnixEpoch).TotalSeconds;
            uint nanosec = (uint)((s - System.Math.Floor(s)) * 1e9);
#if ROS2
            return new TimeMsg((int)s, nanosec);
#else
            return new TimeMsg((uint)s, nanosec);
#endif
        }

        /// <summary>Ruído gaussiano (Box-Muller) para sensores mais realistas.</summary>
        public static float Gaussian(float sigma)
        {
            if (sigma <= 0f) return 0f;
            float u1 = 1f - Random.value, u2 = Random.value;
            return sigma * Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Sin(2f * Mathf.PI * u2);
        }
    }
}
