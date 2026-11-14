using RosMessageTypes.Geometry;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ExpoTech
{
    /// <summary>
    /// Tração diferencial do drone. Fonte do comando, por prioridade:
    /// 1) override de teste, 2) /cmd_vel (geometry_msgs/Twist) do ROS 2, 3) teclado WASD/setas.
    /// </summary>
    public class ExpoDroneDriveController : MonoBehaviour
    {
        [Header("ROS 2")]
        public string cmdVelTopic = "/cmd_vel";
        public bool subscribeToRos = true;
        [Tooltip("Sem comando ROS nesse tempo (s), o robô para ou volta para o teclado")]
        public float commandTimeout = 0.5f;

        [Header("Rodas (do URDF)")]
        public ArticulationBody leftWheel;
        public ArticulationBody rightWheel;
        [Tooltip("Raio da roda (m) — igual ao URDF")] public float wheelRadius = 0.05f;
        [Tooltip("Distância entre rodas (m) — igual ao URDF")] public float trackWidth = 0.17f;
        public bool invertLeft;
        public bool invertRight;
        public float wheelDamping = 10f;
        [Tooltip("Torque máximo por roda (N·m) — effort do URDF")] public float wheelForceLimit = 2f;

        [Header("Limites")]
        public float maxLinear = 1.0f;
        public float maxAngular = 3.0f;
        [Tooltip("Aceleração máxima (m/s²); 0 = sem rampa")] public float maxLinearAcceleration = 1.0f;
        [Tooltip("Frenagem máxima (m/s²); 0 = igual à aceleração")] public float maxLinearDeceleration = 2.0f;
        [Tooltip("Aceleração angular máxima (rad/s²); 0 = sem rampa")] public float maxAngularAcceleration = 2.0f;

        [Header("Teclado (fallback para demo)")]
        public bool keyboardTeleop = true;
        public float keyboardLinear = 0.5f;
        public float keyboardAngular = 2.0f;

        [Header("Teste")]
        public bool overrideCommand;
        public float overrideLinear;
        public float overrideAngular;

        public float CommandLinear { get; private set; }
        public float CommandAngular { get; private set; }
        public string CommandSource { get; private set; } = "none";

        float rosLinear, rosAngular, lastRosCommand = -999f;
        ExpoDroneSmartAvoidance avoidance;
        ExpoDroneAutopilot autopilot;

        void Start()
        {
            avoidance = GetComponent<ExpoDroneSmartAvoidance>();
            autopilot = GetComponent<ExpoDroneAutopilot>();
            foreach (var wheel in new[] { leftWheel, rightWheel })
            {
                if (wheel == null) continue;
                var d = wheel.xDrive;
                d.stiffness = 0f; // controle por velocidade
                d.damping = wheelDamping;
                d.forceLimit = wheelForceLimit;
                wheel.xDrive = d;
            }
            if (subscribeToRos)
                ROSConnection.GetOrCreateInstance().Subscribe<TwistMsg>(cmdVelTopic, OnCmdVel);
        }

        void OnCmdVel(TwistMsg msg)
        {
            // ROS: linear.x = frente (m/s), angular.z = giro anti-horário visto de cima (rad/s).
            rosLinear = (float)msg.linear.x;
            rosAngular = (float)msg.angular.z;
            lastRosCommand = Time.time;
        }

        void FixedUpdate()
        {
            float linear = 0f, angular = 0f;
            if (overrideCommand)
            {
                linear = overrideLinear; angular = overrideAngular; CommandSource = "override";
            }
            else if (Time.time - lastRosCommand <= commandTimeout)
            {
                linear = rosLinear; angular = rosAngular; CommandSource = "ros";
            }
            else if (keyboardTeleop && ReadKeyboard(out linear, out angular))
            {
                // Tecla/analógico pressionado assume o controle, mesmo no modo autônomo.
                CommandSource = "manual";
            }
            else if (autopilot != null && autopilot.TryGetCommand(out linear, out angular))
            {
                CommandSource = "autonomo";
            }
            else CommandSource = "none";

            // O assistente de desvio (se houver) corrige o comando: pula, desacelera ou desvia.
            if (avoidance != null && avoidance.isActiveAndEnabled)
                avoidance.Filter(ref linear, ref angular);

            linear = Mathf.Clamp(linear, -maxLinear, maxLinear);
            angular = Mathf.Clamp(angular, -maxAngular, maxAngular);
            // Rampa: o torque de arrancada vira reação no corpo pendurado no eixo; sem ela o corpo gira.
            float dt = Time.fixedDeltaTime;
            // Acelerar = afastar de zero no mesmo sentido; o resto (frear, inverter) usa a frenagem.
            bool speedingUp = Mathf.Abs(linear) > Mathf.Abs(CommandLinear) && linear * CommandLinear >= 0f;
            float rate = speedingUp || maxLinearDeceleration <= 0f ? maxLinearAcceleration : maxLinearDeceleration;
            CommandLinear = rate > 0f ? Mathf.MoveTowards(CommandLinear, linear, rate * dt) : linear;
            CommandAngular = maxAngularAcceleration > 0f ? Mathf.MoveTowards(CommandAngular, angular, maxAngularAcceleration * dt) : angular;

            float left = (CommandLinear - CommandAngular * trackWidth * 0.5f) / wheelRadius;
            float right = (CommandLinear + CommandAngular * trackWidth * 0.5f) / wheelRadius;
            SetWheelVelocity(leftWheel, left * (invertLeft ? -1f : 1f));
            SetWheelVelocity(rightWheel, right * (invertRight ? -1f : 1f));
        }

        bool ReadKeyboard(out float linear, out float angular)
        {
            linear = angular = 0f;
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null)
            {
                if (k.wKey.isPressed || k.upArrowKey.isPressed) linear += keyboardLinear;
                if (k.sKey.isPressed || k.downArrowKey.isPressed) linear -= keyboardLinear;
                if (k.aKey.isPressed || k.leftArrowKey.isPressed) angular += keyboardAngular;
                if (k.dKey.isPressed || k.rightArrowKey.isPressed) angular -= keyboardAngular;
            }
            // Controle (Xbox/PlayStation): analógico esquerdo, proporcional, com zona morta.
            var g = Gamepad.current;
            if (g != null)
            {
                Vector2 stick = g.leftStick.ReadValue();
                if (stick.magnitude > 0.15f)
                {
                    linear += stick.y * keyboardLinear;
                    angular -= stick.x * keyboardAngular;
                }
            }
#else
            linear = Input.GetAxis("Vertical") * keyboardLinear;
            angular = -Input.GetAxis("Horizontal") * keyboardAngular;
#endif
            return linear != 0f || angular != 0f;
        }

        static void SetWheelVelocity(ArticulationBody wheel, float radPerSec)
        {
            if (wheel == null) return;
            var d = wheel.xDrive;
            d.targetVelocity = radPerSec * Mathf.Rad2Deg; // ArticulationDrive usa graus/s
            wheel.xDrive = d;
        }
    }
}
