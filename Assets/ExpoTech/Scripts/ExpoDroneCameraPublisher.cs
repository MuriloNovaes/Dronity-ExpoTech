// Gerado pelo Unity Robotics MCP. Pode editar à vontade.
using System.Collections.Generic;
using RosMessageTypes.BuiltinInterfaces;
using RosMessageTypes.Geometry;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using Unity.Robotics.UrdfImporter;
using UnityEngine;

public class ExpoDroneCameraPublisher : MonoBehaviour
{
    public string topic = "/expo_drone/camera/image/compressed";
    public Camera sensorCamera;
    public int width = 640;
    public int height = 480;
    public float publishRateHz = 10.0f;
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
}
