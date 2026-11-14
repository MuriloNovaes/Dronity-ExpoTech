using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

namespace ExpoTech
{
    /// <summary>
    /// LiDAR 2D por raycast: sensor_msgs/LaserScan em /scan, 360°, convenção ROS
    /// (ângulo 0 = frente, positivo = anti-horário visto de cima). Ignora o próprio robô.
    /// Coloque no lidar_link.
    /// </summary>
    public class ExpoDroneLidar : MonoBehaviour
    {
        public string topic = "/scan";
        public string frameId = "lidar_link";
        public float rateHz = 10f;
        public int samples = 360;
        public float rangeMin = 0.3f;
        public float rangeMax = 20f;
        [Tooltip("Desvio padrão do ruído de distância (m)")] public float rangeNoise = 0.01f;
        public LayerMask mask = ~0;
        [Tooltip("Raiz do robô (raios que batem nele são ignorados)")] public Transform robotRoot;
        public bool useSimTime;
        public bool drawRays;

        /// <summary>Última leitura (m); +Infinity = nada detectado.</summary>
        public float[] Ranges { get; private set; }

        ROSConnection ros;
        float elapsed;
        readonly RaycastHit[] hits = new RaycastHit[16];

        void Start()
        {
            if (robotRoot == null) robotRoot = transform.root;
            Ranges = new float[samples];
            ros = ROSConnection.GetOrCreateInstance();
            ros.RegisterPublisher<LaserScanMsg>(topic);
        }

        void FixedUpdate()
        {
            elapsed += Time.fixedDeltaTime;
            if (elapsed < 1f / rateHz) return;
            elapsed = 0f;

            float inc = 2f * Mathf.PI / samples;
            float angleMin = -Mathf.PI;
            for (int i = 0; i < samples; i++)
            {
                float angle = angleMin + i * inc;
                // Unity gira no sentido horário em torno de +Y; ROS é anti-horário em torno de +Z.
                Vector3 dir = transform.TransformDirection(Quaternion.AngleAxis(-angle * Mathf.Rad2Deg, Vector3.up) * Vector3.forward);
                Ranges[i] = Cast(transform.position, dir);
                if (drawRays)
                    Debug.DrawRay(transform.position, dir * (float.IsInfinity(Ranges[i]) ? rangeMax : Ranges[i]),
                        float.IsInfinity(Ranges[i]) ? Color.green : Color.red, 1f / rateHz);
            }

            ros.Publish(topic, new LaserScanMsg
            {
                header = new HeaderMsg { frame_id = frameId, stamp = ExpoRosTime.Now(useSimTime) },
                angle_min = angleMin,
                angle_max = angleMin + (samples - 1) * inc,
                angle_increment = inc,
                time_increment = 0f,
                scan_time = 1f / rateHz,
                range_min = rangeMin,
                range_max = rangeMax,
                ranges = (float[])Ranges.Clone(),
                intensities = new float[0],
            });
        }

        float Cast(Vector3 origin, Vector3 dir)
        {
            int n = Physics.RaycastNonAlloc(origin, dir, hits, rangeMax, mask, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider.transform.IsChildOf(robotRoot)) continue;
                if (hits[i].distance < best) best = hits[i].distance;
            }
            if (best < rangeMin) return float.NegativeInfinity; // REP 117: perto demais
            return float.IsInfinity(best) ? best : best + ExpoRosTime.Gaussian(rangeNoise);
        }
    }
}
