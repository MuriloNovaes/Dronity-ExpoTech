using RosMessageTypes.Std;
using Unity.Robotics.ROSTCPConnector;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace ExpoTech
{
    /// <summary>
    /// Assistente de desvio (versão física/ROS 2 do DroneProximitySensor original):
    /// - obstáculo BAIXO que dá para pular -> pede o pulo no momento certo;
    /// - obstáculo ALTO -> reduz a velocidade e desvia para o lado livre;
    /// - sem saída -> para e gira até achar caminho.
    /// Corrige o comando final do ExpoDroneDriveController (vindo do ROS ou do teclado).
    /// ROS 2: publica /expo_drone/avoidance_state (std_msgs/String) e assina
    /// /expo_drone/avoidance_enable (std_msgs/Bool). Tecla T liga/desliga. LEDs mostram o estado.
    /// </summary>
    public class ExpoDroneSmartAvoidance : MonoBehaviour
    {
        [Header("Geral")]
        public bool avoidanceEnabled = true;
        public bool showHUD = true;
        public bool debugDraw = true;
        public LayerMask obstacleMask = ~0;

        [Header("ROS 2")]
        public bool useRos = true;
        public string stateTopic = "/expo_drone/avoidance_state";
        public string enableTopic = "/expo_drone/avoidance_enable";

        [Header("Referências")]
        public ExpoDroneJump jump;
        public ExpoDroneDriveController drive;
        [Tooltip("base_link (articulação raiz)")] public ArticulationBody baseBody;
        [Tooltip("Renderer dos LEDs: a cor mostra o estado")] public Renderer statusLeds;

        [Header("Alcance do sensor")]
        public float minRange = 2.2f;
        public float lookAheadTime = 0.5f;
        public float sensorHeightAboveFloor = 0.35f;
        [Tooltip("Abaixo disso é ignorado (as rodas passam por cima)")] public float minObstacleHeight = 0.3f;

        [Header("Pulo automático")]
        public float jumpClearance = 0.2f;
        public float jumpTriggerBuffer = 0.3f;
        [Tooltip("Folga extra exigida depois do obstáculo para pousar (m)")] public float landingMargin = 0.5f;

        [Header("Desvio lateral")]
        public float stopDistance = 0.9f;
        public float slowDistance = 3f;
        [Range(0.1f, 1f)] public float minSpeedFactor = 0.3f;
        public float dodgeHoldTime = 0.3f;
        public float probeDistance = 2.5f;
        public float[] probeAngles = { 15f, 30f, 45f, 60f, 75f, 90f };
        [Tooltip("Velocidade angular usada para desviar (rad/s)")] public float dodgeTurnRate = 0.8f;

        public string State { get; private set; } = "LIVRE";
        public float NearestDistance { get; private set; } = -1f;

        float halfWidth = 1.4f, halfLength = 0.55f, floorY;
        int dodgeSide, lastSide;
        float dodgeTimer, jumpStateTimer, publishElapsed;
        Color stateColor = Color.green;
        string lastPublished;
        ROSConnection ros;
        MaterialPropertyBlock mpb;

        struct Sense { public Collider col; public float dist; }

        void Start()
        {
            if (jump == null) jump = GetComponent<ExpoDroneJump>();
            if (drive == null) drive = GetComponent<ExpoDroneDriveController>();
            MeasureRobot();
            floorY = FindFloorY();
            if (!useRos) return;
            ros = ROSConnection.GetOrCreateInstance();
            ros.RegisterPublisher<StringMsg>(stateTopic);
            ros.Subscribe<BoolMsg>(enableTopic, msg => avoidanceEnabled = msg.data);
        }

        void MeasureRobot()
        {
            var cols = GetComponentsInChildren<Collider>();
            if (cols.Length == 0) return;
            Bounds b = cols[0].bounds;
            foreach (var c in cols) b.Encapsulate(c.bounds);
            var t = baseBody.transform;
            halfWidth = Mathf.Abs(t.right.x) * b.extents.x + Mathf.Abs(t.right.z) * b.extents.z;
            halfLength = Mathf.Abs(t.forward.x) * b.extents.x + Mathf.Abs(t.forward.z) * b.extents.z;
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var k = Keyboard.current;
            if (k != null && k.tKey.wasPressedThisFrame) avoidanceEnabled = !avoidanceEnabled;
#else
            if (Input.GetKeyDown(KeyCode.T)) avoidanceEnabled = !avoidanceEnabled;
#endif
            if (statusLeds != null)
            {
                mpb ??= new MaterialPropertyBlock();
                statusLeds.GetPropertyBlock(mpb);
                mpb.SetColor("_EmissionColor", stateColor * 4f);
                mpb.SetColor("_BaseColor", stateColor);
                statusLeds.SetPropertyBlock(mpb);
            }
            if (!useRos) return;
            publishElapsed += Time.deltaTime;
            if (State != lastPublished || publishElapsed >= 0.5f)
            {
                ros.Publish(stateTopic, new StringMsg(State));
                lastPublished = State;
                publishElapsed = 0f;
            }
        }

        /// <summary>Chamado pelo ExpoDroneDriveController a cada FixedUpdate (unidades ROS: m/s, rad/s).</summary>
        public void Filter(ref float linear, ref float angular)
        {
            if (!avoidanceEnabled)
            {
                SetState("SENSOR DESLIGADO", Color.gray);
                NearestDistance = -1f;
                return;
            }

            if (jump != null && jump.IsGrounded) floorY = FindFloorY();
            if (jumpStateTimer > 0f) jumpStateTimer -= Time.fixedDeltaTime;

            if (Mathf.Abs(linear) < 0.05f)
            {
                dodgeSide = 0;
                dodgeTimer = 0f;
                NearestDistance = -1f;
                if (jumpStateTimer <= 0f) SetState("PARADO", Color.gray);
                return;
            }

            float sgn = Mathf.Sign(linear);
            Vector3 fwd = baseBody.transform.forward;
            fwd.y = 0f;
            Vector3 dir = fwd.normalized * sgn;

            float speed = Mathf.Max(Mathf.Abs(Vector3.Dot(baseBody.GetPointVelocity(baseBody.transform.position), dir)), 1f);
            float range = minRange + speed * lookAheadTime;
            bool blocked = Probe(dir, range, out Sense front);
            NearestDistance = blocked ? front.dist : -1f;

            if (debugDraw)
            {
                Vector3 o = baseBody.transform.position;
                o.y = floorY + sensorHeightAboveFloor;
                Debug.DrawRay(o, dir * (blocked ? front.dist + halfLength : range), blocked ? Color.red : Color.green);
            }

            // ---------------- caminho livre ----------------
            if (!blocked)
            {
                if (dodgeTimer > 0f)
                {
                    dodgeTimer -= Time.fixedDeltaTime;
                    angular = -dodgeSide * dodgeTurnRate;
                    SetState(dodgeSide > 0 ? "DESVIANDO >>" : "<< DESVIANDO", new Color(1f, 0.55f, 0f));
                    return;
                }
                dodgeSide = 0;
                SetState(jumpStateTimer > 0f ? "PULANDO" : "LIVRE", jumpStateTimer > 0f ? Color.yellow : Color.green);
                return;
            }

            // ---------------- obstáculo à frente ----------------
            float obstacleHeight = front.col.bounds.max.y - floorY;
            Vector3 size = front.col.bounds.size;
            float depth = Mathf.Abs(dir.x) * size.x + Mathf.Abs(dir.z) * size.z;
            float planSpeed = Mathf.Max(speed, drive.maxLinear * 0.85f);

            if (jump != null && CanJumpOver(obstacleHeight, depth, planSpeed, out float tRise, out float neededSpeed)
                && LandingZoneClear(front, dir, depth, planSpeed))
            {
                // No ar as rodas não empurram: a velocidade da decolagem é a do salto inteiro.
                // Só pula com a velocidade REAL suficiente; até lá, acelera em direção ao obstáculo.
                // Velocidade no EIXO: o centro de massa do corpo balança (pêndulo) e engana a medida.
                float realSpeed = Mathf.Abs(Vector3.Dot(baseBody.GetPointVelocity(baseBody.transform.position), dir));
                float triggerGap = realSpeed * tRise + jumpTriggerBuffer;
                bool fastEnough = realSpeed >= neededSpeed;
                if (jump.IsGrounded && fastEnough && front.dist <= triggerGap)
                {
                    jump.RequestJump();
                    jumpStateTimer = 0.9f;
                }
                // Perto demais e ainda lento: não dá mais para ganhar velocidade -> desvia.
                bool tooLate = !fastEnough && front.dist <= triggerGap && jumpStateTimer <= 0f;
                if (!tooLate)
                {
                    SetState(jumpStateTimer > 0f ? "PULANDO" : "OBSTACULO BAIXO", Color.yellow);
                    linear = sgn * drive.maxLinear; // acelera para completar o salto
                    return;
                }
            }

            // Não dá para pular: desvia pelo lado livre (+1 = direita).
            int side = (dodgeTimer > 0f && dodgeSide != 0) ? dodgeSide : ChooseSide(dir);
            if (side == 0)
            {
                if (CanSpin())
                {
                    SetState("BLOQUEADO - PROCURANDO SAIDA", Color.red);
                    linear = 0f;
                    angular = -dodgeTurnRate; // gira no lugar
                }
                else
                {
                    // Sem espaço para girar (o drone tem ~2,8 m de largura): sai de ré, reto.
                    SetState("SEM ESPACO - RECUANDO", Color.red);
                    linear = -sgn * 0.6f;
                    angular = 0f;
                }
                return;
            }

            dodgeSide = lastSide = side;
            dodgeTimer = dodgeHoldTime;
            float slow = Mathf.Clamp01((front.dist - stopDistance) / Mathf.Max(0.1f, slowDistance - stopDistance));
            linear *= Mathf.Lerp(minSpeedFactor, 1f, slow);
            angular = -side * dodgeTurnRate; // ROS: positivo = esquerda
            SetState(side > 0 ? "DESVIANDO >>" : "<< DESVIANDO", new Color(1f, 0.55f, 0f));
        }

        /// <summary>O drone passa por cima? (balística do pulo físico)</summary>
        bool CanJumpOver(float obstacleHeight, float depth, float planSpeed, out float tRise, out float neededSpeed)
        {
            tRise = 0f;
            neededSpeed = float.PositiveInfinity;
            float H = jump.jumpHeight;
            float hh = obstacleHeight + jumpClearance;
            if (hh >= H * 0.97f) return false;

            float g = Mathf.Abs(Physics.gravity.y);
            float v0 = Mathf.Sqrt(2f * g * H);
            float tUp = v0 / g;
            tRise = (v0 - Mathf.Sqrt(Mathf.Max(0f, v0 * v0 - 2f * g * hh))) / g;
            float tFall = Mathf.Sqrt(2f * (H - hh) / (g * Mathf.Max(1f, jump.fallMultiplier)));
            float timeAbove = tUp + tFall - tRise;
            float needed = depth + 2f * halfLength; // obstáculo + comprimento do drone
            neededSpeed = needed / timeAbove;
            return planSpeed * timeAbove >= needed;
        }

        /// <summary>
        /// Depois do obstáculo há espaço para pousar, frear e, se o próximo obstáculo for alto, girar?
        /// Sem isso o drone pula para dentro de um vão estreito e fica preso.
        /// </summary>
        bool LandingZoneClear(Sense front, Vector3 dir, float depth, float takeoffSpeed)
        {
            float decel = drive.maxLinearDeceleration > 0f ? drive.maxLinearDeceleration : drive.maxLinearAcceleration;
            float stopDistance = decel > 0f ? takeoffSpeed * takeoffSpeed / (2f * decel) : 1f;
            float need = Mathf.Max(2f * halfLength + stopDistance, 2f * halfWidth) + landingMargin;

            Vector3 start = baseBody.transform.position + dir * (halfLength + 0.05f + front.dist + depth + 0.05f);
            start.y = floorY + sensorHeightAboveFloor;
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            Vector3 half = new Vector3(halfWidth * 0.98f, 0.1f, 0.05f);
            foreach (var h in Physics.BoxCastAll(start, half, dir, rot, need, obstacleMask, QueryTriggerInteraction.Ignore))
                if (h.collider != front.col && IsObstacle(h.collider)) return false;
            foreach (var c in Physics.OverlapBox(start, half, rot, obstacleMask, QueryTriggerInteraction.Ignore))
                if (c != front.col && IsObstacle(c)) return false;
            return true;
        }

        /// <summary>Há um círculo livre do tamanho do drone para girar no lugar?</summary>
        public bool CanSpin()
        {
            Vector3 c = baseBody.transform.position;
            c.y = floorY + sensorHeightAboveFloor;
            foreach (var col in Physics.OverlapSphere(c, halfWidth * 0.95f, obstacleMask, QueryTriggerInteraction.Ignore))
                if (IsObstacle(col)) return false;
            return true;
        }

        int ChooseSide(Vector3 dir)
        {
            foreach (float a in probeAngles)
            {
                bool freeR = !Probe(Quaternion.AngleAxis(a, Vector3.up) * dir, probeDistance, out _);
                bool freeL = !Probe(Quaternion.AngleAxis(-a, Vector3.up) * dir, probeDistance, out _);
                if (freeR && freeL) return lastSide != 0 ? lastSide : 1;
                if (freeR) return 1;
                if (freeL) return -1;
            }
            return 0;
        }

        bool Probe(Vector3 dir, float distance, out Sense result)
        {
            result = default;
            Vector3 center = baseBody.transform.position + dir * (halfLength + 0.05f);
            center.y = floorY + sensorHeightAboveFloor;
            Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
            Vector3 half = new Vector3(halfWidth * 0.98f, 0.1f, 0.05f);

            foreach (var c in Physics.OverlapBox(center, half, rot, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                if (!IsObstacle(c)) continue;
                result.col = c;
                result.dist = 0f;
                return true;
            }

            float best = float.MaxValue;
            foreach (var h in Physics.BoxCastAll(center, half, dir, rot, distance, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                if (!IsObstacle(h.collider) || h.distance >= best) continue;
                best = h.distance;
                result.col = h.collider;
                result.dist = h.distance;
            }
            return result.col != null;
        }

        bool IsObstacle(Collider c) =>
            !c.transform.IsChildOf(transform) && c.bounds.max.y - floorY >= minObstacleHeight;

        float FindFloorY()
        {
            float best = float.MaxValue, y = floorY;
            foreach (var h in Physics.RaycastAll(baseBody.transform.position, Vector3.down, 50f, obstacleMask, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(transform) || h.distance >= best) continue;
                best = h.distance;
                y = h.point.y;
            }
            return y;
        }

        void SetState(string s, Color c)
        {
            State = s;
            stateColor = c;
        }

        void OnGUI()
        {
            if (!showHUD) return;
            var style = new GUIStyle(GUI.skin.box) { fontSize = 15, alignment = TextAnchor.UpperLeft };
            style.normal.textColor = Color.white;
            var prev = GUI.backgroundColor;
            GUI.backgroundColor = stateColor;
            string dist = NearestDistance >= 0f ? NearestDistance.ToString("0.0") + " m" : "--";
            string ground = jump != null && jump.IsGrounded ? "no chão" : "no ar";
            GUI.Box(new Rect(12, Screen.height - 98, 360, 86),
                $"ASSISTENTE DE DESVIO  (T liga/desliga)\nEstado: {State}\nObstáculo à frente: {dist}\nDrone: {ground}", style);
            GUI.backgroundColor = prev;
        }
    }
}
