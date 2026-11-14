using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ExpoTech
{
    /// <summary>
    /// Reposiciona o drone no ponto de partida, parado e em pé (útil na demonstração se ele ficar preso).
    /// Tecla R, botão do painel ou /expo_drone/reset (std_msgs/Empty).
    /// </summary>
    public class ExpoDroneReset : MonoBehaviour
    {
        public bool useRos = true;
        public string resetTopic = "/expo_drone/reset";

        ArticulationBody root;
        ArticulationBody[] bodies;
        Vector3 startPosition;
        Quaternion startRotation;
        bool pending;

        void Start()
        {
            bodies = GetComponentsInChildren<ArticulationBody>();
            foreach (var b in bodies) if (b.isRoot) root = b;
            startPosition = root.transform.position;
            startRotation = root.transform.rotation;
            if (useRos)
                ROSConnection.GetOrCreateInstance().Subscribe<EmptyMsg>(resetTopic, _ => pending = true);
        }

        /// <summary>Pede o reposicionamento (feito no próximo passo de física).</summary>
        public void ResetPose() => pending = true;

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null && k.rKey.wasPressedThisFrame) pending = true;
#else
            if (Input.GetKeyDown(KeyCode.R)) pending = true;
#endif
        }

        void FixedUpdate()
        {
            if (!pending) return;
            pending = false;
            var autopilot = GetComponent<ExpoDroneAutopilot>();
            if (autopilot != null && autopilot.active) autopilot.SetActive(true); // zera o estado de "preso"
            root.TeleportRoot(startPosition, startRotation);
            foreach (var b in bodies)
            {
                b.linearVelocity = Vector3.zero;
                b.angularVelocity = Vector3.zero;
                if (!b.isRoot && b.dofCount > 0)
                {
                    b.jointVelocity = new ArticulationReducedSpace(0f);
                    b.jointPosition = new ArticulationReducedSpace(0f);
                }
            }
        }
    }
}
