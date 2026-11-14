using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Pulo do Drone. Coloque no MESMO objeto do DroneController.
/// O pulo normalmente e acionado pelo DroneProximitySensor (RequestJump).
/// Marque "Manual Jump" se quiser tambem pular com Espaco / botao A.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class DroneJump : MonoBehaviour
{
    [Header("Pulo")]
    public bool manualJump = false;         // permite pular com Espaco / botao A do controle
    public float jumpHeight = 1.5f;         // altura maxima do pulo (m)
    public float coyoteTime = 0.1f;
    public float jumpBufferTime = 0.15f;

    [Header("Gravidade (sensacao de peso)")]
    public float fallMultiplier = 2.5f;
    public float lowJumpMultiplier = 2f;

    [Header("Deteccao de chao")]
    public LayerMask groundMask = ~0;
    public float groundCheckDistance = 0.2f;

    public bool IsGrounded { get; private set; }

    Rigidbody rb;
    float bottomOffset;
    float checkRadius = 0.3f;
    float lastGroundedTime = -10f;
    float lastJumpPressedTime = -10f;
    bool jumpHeld;
    bool autoJumpRequested;
    bool autoJumpActive;

    Vector3 Velocity
    {
        get
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }
        set
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = value;
#else
            rb.velocity = value;
#endif
        }
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();

        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            bottomOffset = b.min.y - transform.position.y;
            checkRadius = Mathf.Max(0.1f, Mathf.Min(b.extents.x, b.extents.z) * 0.4f);
        }
    }

    /// <summary>Pede um pulo (usado pelo sensor de proximidade).</summary>
    public void RequestJump()
    {
        lastJumpPressedTime = Time.time;
        autoJumpRequested = true;
    }

    void Update()
    {
        if (!manualJump)
        {
            jumpHeld = false;
            return;
        }

        bool pressed, held;
        ReadJumpInput(out pressed, out held);
        jumpHeld = held;
        if (pressed) lastJumpPressedTime = Time.time;
    }

    void ReadJumpInput(out bool pressed, out bool held)
    {
#if ENABLE_INPUT_SYSTEM
        var k = Keyboard.current;
        var g = Gamepad.current;
        pressed = (k != null && k.spaceKey.wasPressedThisFrame) || (g != null && g.buttonSouth.wasPressedThisFrame);
        held    = (k != null && k.spaceKey.isPressed)           || (g != null && g.buttonSouth.isPressed);
#else
        pressed = Input.GetButtonDown("Jump");
        held    = Input.GetButton("Jump");
#endif
    }

    void FixedUpdate()
    {
        IsGrounded = Velocity.y <= 0.1f && CheckGround();
        if (IsGrounded) lastGroundedTime = Time.time;

        bool canJump   = Time.time - lastGroundedTime    <= coyoteTime;
        bool wantsJump = Time.time - lastJumpPressedTime <= jumpBufferTime;

        if (canJump && wantsJump) Jump();

        ApplyBetterGravity();
    }

    bool CheckGround()
    {
        Vector3 origin = transform.position + Vector3.up * (bottomOffset + checkRadius + 0.05f);
        RaycastHit[] hits = Physics.SphereCastAll(origin, checkRadius, Vector3.down,
                                                  groundCheckDistance + 0.05f, groundMask,
                                                  QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            if (h.collider.attachedRigidbody == rb) continue;
            if (h.collider.transform.IsChildOf(transform)) continue;
            return true;
        }
        return false;
    }

    void Jump()
    {
        float g = Mathf.Abs(Physics.gravity.y);
        Vector3 vel = Velocity;
        vel.y = Mathf.Sqrt(2f * g * jumpHeight);   // v = raiz(2 * g * h)
        Velocity = vel;

        autoJumpActive = autoJumpRequested;        // pulo automatico sobe ate a altura maxima
        autoJumpRequested = false;
        lastJumpPressedTime = -10f;
        lastGroundedTime = -10f;
        IsGrounded = false;
    }

    void ApplyBetterGravity()
    {
        if (!rb.useGravity) return;

        float vy = Velocity.y;
        bool held = jumpHeld || autoJumpActive;

        if (vy < 0f)
            rb.AddForce(Physics.gravity * (fallMultiplier - 1f), ForceMode.Acceleration);
        else if (vy > 0f && !held)
            rb.AddForce(Physics.gravity * (lowJumpMultiplier - 1f), ForceMode.Acceleration);

        if (vy <= 0f) autoJumpActive = false;
    }
}
