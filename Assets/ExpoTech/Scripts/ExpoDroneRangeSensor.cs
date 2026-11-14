using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;

namespace ExpoTech
{
    /// <summary>
    /// Sensor de distância frontal (ultrassom): sensor_msgs/Range. O cone é amostrado com
    /// rays x rays raios; vale a menor distância. Coloque no camera_link (aponta para a frente).
    /// </summary>
    public class ExpoDroneRangeSensor : MonoBehaviour
    {
        public string topic = "/expo_drone/range_front";
        public string frameId = "camera_link";
        public float rateHz = 20f;
        [Tooltip("Abertura do cone (rad)")] public float fieldOfView = 0.5f;
        public float minRange = 0.05f;
        public float maxRange = 6f;
        [Tooltip("Raios por eixo dentro do cone")] public int rays = 5;
        [Tooltip("Ângulo mais baixo do cone (graus, negativo = para baixo). Evita medir o próprio chão.")]
        public float minPitchDeg = -2f;
        public LayerMask mask = ~0;
        public Transform robotRoot;
        public bool useSimTime;

        /// <summary>Última leitura (m); +Infinity = nada no alcance.</summary>
        public float Distance { get; private set; } = float.PositiveInfinity;

        ROSConnection ros;
        float elapsed;
        readonly RaycastHit[] hits = new RaycastHit[16];

        void Start()
        {
            if (robotRoot == null) robotRoot = transform.root;
            ros = ROSConnection.GetOrCreateInstance();
            ros.RegisterPublisher<RangeMsg>(topic);
        }

        void FixedUpdate()
        {
            elapsed += Time.fixedDeltaTime;
            if (elapsed < 1f / rateHz) return;
            elapsed = 0f;

            float best = float.PositiveInfinity;
            float half = fieldOfView * 0.5f * Mathf.Rad2Deg;
            for (int i = 0; i < rays; i++)
                for (int j = 0; j < rays; j++)
                {
                    float yaw = rays == 1 ? 0f : Mathf.Lerp(-half, half, i / (rays - 1f));
                    // No Unity, pitch positivo em torno de X aponta para baixo.
                    float down = Mathf.Min(half, -minPitchDeg);
                    float pitch = rays == 1 ? 0f : Mathf.Lerp(down, -half, j / (rays - 1f));
                    Vector3 dir = transform.TransformDirection(Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward);
                    int n = Physics.RaycastNonAlloc(transform.position, dir, hits, maxRange, mask, QueryTriggerInteraction.Ignore);
                    for (int k = 0; k < n; k++)
                        if (!hits[k].collider.transform.IsChildOf(robotRoot) && hits[k].distance < best)
                            best = hits[k].distance;
                }
            Distance = best;

            ros.Publish(topic, new RangeMsg
            {
                header = new HeaderMsg { frame_id = frameId, stamp = ExpoRosTime.Now(useSimTime) },
                radiation_type = RangeMsg.ULTRASOUND,
                field_of_view = fieldOfView,
                min_range = minRange,
                max_range = maxRange,
                range = best, // REP 117: +Inf = nada detectado
            });
        }
    }
}
