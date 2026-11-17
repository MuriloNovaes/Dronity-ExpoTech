using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ExpoTech
{
    /// <summary>
    /// Modo autônomo ("andar sozinho"): explora o ambiente seguindo o caminho mais livre visto pelo LiDAR.
    /// Obstáculos baixos (abaixo do LiDAR) ficam com o assistente de desvio, que pula ou desvia.
    /// Se ficar preso, dá ré girando e tenta de novo.
    /// Liga/desliga: botão do painel, tecla M ou /expo_drone/autopilot_enable (std_msgs/Bool).
    /// Publica o estado em /expo_drone/autopilot_state (std_msgs/String).
    /// </summary>
    public class ExpoDroneAutopilot : MonoBehaviour
    {
        public bool active;

        [Header("ROS 2")]
        public bool useRos = true;
        public string enableTopic = "/expo_drone/autopilot_enable";
        public string stateTopic = "/expo_drone/autopilot_state";

        [Header("Referências")]
        public ExpoDroneLidar lidar;
        [Tooltip("base_link")] public ArticulationBody baseBody;

        [Header("Exploração")]
        [Tooltip("Velocidade de cruzeiro (m/s)")] public float cruiseSpeed = 2.0f;
        [Tooltip("Velocidade mínima quando há obstáculo perto (m/s)")] public float minSpeed = 0.6f;
        [Tooltip("Giro máximo (rad/s)")] public float maxTurn = 1.2f;
        public float steerGain = 1.5f;
        [Tooltip("Abertura da busca de caminho, para cada lado (graus)")] public float searchAngle = 75f;
        [Tooltip("A partir dessa distância livre o caminho é considerado aberto (m)")] public float clearDistance = 6f;
        [Tooltip("Meia largura do drone, para o caminho caber nele (m)")] public float halfWidth = 1.4f;
        [Tooltip("Altura dos raios baixos acima do chão (m): enxergam o que fica abaixo do LiDAR")] public float lowSensorHeight = 0.4f;
        [Tooltip("Altura do eixo das rodas acima do chão (m) = raio da roda")] public float axleHeight = 0.55f;

        [Header("Recuperação quando preso")]
        public float stuckTime = 2.5f;
        public float reverseTime = 3f;

        [Header("Progresso (anti-vaivém)")]
        [Tooltip("Janela para medir o avanço (s)")] public float progressWindow = 8f;
        [Tooltip("Avanço mínimo na janela; abaixo disso escolhe nova direção (m)")] public float minProgress = 1.5f;
        [Tooltip("Tempo seguindo a nova direção antes de voltar a explorar livremente (s)")] public float commitTime = 4f;
        [Tooltip("Penalidade por trocar de lado (histerese)")] public float switchPenalty = 0.4f;

        public string State { get; private set; } = "DESLIGADO";

        float stuckTimer, reverseTimer, turnDir = 1f, smoothedTurn, publishElapsed;
        float lastAngle, commitTimer, sampleTimer;
        bool reorienting;
        Vector3 targetDir;
        readonly RaycastHit[] lowHits = new RaycastHit[16];
        readonly System.Collections.Generic.Queue<Vector3> history = new System.Collections.Generic.Queue<Vector3>();
        string lastPublished;
        ROSConnection ros;

        ExpoDroneSmartAvoidance avoidance;

        void Start()
        {
            avoidance = GetComponent<ExpoDroneSmartAvoidance>();
            if (!useRos) return;
            ros = ROSConnection.GetOrCreateInstance();
            ros.Subscribe<BoolMsg>(enableTopic, msg => SetActive(msg.data));
            ros.RegisterPublisher<StringMsg>(stateTopic);
        }

        public void SetActive(bool on)
        {
            active = on;
            stuckTimer = reverseTimer = commitTimer = 0f;
            reorienting = false;
            history.Clear();
            State = on ? "EXPLORANDO" : "DESLIGADO";
        }

        public void Toggle() => SetActive(!active);

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            var g = Gamepad.current;
            if ((k != null && k.mKey.wasPressedThisFrame) || (g != null && g.startButton.wasPressedThisFrame)) Toggle();
#else
            if (Input.GetKeyDown(KeyCode.M)) Toggle();
#endif
            if (!useRos) return;
            publishElapsed += Time.deltaTime;
            if (State != lastPublished || publishElapsed >= 1f)
            {
                ros.Publish(stateTopic, new StringMsg(State));
                lastPublished = State;
                publishElapsed = 0f;
            }
        }

        /// <summary>Comando do modo autônomo (unidades ROS: m/s e rad/s, positivo = esquerda).</summary>
        public bool TryGetCommand(out float linear, out float angular)
        {
            linear = angular = 0f;
            if (!active || lidar == null || lidar.Ranges == null || lidar.Ranges.Length == 0) return false;
            float dt = Time.fixedDeltaTime;

            // Preso: primeiro dá ré RETO (girar num vão estreito bate nas laterais);
            // depois gira no lugar, só se houver espaço para o drone inteiro girar.
            if (reverseTimer > 0f)
            {
                reverseTimer -= dt;
                bool firstHalf = reverseTimer > reverseTime * 0.5f;
                bool canSpin = avoidance == null || avoidance.CanSpin();
                if (firstHalf || !canSpin)
                {
                    linear = -0.8f;
                    angular = 0f;
                    State = "RECUANDO";
                }
                else
                {
                    linear = 0f;
                    angular = turnDir * maxTurn;
                    State = "GIRANDO";
                }
                return true;
            }

            Vector3 fwd = baseBody.transform.forward;
            float speed = Vector3.Dot(baseBody.GetPointVelocity(baseBody.transform.position), fwd);

            // Anti-vaivém: sem avançar minProgress em progressWindow, escolhe a direção mais aberta (360°).
            TrackProgress(dt);
            if (reorienting)
            {
                float err = AngleTo(targetDir);
                if (Mathf.Abs(err) < 15f * Mathf.Deg2Rad)
                {
                    reorienting = false;
                    commitTimer = commitTime;
                }
                else
                {
                    bool canSpin = avoidance == null || avoidance.CanSpin();
                    linear = canSpin ? 0f : -0.6f;
                    angular = canSpin ? Mathf.Sign(err) * maxTurn : 0f;
                    State = canSpin ? "NOVA DIRECAO" : "RECUANDO P/ GIRAR";
                    return true;
                }
            }
            float preferred = 0f;
            if (commitTimer > 0f)
            {
                commitTimer -= dt;
                preferred = Mathf.Clamp(AngleTo(targetDir), -searchAngle * Mathf.Deg2Rad, searchAngle * Mathf.Deg2Rad);
            }

            float bestAngle = 0f, bestScore = float.NegativeInfinity, frontClear = 0f;
            for (float deg = -searchAngle; deg <= searchAngle; deg += 5f)
            {
                float rad = deg * Mathf.Deg2Rad;
                float free = FreeDistance(rad);
                if (Mathf.Abs(deg) < 0.1f) frontClear = free;
                // Prefere caminhos livres, perto da direção preferida, sem trocar de lado à toa.
                float score = Mathf.Min(free, clearDistance) - 0.6f * Mathf.Abs(rad - preferred);
                if (Mathf.Sign(rad) != Mathf.Sign(lastAngle) && Mathf.Abs(rad) > 0.1f) score -= switchPenalty;
                if (score > bestScore) { bestScore = score; bestAngle = rad; }
            }
            lastAngle = bestAngle;

            // Pouca distância livre em qualquer direção -> trata como preso.
            bool blocked = bestScore < 1.5f;
            stuckTimer = (speed < 0.15f || blocked) ? stuckTimer + dt : 0f;
            if (stuckTimer > stuckTime)
            {
                stuckTimer = 0f;
                reverseTimer = reverseTime;
                turnDir = bestAngle >= 0f ? 1f : -1f;
            }

            smoothedTurn = Mathf.Lerp(smoothedTurn, Mathf.Clamp(steerGain * bestAngle, -maxTurn, maxTurn), 0.1f);
            angular = smoothedTurn;
            linear = Mathf.Lerp(minSpeed, cruiseSpeed, Mathf.Clamp01(frontClear / clearDistance))
                     * Mathf.Lerp(1f, 0.5f, Mathf.Abs(bestAngle) / (searchAngle * Mathf.Deg2Rad));
            State = commitTimer > 0f ? "SEGUINDO NOVA DIRECAO"
                : Mathf.Abs(bestAngle) > 0.2f ? (bestAngle > 0f ? "<< CONTORNANDO" : "CONTORNANDO >>") : "EXPLORANDO";
            return true;
        }

        void TrackProgress(float dt)
        {
            sampleTimer += dt;
            if (sampleTimer < 0.5f) return;
            sampleTimer = 0f;
            Vector3 p = baseBody.transform.position;
            history.Enqueue(p);
            int window = Mathf.CeilToInt(progressWindow / 0.5f);
            if (history.Count <= window) return;
            Vector3 old = history.Dequeue();
            if (reorienting || commitTimer > 0f) return;
            if (Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(old.x, 0f, old.z)) >= minProgress) return;

            // Direção mais aberta em 360°, evitando a frente atual (onde ficou indo e voltando).
            float bestFree = -1f, bestRad = Mathf.PI;
            for (float deg = -180f; deg < 180f; deg += 10f)
            {
                if (Mathf.Abs(deg) < 40f) continue;
                float rad = deg * Mathf.Deg2Rad;
                float free = Mathf.Min(FreeDistance(rad), 15f);
                if (free > bestFree) { bestFree = free; bestRad = rad; }
            }
            Vector3 fwd = baseBody.transform.forward;
            fwd.y = 0f;
            targetDir = Quaternion.AngleAxis(-bestRad * Mathf.Rad2Deg, Vector3.up) * fwd.normalized;
            reorienting = true;
            history.Clear();
        }

        /// <summary>Ângulo (rad, convenção ROS: positivo = esquerda) da frente do drone até a direção dada.</summary>
        float AngleTo(Vector3 worldDir)
        {
            Vector3 fwd = baseBody.transform.forward;
            fwd.y = 0f;
            return -Vector3.SignedAngle(fwd, worldDir, Vector3.up) * Mathf.Deg2Rad;
        }

        /// <summary>
        /// Distância livre num leque com a largura do drone em torno do ângulo (rad, ROS): o menor valor
        /// entre o LiDAR (~1 m do chão) e raios baixos, na altura das rodas. Sem os raios baixos, mesas e
        /// caixas mais baixas que o LiDAR ficam invisíveis e o piloto briga com o assistente de desvio.
        /// </summary>
        float FreeDistance(float angle)
        {
            var r = lidar.Ranges;
            int n = r.Length;
            float inc = 2f * Mathf.PI / n;
            float nearest = float.PositiveInfinity;
            // Abertura angular necessária para a largura do drone caber a ~3 m.
            float halfFan = Mathf.Atan2(halfWidth, 3f);
            for (float a = angle - halfFan; a <= angle + halfFan; a += inc)
            {
                int i = Mathf.RoundToInt((a + Mathf.PI) / inc);
                i = ((i % n) + n) % n;
                float v = r[i];
                if (float.IsNegativeInfinity(v)) v = 0f;
                if (v < nearest) nearest = v;
            }
            for (int k = -1; k <= 1; k++)
                nearest = Mathf.Min(nearest, LowDistance(angle + k * halfFan));
            return float.IsPositiveInfinity(nearest) ? clearDistance * 2f : nearest;
        }

        /// <summary>Raio horizontal na altura das rodas (lowSensorHeight acima do chão), ignorando o próprio drone.</summary>
        float LowDistance(float angle)
        {
            Vector3 fwd = baseBody.transform.forward;
            fwd.y = 0f;
            Vector3 dir = Quaternion.AngleAxis(-angle * Mathf.Rad2Deg, Vector3.up) * fwd.normalized;
            Vector3 origin = baseBody.transform.position;
            origin.y += lowSensorHeight - axleHeight;
            int count = Physics.SphereCastNonAlloc(origin, 0.2f, dir, lowHits, clearDistance * 2f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (!lowHits[i].collider.transform.IsChildOf(transform) && lowHits[i].distance > 0f && lowHits[i].distance < best)
                    best = lowHits[i].distance;
            return best;
        }
    }
}
