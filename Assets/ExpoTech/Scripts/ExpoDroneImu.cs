using RosMessageTypes.Geometry;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using UnityEngine;

namespace ExpoTech
{
    /// <summary>
    /// IMU: sensor_msgs/Imu em /imu. Orientação no mundo (ENU), velocidade angular e aceleração
    /// própria (o que um acelerômetro mede: aceleração - gravidade) no frame do imu_link.
    /// Coloque no imu_link (filho rígido do base_link).
    /// </summary>
    public class ExpoDroneImu : MonoBehaviour
    {
        public string topic = "/imu";
        public string frameId = "imu_link";
        public float rateHz = 50f;
        [Tooltip("Desvio padrão do ruído do giroscópio (rad/s)")] public float gyroNoise = 0.002f;
        [Tooltip("Desvio padrão do ruído do acelerômetro (m/s²)")] public float accelNoise = 0.02f;
        public bool useSimTime;

        public Vector3 LastProperAccelerationLocal { get; private set; }

        ROSConnection ros;
        ArticulationBody body;
        Vector3 previousVelocity;
        bool hasPrevious;
        float elapsed;

        void Start()
        {
            body = GetComponentInParent<ArticulationBody>();
            ros = ROSConnection.GetOrCreateInstance();
            ros.RegisterPublisher<ImuMsg>(topic);
        }

        void FixedUpdate()
        {
            Vector3 v = body.linearVelocity;
            Vector3 accel = hasPrevious ? (v - previousVelocity) / Time.fixedDeltaTime : Vector3.zero;
            previousVelocity = v;
            hasPrevious = true;

            elapsed += Time.fixedDeltaTime;
            if (elapsed < 1f / rateHz) return;
            elapsed = 0f;

            var inv = Quaternion.Inverse(transform.rotation);
            Vector3 proper = inv * (accel - Physics.gravity);
            LastProperAccelerationLocal = proper;
            var a = proper.To<FLU>();
            // Vetor axial: trocar de mão (Unity esquerda -> ROS direita) inverte o sinal.
            var w = (inv * body.angularVelocity).To<FLU>();

            ros.Publish(topic, new ImuMsg
            {
                header = new HeaderMsg { frame_id = frameId, stamp = ExpoRosTime.Now(useSimTime) },
                orientation = transform.rotation.To<FLU>(),
                orientation_covariance = Diagonal(1e-4),
                angular_velocity = new Vector3Msg(
                    -w.x + ExpoRosTime.Gaussian(gyroNoise), -w.y + ExpoRosTime.Gaussian(gyroNoise), -w.z + ExpoRosTime.Gaussian(gyroNoise)),
                angular_velocity_covariance = Diagonal(gyroNoise * gyroNoise),
                linear_acceleration = new Vector3Msg(
                    a.x + ExpoRosTime.Gaussian(accelNoise), a.y + ExpoRosTime.Gaussian(accelNoise), a.z + ExpoRosTime.Gaussian(accelNoise)),
                linear_acceleration_covariance = Diagonal(accelNoise * accelNoise),
            });
        }

        static double[] Diagonal(double v) => new[] { v, 0, 0, 0, v, 0, 0, 0, v };
    }
}
