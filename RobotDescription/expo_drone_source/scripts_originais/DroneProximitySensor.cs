using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Sensor de proximidade do Drone com desvio automatico.
///
/// - Obstaculo BAIXO que da para pular  -> o drone pula sozinho.
/// - Obstaculo ALTO / largo demais      -> o drone reduz a velocidade e desvia para o lado livre.
/// - Sem saida                          -> para e gira ate achar um caminho livre.
///
/// O sensor atua quando voce acelera (W/S). Tecla T liga/desliga.
/// Coloque no MESMO objeto do DroneController e do DroneJump.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(DroneController))]
[RequireComponent(typeof(DroneJump))]
public class DroneProximitySensor : MonoBehaviour
{
    [Header("Geral")]
    public bool avoidanceEnabled = true;     // tecla T liga/desliga
    public bool showHUD = true;              // painel de estado na tela
    public bool debugDraw = true;            // linha do sensor na Scene (e na Game com Gizmos ligado)
    public LayerMask obstacleMask = ~0;

    [Header("Alcance do sensor")]
    public float minRange = 4f;              // alcance minimo (m)
    public float lookAheadTime = 0.8f;       // alcance extra = velocidade x isso
    public float sensorHeightAboveFloor = 0.35f;
    public float minObstacleHeight = 0.3f;   // abaixo disso e ignorado (meio-fio)

    [Header("Pulo automatico")]
    public float jumpClearance = 0.2f;       // folga acima do obstaculo (m)
    public float jumpTriggerBuffer = 0.3f;   // antecipa o pulo um pouco (m)

    [Header("Desvio lateral")]
    public float stopDistance = 1.2f;
    public float slowDistance = 5f;
    [Range(0.1f, 1f)] public float minSpeedFactor = 0.3f;
    public float dodgeHoldTime = 0.5f;       // continua virando um pouco depois de liberar (s)
    public float probeDistance = 4f;
    public float[] probeAngles = { 15f, 30f, 45f, 60f, 75f, 90f };

    public string State { get; private set; } = "LIVRE";
    public float NearestDistance { get; private set; } = -1f;

    DroneController ctrl;
    DroneJump jump;
    Rigidbody rb;

    float halfWidth = 1.2f;    // meia largura do drone (ao longo do eixo das rodas)
    float halfLength = 0.55f;  // meio comprimento (no sentido do movimento)
    float bottomOffset;
    float floorY;

    int dodgeSide;             // -1 esquerda, +1 direita
    int lastSide;
    float dodgeTimer;
    float jumpStateTimer;
    Color stateColor = Color.green;

    struct Sense { public Collider col; public float dist; }

    void Awake()
    {
        ctrl = GetComponent<DroneController>();
        jump = GetComponent<DroneJump>();
        rb = GetComponent<Rigidbody>();
    }

    void Start()
    {
        var rends = GetComponentsInChildren<Renderer>();
        if (rends.Length > 0)
        {
            Bounds b = rends[0].bounds;
            foreach (var r in rends) b.Encapsulate(r.bounds);
            halfWidth = ProjectExtent(b.extents, ctrl.RollAxisWorld);
            halfLength = ProjectExtent(b.extents, ctrl.DriveForward);
            bottomOffset = b.min.y - transform.position.y;
        }
        floorY = transform.position.y + bottomOffset;
    }

    static float ProjectExtent(Vector3 e, Vector3 d)
    {
        return Mathf.Abs(d.x) * e.x + Mathf.Abs(d.y) * e.y + Mathf.Abs(d.z) * e.z;
    }

