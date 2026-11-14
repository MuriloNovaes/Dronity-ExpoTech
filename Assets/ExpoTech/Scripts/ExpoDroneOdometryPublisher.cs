using RosMessageTypes.BuiltinInterfaces;
using RosMessageTypes.Geometry;
using RosMessageTypes.Nav;
using RosMessageTypes.Std;
using RosMessageTypes.Tf2;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.ROSTCPConnector.ROSGeometry;
using UnityEngine;

namespace ExpoTech
{
    /// <summary>
    /// Publica nav_msgs/Odometry (/odom) e a TF odom -> base_link (/tf) a partir da pose real
    /// do ArticulationBody raiz ("ground truth" da simulação). Origem do odom = pose inicial.
    /// TFs das rodas/câmera ficam com o robot_state_publisher no ROS (URDF + /joint_states).
    /// </summary>
    public class ExpoDroneOdometryPublisher : MonoBehaviour
    {
        public string odomTopic = "/odom";
        public string tfTopic = "/tf";
        public string odomFrame = "odom";
        public string baseFrame = "base_link";
        public float publishRateHz = 30f;
        public bool publishTf = true;
        [Tooltip("ArticulationBody raiz (base_link)")] public ArticulationBody baseBody;
        [Tooltip("true = tempo da simulação (use_sim_time no ROS); false = relógio do sistema")]
        public bool useSimTime = false;

        ROSConnection ros;
        Vector3 startPosition;
        Quaternion startRotation;
        float elapsed;

        void Start()
        {
            if (baseBody == null) baseBody = GetComponentInChildren<ArticulationBody>();
            startPosition = baseBody.transform.position;
            startRotation = baseBody.transform.rotation;
            ros = ROSConnection.GetOrCreateInstance();
            ros.RegisterPublisher<OdometryMsg>(odomTopic);
            if (publishTf) ros.RegisterPublisher<TFMessageMsg>(tfTopic);
        }

        void FixedUpdate()
        {
            elapsed += Time.fixedDeltaTime;
            if (elapsed < 1f / publishRateHz) return;
            elapsed = 0f;

            var t = baseBody.transform;
            var inv = Quaternion.Inverse(startRotation);
            Vector3 position = inv * (t.position - startPosition);
            Quaternion rotation = inv * t.rotation;
            // Velocidades no frame do próprio robô (child_frame_id = base_link), como o ROS espera.
#if UNITY_6000_0_OR_NEWER
            Vector3 linear = Quaternion.Inverse(t.rotation) * baseBody.linearVelocity;
#else
            Vector3 linear = Quaternion.Inverse(t.rotation) * baseBody.velocity;
#endif
            Vector3 angular = Quaternion.Inverse(t.rotation) * baseBody.angularVelocity;
            var angularFlu = angular.To<FLU>();

            var header = new HeaderMsg { frame_id = odomFrame, stamp = Now() };
            var odom = new OdometryMsg
            {
                header = header,
                child_frame_id = baseFrame,
                pose = new PoseWithCovarianceMsg
                {
                    pose = new PoseMsg { position = position.To<FLU>(), orientation = rotation.To<FLU>() },
                    covariance = new double[36],
                },
                twist = new TwistWithCovarianceMsg
                {
                    twist = new TwistMsg
                    {
                        linear = linear.To<FLU>(),
                        // Vetor axial: trocar de mão (Unity esquerda -> ROS direita) inverte o sinal.
                        angular = new Vector3Msg(-angularFlu.x, -angularFlu.y, -angularFlu.z),
                    },
                    covariance = new double[36],
                },
            };
            ros.Publish(odomTopic, odom);

            if (!publishTf) return;
            var tf = new TransformStampedMsg
            {
                header = header,
                child_frame_id = baseFrame,
                transform = new TransformMsg { translation = position.To<FLU>(), rotation = rotation.To<FLU>() },
            };
            ros.Publish(tfTopic, new TFMessageMsg { transforms = new[] { tf } });
        }

        TimeMsg Now()
        {
            double s = useSimTime ? Time.timeAsDouble : (System.DateTime.UtcNow - System.DateTime.UnixEpoch).TotalSeconds;
            uint nanosec = (uint)((s - System.Math.Floor(s)) * 1e9);
#if ROS2
            return new TimeMsg((int)s, nanosec);
#else
            return new TimeMsg((uint)s, nanosec);
#endif
        }
    }
}
