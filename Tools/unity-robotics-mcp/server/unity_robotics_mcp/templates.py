"""Templates C# de scripts de runtime para robôs com ROS-TCP-Connector.

Os scripts vão para Assets/ do projeto e compilam no Assembly-CSharp, que já
referencia os pacotes ROS-TCP-Connector e URDF Importer quando instalados.
"""

from __future__ import annotations

_HEADER = """// Gerado pelo Unity Robotics MCP. Pode editar à vontade.
using System.Collections.Generic;
using RosMessageTypes.BuiltinInterfaces;
using RosMessageTypes.Geometry;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.UrdfImporter;
using UnityEngine;
"""

_STAMP = """
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
"""

_JOINT_NAME = """
    static string JointName(ArticulationBody body)
    {
        var urdf = body.GetComponent<UrdfJoint>();
        return urdf != null && !string.IsNullOrEmpty(urdf.jointName) ? urdf.jointName : body.name;
    }
"""

TEMPLATES: dict[str, dict] = {
    "joint_state_publisher": {
        "description": "Publica sensor_msgs/JointState de todas as juntas móveis do robô (posição rad/m, velocidade, esforço).",
        "defaults": {"topic": "/joint_states", "rate_hz": 30},
        "code": _HEADER + """
public class __CLASS__ : MonoBehaviour
{
    public string topic = "__TOPIC__";
    public float publishRateHz = __RATE__f;
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
""" + _STAMP + _JOINT_NAME + "}\n",
    },
    "joint_command_subscriber": {
        "description": "Assina sensor_msgs/JointState e usa 'position' como alvo dos drives (rad p/ revolute, m p/ prismatic), casando por nome de junta.",
        "defaults": {"topic": "/joint_command", "rate_hz": 0},
        "code": _HEADER + """
public class __CLASS__ : MonoBehaviour
{
    public string topic = "__TOPIC__";
    [Tooltip("Raiz do robô; vazio = este GameObject")]
    public GameObject robotRoot;

    readonly Dictionary<string, ArticulationBody> byName = new Dictionary<string, ArticulationBody>();

    void Start()
    {
        var root = robotRoot != null ? robotRoot : gameObject;
        foreach (var body in root.GetComponentsInChildren<ArticulationBody>())
            if (!body.isRoot && body.jointType != ArticulationJointType.FixedJoint)
                byName[JointName(body)] = body;
        ROSConnection.GetOrCreateInstance().Subscribe<JointStateMsg>(topic, OnCommand);
    }

    void OnCommand(JointStateMsg msg)
    {
        for (int i = 0; i < msg.name.Length && i < msg.position.Length; i++)
        {
            if (!byName.TryGetValue(msg.name[i], out var body)) continue;
            var drive = body.xDrive;
            // ArticulationDrive.target é em graus para juntas rotacionais e metros para prismáticas.
            drive.target = body.jointType == ArticulationJointType.PrismaticJoint
                ? (float)msg.position[i]
                : (float)msg.position[i] * Mathf.Rad2Deg;
            body.xDrive = drive;
        }
    }
""" + _JOINT_NAME + "}\n",
    },
    "diff_drive_cmd_vel": {
        "description": "Base diferencial: assina geometry_msgs/Twist (/cmd_vel) e comanda velocidade das rodas esquerda/direita.",
        "defaults": {"topic": "/cmd_vel", "rate_hz": 0},
        "code": _HEADER + """
public class __CLASS__ : MonoBehaviour
{
    public string topic = "__TOPIC__";
    public ArticulationBody leftWheel;
    public ArticulationBody rightWheel;
    [Tooltip("Raio da roda (m)")] public float wheelRadius = 0.05f;
    [Tooltip("Distância entre as rodas (m)")] public float trackWidth = 0.3f;
    [Tooltip("Inverta se a roda gira ao contrário (depende do eixo no URDF)")]
    public bool invertLeft, invertRight;
    [Tooltip("Para o robô se não chegar comando nesse tempo (s)")]
    public float commandTimeout = 0.5f;
    public float wheelDamping = 100f;
    public float wheelForceLimit = 50f;

    float linear, angular, lastCommand;

    void Start()
    {
        foreach (var wheel in new[] { leftWheel, rightWheel })
        {
            if (wheel == null) continue;
            var d = wheel.xDrive;
            d.stiffness = 0f;            // controle por velocidade
            d.damping = wheelDamping;
            d.forceLimit = wheelForceLimit;
            wheel.xDrive = d;
        }
        ROSConnection.GetOrCreateInstance().Subscribe<TwistMsg>(topic, OnCmdVel);
    }

    void OnCmdVel(TwistMsg msg)
    {
        // Em ROS: linear.x = frente (m/s), angular.z = giro (rad/s, anti-horário).
        linear = (float)msg.linear.x;
        angular = (float)msg.angular.z;
        lastCommand = Time.time;
    }

    void FixedUpdate()
    {
        if (Time.time - lastCommand > commandTimeout) linear = angular = 0f;
        float left = (linear - angular * trackWidth * 0.5f) / wheelRadius;
        float right = (linear + angular * trackWidth * 0.5f) / wheelRadius;
        SetVelocity(leftWheel, left * (invertLeft ? -1f : 1f));
        SetVelocity(rightWheel, right * (invertRight ? -1f : 1f));
    }

    static void SetVelocity(ArticulationBody wheel, float radPerSec)
    {
        if (wheel == null) return;
        var d = wheel.xDrive;
        d.targetVelocity = radPerSec * Mathf.Rad2Deg; // drive usa graus/s
        wheel.xDrive = d;
    }
}
""",
    },
    "camera_publisher": {
        "description": "Publica sensor_msgs/CompressedImage (JPEG) de uma Camera do Unity.",
        "defaults": {"topic": "/camera/image/compressed", "rate_hz": 10},
        "code": _HEADER + """
public class __CLASS__ : MonoBehaviour
{
    public string topic = "__TOPIC__";
    public Camera sensorCamera;
    public int width = 640;
    public int height = 480;
    public float publishRateHz = __RATE__f;
    [Range(10, 100)] public int jpegQuality = 75;
    public string frameId = "camera_link";

    ROSConnection ros;
    RenderTexture target;
    Texture2D readback;
    float elapsed;

    void Start()
    {
        if (sensorCamera == null) sensorCamera = GetComponent<Camera>();
        target = new RenderTexture(width, height, 24);
        readback = new Texture2D(width, height, TextureFormat.RGB24, false);
        ros = ROSConnection.GetOrCreateInstance();
        ros.RegisterPublisher<CompressedImageMsg>(topic);
    }

    void Update()
    {
        elapsed += Time.deltaTime;
        if (elapsed < 1f / publishRateHz || sensorCamera == null) return;
        elapsed = 0f;

        var previous = sensorCamera.targetTexture;
        sensorCamera.targetTexture = target;
        sensorCamera.Render();
        RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        readback.Apply();
        sensorCamera.targetTexture = previous;
        RenderTexture.active = null;

        ros.Publish(topic, new CompressedImageMsg
        {
            header = new HeaderMsg { frame_id = frameId, stamp = Now() },
            format = "jpeg",
            data = readback.EncodeToJPG(jpegQuality),
        });
    }

    void OnDestroy()
    {
        if (target != null) target.Release();
    }
""" + _STAMP + "}\n",
    },
}


def render_template(template: str, class_name: str, topic: str | None, rate_hz: float | None) -> str:
    if template not in TEMPLATES:
        raise ValueError(f"Template desconhecido '{template}'. Opções: {sorted(TEMPLATES)}")
    spec = TEMPLATES[template]
    defaults = spec["defaults"]
    return (
        spec["code"]
        .replace("__CLASS__", class_name)
        .replace("__TOPIC__", topic or defaults["topic"])
        .replace("__RATE__", str(rate_hz if rate_hz is not None else defaults["rate_hz"]))
    )
