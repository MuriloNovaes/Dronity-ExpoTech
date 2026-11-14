using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Controle simples do Drone (tracao diferencial, 2 rodas).
/// Coloque este script no objeto raiz do Drone.
/// WASD / setas: andar e girar. Se houver um DroneProximitySensor no mesmo objeto,
/// ele corrige a entrada para desviar de obstaculos automaticamente.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DroneController : MonoBehaviour
{
    [Header("Rodas (arraste Wheel_L e Wheel_R)")]
    public Transform wheelA;
    public Transform wheelB;

    [Header("Movimento")]
    public float maxSpeed = 6f;          // m/s
    public float acceleration = 12f;     // m/s^2
    public float turnSpeed = 120f;       // graus/s
    public float wheelRadius = 0.56f;    // raio externo da roda (m)

    [Header("Ajustes")]
    public bool invertSpin = false;      // marque se as rodas girarem ao contrario
    public bool invertDirection = false; // marque se o drone andar para tras ao apertar W

    Rigidbody rb;
    Transform leftWheel, rightWheel;
    float currentSpeed;
    DroneProximitySensor sensor;

    // direcoes em coordenadas locais do objeto raiz
    Vector3 forwardLocal = Vector3.forward;  // para onde o drone anda
    Vector3 rollAxisLocal = Vector3.right;   // eixo em volta do qual as rodas giram

    // --- informacoes para outros scripts (sensor, camera...) ---
    public float CurrentSpeed => currentSpeed;
    public Vector3 RollAxisWorld => transform.TransformDirection(rollAxisLocal);
    public Vector3 DriveForward
    {
        get
        {
            Vector3 f = transform.TransformDirection(forwardLocal);
            return invertDirection ? -f : f;
        }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        sensor = GetComponent<DroneProximitySensor>();

        DetectAxle();

        // cria um collider simples se nao houver nenhum
        if (GetComponentInChildren<Collider>() == null)
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                var bc = gameObject.AddComponent<BoxCollider>();
                bc.center = transform.InverseTransformPoint(b.center);
                bc.size = b.size;
            }
        }
    }

    // O eixo das rodas e a linha que liga uma roda a outra.
    // O drone anda perpendicular a esse eixo, como um carro.
    void DetectAxle()
    {
        leftWheel = wheelA;
        rightWheel = wheelB;
        if (!wheelA || !wheelB) return;

        Vector3 axle = transform.InverseTransformPoint(wheelB.position) -
                       transform.InverseTransformPoint(wheelA.position);
        axle.y = 0f;
        if (axle.sqrMagnitude < 0.0001f) return;
        axle.Normalize();

        Vector3 f = Vector3.Cross(axle, Vector3.up);
        float pref = Mathf.Abs(f.z) > 0.1f ? f.z : f.x;
        if (pref < 0f) f = -f;

        forwardLocal = f.normalized;
        rollAxisLocal = Vector3.Cross(Vector3.up, forwardLocal).normalized;  // aponta para a direita

        if (Vector3.Dot(transform.InverseTransformPoint(wheelA.position), rollAxisLocal) >
            Vector3.Dot(transform.InverseTransformPoint(wheelB.position), rollAxisLocal))
        {
            leftWheel = wheelB;
            rightWheel = wheelA;
        }
    }

    Vector2 ReadInput()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k == null) return Vector2.zero;
        float x = ((k.dKey.isPressed || k.rightArrowKey.isPressed) ? 1f : 0f)
                - ((k.aKey.isPressed || k.leftArrowKey.isPressed) ? 1f : 0f);
        float y = ((k.wKey.isPressed || k.upArrowKey.isPressed) ? 1f : 0f)
                - ((k.sKey.isPressed || k.downArrowKey.isPressed) ? 1f : 0f);
        return new Vector2(x, y);
#else
        return new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
#endif
    }

    void FixedUpdate()
    {
        Vector2 input = ReadInput();

        // o sensor pode corrigir a direcao e a velocidade para desviar de obstaculos
        if (sensor != null && sensor.enabled)
            input = sensor.Filter(input, currentSpeed);

        float h = input.x;
        float v = input.y;

        currentSpeed = Mathf.MoveTowards(currentSpeed, v * maxSpeed, acceleration * Time.fixedDeltaTime);

#if UNITY_6000_0_OR_NEWER
        Vector3 vel = DriveForward * currentSpeed;
        vel.y = rb.linearVelocity.y;
        rb.linearVelocity = vel;
#else
        Vector3 vel = DriveForward * currentSpeed;
        vel.y = rb.velocity.y;
        rb.velocity = vel;
#endif
        rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, h * turnSpeed * Time.fixedDeltaTime, 0f));

        SpinWheels(h);
    }

    void SpinWheels(float turn)
    {
        float sign = (invertSpin ? -1f : 1f) * (invertDirection ? -1f : 1f);
        float leftLinear  = currentSpeed + turn * 2f;
        float rightLinear = currentSpeed - turn * 2f;
        float k = Time.fixedDeltaTime * Mathf.Rad2Deg / wheelRadius;

        Vector3 axisWorld = RollAxisWorld;

        if (leftWheel)  leftWheel.Rotate(axisWorld,  sign * leftLinear  * k, Space.World);
        if (rightWheel) rightWheel.Rotate(axisWorld, sign * rightLinear * k, Space.World);
    }
}