    void Update()
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        if (k != null && k.tKey.wasPressedThisFrame) avoidanceEnabled = !avoidanceEnabled;
#else
        if (Input.GetKeyDown(KeyCode.T)) avoidanceEnabled = !avoidanceEnabled;
#endif
    }

    // ------------------------------------------------------------------
    // Chamado pelo DroneController a cada FixedUpdate.
    // Recebe a entrada do jogador e devolve a entrada corrigida.
    // ------------------------------------------------------------------
    public Vector2 Filter(Vector2 input, float currentSpeed)
    {
        if (!avoidanceEnabled)
        {
            SetState("SENSOR DESLIGADO", Color.gray);
            NearestDistance = -1f;
            return input;
        }

        if (jump.IsGrounded) floorY = FindFloorY();
        if (jumpStateTimer > 0f) jumpStateTimer -= Time.fixedDeltaTime;

        float throttle = input.y;
        if (Mathf.Abs(throttle) < 0.1f)
        {
            dodgeSide = 0;
            dodgeTimer = 0f;
            NearestDistance = -1f;
            if (jumpStateTimer <= 0f) SetState("PARADO", Color.gray);
            return input;
        }

        float sgn = Mathf.Sign(throttle);
        Vector3 dir = ctrl.DriveForward * sgn;
        dir.y = 0f;
        dir.Normalize();

        float speed = Mathf.Max(Mathf.Abs(currentSpeed), 1f);
        float range = minRange + speed * lookAheadTime;

        Sense front;
        bool blocked = Probe(dir, range, out front);
        NearestDistance = blocked ? front.dist : -1f;

        if (debugDraw)
        {
            Vector3 o = new Vector3(transform.position.x, floorY + sensorHeightAboveFloor, transform.position.z);
            Debug.DrawRay(o, dir * (blocked ? front.dist + halfLength : range), blocked ? Color.red : Color.green);
        }

        // ---------------- caminho livre ----------------
        if (!blocked)
        {
            if (dodgeTimer > 0f)
            {
                dodgeTimer -= Time.fixedDeltaTime;
                input.x = dodgeSide;
                SetState(dodgeSide > 0 ? "DESVIANDO >>" : "<< DESVIANDO", new Color(1f, 0.55f, 0f));
                return input;
            }
            dodgeSide = 0;
            if (jumpStateTimer > 0f) SetState("PULANDO", Color.yellow);
            else SetState("LIVRE", Color.green);
            return input;
        }

        // ---------------- obstaculo a frente ----------------
        float obstacleHeight = front.col.bounds.max.y - floorY;
        Vector3 size = front.col.bounds.size;
        float depth = Mathf.Abs(dir.x) * size.x + Mathf.Abs(dir.z) * size.z;
        float planSpeed = Mathf.Max(speed, ctrl.maxSpeed * 0.85f);

        float tRise;
        if (CanJumpOver(obstacleHeight, depth, planSpeed, out tRise))
        {
            // pula no momento certo: quando o drone ja estara no alto ao chegar no obstaculo
            float triggerGap = speed * tRise + jumpTriggerBuffer;
            if (jump.IsGrounded && front.dist <= triggerGap)
            {
                jump.RequestJump();
                jumpStateTimer = 0.9f;
            }
            SetState(jumpStateTimer > 0f ? "PULANDO" : "OBSTACULO BAIXO", Color.yellow);
            input.y = sgn;   // mantem a velocidade para completar o salto
            return input;
        }

        // nao da para pular -> desvia pelo lado livre
        int side = (dodgeTimer > 0f && dodgeSide != 0) ? dodgeSide : ChooseSide(dir);
        if (side == 0)
        {
            SetState("BLOQUEADO - PROCURANDO SAIDA", Color.red);
            input.y = 0f;     // para
            input.x = 1f;     // e gira no lugar ate achar um caminho
            return input;
        }

        dodgeSide = side;
        lastSide = side;
        dodgeTimer = dodgeHoldTime;

        float slow = Mathf.Clamp01((front.dist - stopDistance) / Mathf.Max(0.1f, slowDistance - stopDistance));
        input.y *= Mathf.Lerp(minSpeedFactor, 1f, slow);
        input.x = side;
        SetState(side > 0 ? "DESVIANDO >>" : "<< DESVIANDO", new Color(1f, 0.55f, 0f));
        return input;
    }

    // ------------------------------------------------------------------
    // O drone consegue passar por cima? (fisica do pulo)
    // ------------------------------------------------------------------
    bool CanJumpOver(float obstacleHeight, float depth, float planSpeed, out float tRise)
    {
        tRise = 0f;
        float H = jump.jumpHeight;
        float hh = obstacleHeight + jumpClearance;
        if (hh >= H * 0.97f) return false;                     // alto demais

        float g = Mathf.Abs(Physics.gravity.y);
        float v0 = Mathf.Sqrt(2f * g * H);
        float tUp = v0 / g;                                    // tempo ate o topo
        tRise = (v0 - Mathf.Sqrt(Mathf.Max(0f, v0 * v0 - 2f * g * hh))) / g;   // tempo ate passar da altura do obstaculo
        float tFall = Mathf.Sqrt(2f * (H - hh) / (g * Mathf.Max(1f, jump.fallMultiplier)));
        float timeAbove = (tUp + tFall) - tRise;               // tempo acima do obstaculo

        float reach = planSpeed * timeAbove;                   // distancia percorrida no alto
        return reach >= (depth + 2f * halfLength) * 0.9f;      // precisa cobrir o obstaculo + o comprimento do drone
    }

    // ------------------------------------------------------------------
    // Escolhe o lado livre com o menor desvio angular. 0 = sem saida.
    // ------------------------------------------------------------------
    int ChooseSide(Vector3 dir)
    {
        foreach (float a in probeAngles)
        {
            Sense tmp;
            bool freeR = !Probe(Quaternion.AngleAxis(a, Vector3.up) * dir, probeDistance, out tmp);
            bool freeL = !Probe(Quaternion.AngleAxis(-a, Vector3.up) * dir, probeDistance, out tmp);
            if (freeR && freeL) return lastSide != 0 ? lastSide : 1;
            if (freeR) return 1;
            if (freeL) return -1;
        }
        return 0;
    }

    // ------------------------------------------------------------------
    // Varredura com uma caixa do tamanho do drone na direcao dir.
    // ------------------------------------------------------------------
    bool Probe(Vector3 dir, float distance, out Sense result)
    {
        result = default(Sense);

        Vector3 center = transform.position + dir * (halfLength + 0.05f);
        center.y = floorY + sensorHeightAboveFloor;
        Quaternion rot = Quaternion.LookRotation(dir, Vector3.up);
        Vector3 half = new Vector3(halfWidth * 0.98f, 0.1f, 0.05f);

        // obstaculo ja encostando no drone
        Collider[] overlaps = Physics.OverlapBox(center, half, rot, obstacleMask, QueryTriggerInteraction.Ignore);
        foreach (var c in overlaps)
        {
            if (!IsObstacle(c)) continue;
            result.col = c;
            result.dist = 0f;
            return true;
        }

        RaycastHit[] hits = Physics.BoxCastAll(center, half, dir, rot, distance, obstacleMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        foreach (var h in hits)
        {
            if (!IsObstacle(h.collider)) continue;
            if (h.distance < best)
            {
                best = h.distance;
                result.col = h.collider;
                result.dist = h.distance;
            }
        }
        return result.col != null;
    }

    bool IsObstacle(Collider c)
    {
        if (c.transform.IsChildOf(transform)) return false;               // o proprio drone
        return c.bounds.max.y - floorY >= minObstacleHeight;               // chao e meio-fio nao contam
    }

    float FindFloorY()
    {
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 50f, obstacleMask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        float y = floorY;
        foreach (var h in hits)
        {
            if (h.collider.transform.IsChildOf(transform)) continue;
            if (h.distance < best)
            {
                best = h.distance;
                y = h.point.y;
            }
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

        Color prevBg = GUI.backgroundColor;
        GUIStyle st = new GUIStyle(GUI.skin.box);
        st.fontSize = 15;
        st.alignment = TextAnchor.UpperLeft;
        st.normal.textColor = Color.white;

        GUI.backgroundColor = new Color(stateColor.r, stateColor.g, stateColor.b, 1f);
        string dist = NearestDistance >= 0f ? NearestDistance.ToString("0.0") + " m" : "--";
        GUI.Box(new Rect(12, 12, 340, 74),
            "SENSOR DE PROXIMIDADE  (T liga/desliga)\nEstado: " + State + "\nObstaculo a frente: " + dist, st);
        GUI.backgroundColor = prevBg;
    }
}
