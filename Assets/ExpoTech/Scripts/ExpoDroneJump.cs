using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ExpoTech
{
    /// <summary>
    /// Pulo físico do drone: aplica a mesma variação de velocidade vertical em todos os corpos da
    /// articulação (impulso), calculada para atingir jumpHeight. Comandos: /expo_drone/jump
    /// (std_msgs/Empty), barra de espaço ou RequestJump() (usado pelo assistente de desvio).
    /// Publica /expo_drone/grounded (std_msgs/Bool).
    /// </summary>
    public class ExpoDroneJump : MonoBehaviour
    {
        [Header("ROS 2")]
        public bool useRos = true;
        public string jumpTopic = "/expo_drone/jump";
        public string groundedTopic = "/expo_drone/grounded";
        public float groundedRateHz = 10f;

        [Header("Pulo")]
        [Tooltip("Altura que o centro de massa sobe (m)")] public float jumpHeight = 1.5f;
        [Tooltip("Intervalo mínimo entre pulos (s)")] public float cooldown = 0.6f;
        [Tooltip("Pedido de pulo continua valendo por esse tempo, caso o drone ainda não esteja no chão (s)")]
        public float jumpBufferTime = 0.15f;
        public bool keyboardJump = true;
        [Tooltip("1 = física pura. Acima de 1 a queda fica mais rápida (sensação de peso do DroneJump original)")]
        public float fallMultiplier = 1f;

        [Header("Detecção de chão")]
        public ArticulationBody[] wheels;
        public float wheelRadius = 0.55f;
        public float groundTolerance = 0.08f;
        public LayerMask groundMask = ~0;

        public bool IsGrounded { get; private set; }
        public int JumpCount { get; private set; }
        public float ExpectedApexSpeed => Mathf.Sqrt(2f * Mathf.Abs(Physics.gravity.y) * jumpHeight);

        ArticulationBody[] bodies;
        ArticulationBody root;
        ROSConnection ros;
        float lastJump = -10f, requestedAt = -10f, publishElapsed;
        readonly RaycastHit[] hits = new RaycastHit[16];

        void Start()
        {
            bodies = GetComponentsInChildren<ArticulationBody>();
            foreach (var b in bodies) if (b.isRoot) root = b;
            if (!useRos) return;
            ros = ROSConnection.GetOrCreateInstance();
            ros.Subscribe<EmptyMsg>(jumpTopic, _ => RequestJump());
            ros.RegisterPublisher<BoolMsg>(groundedTopic);
        }

        /// <summary>Pede um pulo; acontece no próximo passo de física em que o drone estiver no chão.</summary>
        public void RequestJump() => requestedAt = Time.time;

        void Update()
        {
            if (!keyboardJump) return;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            var g = Gamepad.current;
            if ((k != null && k.spaceKey.wasPressedThisFrame) || (g != null && g.buttonSouth.wasPressedThisFrame)) RequestJump();
#else
            if (Input.GetButtonDown("Jump")) RequestJump();
#endif
        }

        void FixedUpdate()
        {
            IsGrounded = CheckGround();
            bool wants = Time.time - requestedAt <= jumpBufferTime;
            if (wants && IsGrounded && Time.time - lastJump >= cooldown) Jump();

            if (fallMultiplier > 1f && !IsGrounded && root.linearVelocity.y < 0f)
                foreach (var b in bodies)
                    b.AddForce(Physics.gravity * (fallMultiplier - 1f), ForceMode.Acceleration);

            if (!useRos) return;
            publishElapsed += Time.fixedDeltaTime;
            if (publishElapsed >= 1f / groundedRateHz)
            {
                publishElapsed = 0f;
                ros.Publish(groundedTopic, new BoolMsg(IsGrounded));
            }
        }

        void Jump()
        {
            // v = raiz(2 g h); soma só o que falta para não "empilhar" pulos.
            float dv = ExpectedApexSpeed - Mathf.Max(0f, root.linearVelocity.y);
            foreach (var b in bodies)
                b.AddForce(Vector3.up * dv, ForceMode.VelocityChange);
            lastJump = Time.time;
            requestedAt = -10f;
            JumpCount++;
        }

        bool CheckGround()
        {
            foreach (var wheel in wheels)
            {
                if (wheel == null) continue;
                int n = Physics.RaycastNonAlloc(wheel.transform.position, Vector3.down, hits,
                    wheelRadius + groundTolerance, groundMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                    if (!hits[i].collider.transform.IsChildOf(transform)) return true;
            }
            return false;
        }
    }
}
