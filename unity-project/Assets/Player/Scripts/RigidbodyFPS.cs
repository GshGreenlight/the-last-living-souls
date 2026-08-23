using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class RigidbodyFPS : MonoBehaviour
{
    [Header("References")]
    public Transform cameraPivot;
    public PhysicalCable cable;

    [Header("Input")]
    public InputActionReference moveAction;
    public InputActionReference lookAction;

    [Header("Movement")]
    public float maxSpeed = 4f;
    public float acceleration = 25f;   // м/с², время разгона до maxSpeed ≈ maxSpeed / acceleration
    public float deceleration = 40f;   // м/с², торможение при отсутствии ввода

    [Header("Ground Check")]
    public float groundCheckRadius = 0.25f;
    public float groundCheckDistance = 0.15f;
    public LayerMask groundMask = ~0;

    [Header("Look")]
    public float mouseSensitivity = 0.1f;
    public float minPitch = -80f;
    public float maxPitch = 80f;
    private float pendingYaw;

    [Header("Cable Retract")]
    public InputActionReference retractAction;
    public UnityEvent onRetractStart;
    public UnityEvent onRetractEnd;
    
    [Header("Cable Tension")]
    [Range(0f, 1f)]
    public float tensionStartRatio = 0.95f;
    [Range(0f, 1f)]
    public float minForceScale = 0.5f;

    private bool isRetracting;
    public bool IsRetracting => isRetracting;

    private Rigidbody rb;
    private Collider ownCollider;
    private readonly Collider[] groundCheckResults = new Collider[8];
    private float pitch;
    private bool grounded;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        ownCollider = GetComponent<CapsuleCollider>();

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.mass = 70f;
        rb.linearDamping = 0f;
        rb.angularDamping = 0f;
        rb.useGravity = true;

        if (cameraPivot != null)
        {
            pitch = cameraPivot.localEulerAngles.x;
            if (pitch > 180f) pitch -= 360f;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnEnable()
    {
        moveAction?.action.Enable();
        lookAction?.action.Enable();
        retractAction?.action.Enable();
    }

    private void OnDisable()
    {
        moveAction?.action.Disable();
        lookAction?.action.Disable();
        retractAction?.action.Disable();
    }

    private void Update()
    {
        HandleLookInput();
        HandleRetractInput();
    }

    private void FixedUpdate()
    {
        CheckGround();

        if (pendingYaw != 0f)
        {
            Quaternion deltaRot = Quaternion.Euler(0f, pendingYaw, 0f);
            rb.MoveRotation(rb.rotation * deltaRot);
            pendingYaw = 0f;
        }

        if (isRetracting)
        {
            Vector3 vel = rb.linearVelocity;
            rb.linearVelocity = new Vector3(0f, vel.y, 0f);
        }
        else
        {
            HandleMovement();
        }
    }

    private void HandleLookInput()
    {
        if (lookAction == null)
            return;

        Vector2 look = lookAction.action.ReadValue<Vector2>();

        pendingYaw += look.x * mouseSensitivity;

        if (cameraPivot != null)
        {
            pitch = Mathf.Clamp(pitch - look.y * mouseSensitivity, minPitch, maxPitch);
            cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }
    }

    private void CheckGround()
    {
        Bounds bounds = ownCollider.bounds;
        Vector3 checkPos = new Vector3(bounds.center.x, bounds.min.y + groundCheckRadius, bounds.center.z);

        int hitCount = Physics.OverlapSphereNonAlloc(
            checkPos,
            groundCheckRadius + groundCheckDistance,
            groundCheckResults,
            groundMask,
            QueryTriggerInteraction.Ignore
        );

        grounded = false;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = groundCheckResults[i];

            if (hit == ownCollider) continue;
            if (hit.transform.IsChildOf(transform)) continue;

            grounded = true;
            break;
        }
    }

    private void HandleMovement()
    {
        Vector3 vel = rb.linearVelocity;
        Vector3 hVel = new Vector3(vel.x, 0f, vel.z);

        Vector2 input = Vector2.zero;

        if (grounded && moveAction != null)
            input = moveAction.action.ReadValue<Vector2>();

        Vector3 wishDir = transform.right * input.x + transform.forward * input.y;

        if (wishDir.sqrMagnitude > 1f)
            wishDir.Normalize();

        bool hasInput = wishDir.sqrMagnitude > 0.01f;

        float scale = hasInput ? GetCableForceScale(wishDir) : 1f;
        float effectiveAccel = acceleration * scale;

        Vector3 wishVel = wishDir * maxSpeed;
        float rate = hasInput ? effectiveAccel : deceleration;

        Vector3 newHVel = grounded
            ? Vector3.MoveTowards(hVel, wishVel, rate * Time.fixedDeltaTime)
            : hVel;

        Vector3 deltaV = newHVel - hVel;
        Vector3 forceNeeded = deltaV * rb.mass / Time.fixedDeltaTime;

        rb.AddForce(forceNeeded, ForceMode.Force);
    }

    private float GetCableForceScale(Vector3 wishDir)
    {
        if (cable == null || cable.ActiveCount < cable.segmentCount)
            return 1f;

        float chainLen = cable.GetChainLength();
        float maxLen = cable.totalLength;

        if (chainLen < maxLen * tensionStartRatio)
            return 1f;

        Vector3 pullDir = cable.GetPullDirectionXZ();
        if (pullDir.sqrMagnitude < 0.001f)
            return 1f;

        float awayDot = Vector3.Dot(wishDir, -pullDir);
        if (awayDot <= 0f)
            return 1f;

        float tensionRatio = Mathf.InverseLerp(maxLen * tensionStartRatio, maxLen, chainLen);

        float scale = Mathf.Lerp(1f, minForceScale, tensionRatio);

        scale = Mathf.Lerp(1f, scale, awayDot);

        return Mathf.Clamp01(scale);
    }

    private void HandleRetractInput()
    {
        if (retractAction == null || cable == null)
            return;

        bool pressed = retractAction.action.IsPressed() && !cable.IsFullyRetracted;

        if (pressed && !isRetracting)
        {
            isRetracting = true;
            cable.SetManualRetract(true);

            rb.constraints = RigidbodyConstraints.FreezeRotation
                            | RigidbodyConstraints.FreezePositionX
                            | RigidbodyConstraints.FreezePositionZ;

            onRetractStart?.Invoke();
        }
        else if (!pressed && isRetracting)
        {
            isRetracting = false;
            cable.SetManualRetract(false);

            rb.constraints = RigidbodyConstraints.FreezeRotation;

            onRetractEnd?.Invoke();
        }
    }
}