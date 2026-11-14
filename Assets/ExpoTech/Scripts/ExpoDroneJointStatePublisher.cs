// Gerado pelo Unity Robotics MCP. Pode editar à vontade.
using System.Collections.Generic;
using RosMessageTypes.BuiltinInterfaces;
using RosMessageTypes.Geometry;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.UrdfImporter;
using UnityEngine;

public class ExpoDroneJointStatePublisher : MonoBehaviour
{
    public string topic = "/joint_states";
    public float publishRateHz = 30.0f;
    [Tooltip("Raiz do robô; vazio = este GameObject")]
    public GameObject robotRoot;
    public string frameId = "";

    ROSConnection ros;
    ArticulationBody[] joints;
    string[] names;
    float elapsed;

    void Start()
    {
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<JointStateMsg>(topic);
        var root = robotRoot != null ? robotRoot : gameObject;
        var list = new List<ArticulationBody>();
        foreach (var body in root.GetComponentsInChildren<ArticulationBody>())
            if (!body.isRoot && body.jointType != ArticulationJointType.FixedJoint)
                list.Add(body);
        joints = list.ToArray();
        names = new string[joints.Length];
        for (int i = 0; i < joints.Length; i++)
            names[i] = JointName(joints[i]);
    }

    void FixedUpdate()
    {
        elapsed += Time.fixedDeltaTime;
        if (elapsed < 1f / publishRateHz) return;
        elapsed = 0f;

        int n = joints.Length;
        var msg = new JointStateMsg
        {
            header = new HeaderMsg { frame_id = frameId, stamp = Now() },
            name = names,
            position = new double[n],
            velocity = new double[n],
            effort = new double[n],
        };
        for (int i = 0; i < n; i++)
        {
            if (joints[i].dofCount == 0) continue;
            msg.position[i] = joints[i].jointPosition[0];
            msg.velocity[i] = joints[i].jointVelocity[0];
            msg.effort[i] = joints[i].jointForce[0];
        }
        ros.Publish(topic, msg);
    }

    [Tooltip("true = tempo da simulação (use_sim_time no ROS); false = relógio do sistema")]
    public bool useSimTime = false;

    TimeMsg Now()
    {
        double t = useSimTime ? Time.timeAsDouble : (System.DateTime.UtcNow - System.DateTime.UnixEpoch).TotalSeconds;
        uint nanosec = (uint)((t - System.Math.Floor(t)) * 1e9);
#if ROS2
        return new TimeMsg((int)t, nanosec);
#else
        return new TimeMsg((uint)t, nanosec);
#endif
    }

    static string JointName(ArticulationBody body)
    {
        var urdf = body.GetComponent<UrdfJoint>();
        return urdf != null && !string.IsNullOrEmpty(urdf.jointName) ? urdf.jointName : body.name;
    }
}
