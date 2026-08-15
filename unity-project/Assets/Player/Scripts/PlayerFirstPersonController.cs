using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class PlayerFirstPersonController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] Camera playerCamera;
    [SerializeField] InputActionAsset inputActions;
    [Tooltip("Optional. If empty, camera transform is used for head bob.")]
    [SerializeField] Transform joint;

    [Header("Look")]
    [SerializeField] bool cameraCanMove = true;
    [SerializeField] float mouseSensitivity = 2f;
    [SerializeField] float maxLookAngle = 50f;
    [SerializeField] bool invertCamera;
    [SerializeField] bool lockCursor = true;

    [Header("Movement")]
    [SerializeField] bool playerCanMove = true;
    [SerializeField] float walkSpeed = 5f;
    [SerializeField] float maxVelocityChange = 10f;

    [Header("Jump")]
    [SerializeField] bool enableJump = true;
    [SerializeField] float jumpPower = 5f;

    [Header("Crouch")]
    [SerializeField] bool enableCrouch = true;
    [SerializeField] float crouchHeight = 0.75f;
    [SerializeField] float speedReduction = 0.5f;

    [Header("Head Bob")]
    [SerializeField] bool enableHeadBob = true;
    [SerializeField] float bobSpeed = 10f;
    [SerializeField] Vector3 bobAmount = new Vector3(0f, 0.1f, 0f);

    Rigidbody _rb;
    InputActionMap _playerMap;
    InputAction _move;
    InputAction _jump;
    InputAction _crouch;

    float _yaw;
    float _pitch;
    float _bobTimer;
    float _currentWalkSpeed;

    Vector3 _standingScale;
    Vector3 _jointBasePos;

    bool _isGrounded;
    bool _isCrouched;
    bool _isWalking;
    bool _crouchHeldPrev;

    const float MouseAxisScale = 0.08f;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.freezeRotation = true;
        _rb.interpolation = RigidbodyInterpolation.None;

        if (playerCamera == null)
            playerCamera = GetComponentInChildren<Camera>();

        if (joint == null && playerCamera != null)
            joint = playerCamera.transform;

        _standingScale = transform.localScale;
        _jointBasePos = joint != null ? joint.localPosition : Vector3.zero;
        _currentWalkSpeed = walkSpeed;
        _yaw = transform.localEulerAngles.y;

        if (inputActions == null)
        {
            Debug.LogError($"{nameof(PlayerFirstPersonController)}: assign InputSystem_Actions.", this);
            enabled = false;
            return;
        }

        _playerMap = inputActions.FindActionMap("Player", throwIfNotFound: true);
        _move = _playerMap.FindAction("Move", throwIfNotFound: true);
        _jump = _playerMap.FindAction("Jump", throwIfNotFound: true);
        _crouch = _playerMap.FindAction("Crouch", throwIfNotFound: true);
    }

    void OnEnable()
    {
        _playerMap?.Enable();
    }

    void OnDisable()
    {
        _playerMap?.Disable();
    }

    void Start()
    {
        if (!lockCursor)
            return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        HandleLook();
        HandleJump();
        HandleCrouchHold();
        CheckGround();

        if (enableHeadBob)
            HeadBob();
    }

    void FixedUpdate()
    {
        if (!playerCanMove)
            return;

        Vector2 moveInput = _move.ReadValue<Vector2>();
        if (moveInput.sqrMagnitude > 1f)
            moveInput.Normalize();

        Vector3 targetVelocity = new Vector3(moveInput.x, 0f, moveInput.y);
        _isWalking = (targetVelocity.x != 0f || targetVelocity.z != 0f) && _isGrounded;

        targetVelocity = transform.TransformDirection(targetVelocity) * _currentWalkSpeed;

        Vector3 velocity = _rb.linearVelocity;
        Vector3 velocityChange = targetVelocity - velocity;
        velocityChange.x = Mathf.Clamp(velocityChange.x, -maxVelocityChange, maxVelocityChange);
        velocityChange.z = Mathf.Clamp(velocityChange.z, -maxVelocityChange, maxVelocityChange);
        velocityChange.y = 0f;

        _rb.AddForce(velocityChange, ForceMode.VelocityChange);
    }

    void HandleLook()
    {
        if (!cameraCanMove || playerCamera == null)
            return;

        Vector2 mouse = Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
        Vector2 stick = Gamepad.current != null ? Gamepad.current.rightStick.ReadValue() : Vector2.zero;

        float lookX;
        float lookY;

        if (mouse.sqrMagnitude > 0.0001f)
        {
            lookX = mouse.x * MouseAxisScale * mouseSensitivity;
            lookY = mouse.y * MouseAxisScale * mouseSensitivity;
        }
        else
        {
            lookX = stick.x * mouseSensitivity * 1.5f * Time.deltaTime * 60f;
            lookY = stick.y * mouseSensitivity * 1.5f * Time.deltaTime * 60f;
        }

        _yaw = transform.localEulerAngles.y + lookX;

        if (!invertCamera)
            _pitch -= lookY;
        else
            _pitch += lookY;

        _pitch = Mathf.Clamp(_pitch, -maxLookAngle, maxLookAngle);

        transform.localEulerAngles = new Vector3(0f, _yaw, 0f);
        playerCamera.transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
    }

    void HandleJump()
    {
        if (!enableJump || !_isGrounded)
            return;

        if (_jump.WasPressedThisFrame())
            Jump();
    }

    void HandleCrouchHold()
    {
        if (!enableCrouch)
            return;

        // Hold Left Ctrl, or stock Crouch binding (C / gamepad).
        bool held = IsCrouchHeld();

        if (held && !_crouchHeldPrev && !_isCrouched)
            Crouch();
        else if (!held && _crouchHeldPrev && _isCrouched)
            Crouch();

        _crouchHeldPrev = held;
    }

    bool IsCrouchHeld()
    {
        if (_crouch != null && _crouch.IsPressed())
            return true;

        Keyboard keyboard = Keyboard.current;
        return keyboard != null && keyboard.leftCtrlKey.isPressed;
    }

    void CheckGround()
    {
        Vector3 origin = new Vector3(
            transform.position.x,
            transform.position.y - (transform.localScale.y * 0.5f),
            transform.position.z);
        float distance = 0.75f;
        _isGrounded = Physics.Raycast(origin, Vector3.down, distance, ~0, QueryTriggerInteraction.Ignore);
    }

    void Jump()
    {
        if (!_isGrounded)
            return;

        _rb.AddForce(0f, jumpPower, 0f, ForceMode.Impulse);
        _isGrounded = false;
    }

    void Crouch()
    {
        if (_isCrouched)
        {
            transform.localScale = new Vector3(_standingScale.x, _standingScale.y, _standingScale.z);
            _currentWalkSpeed = walkSpeed;
            _isCrouched = false;
        }
        else
        {
            transform.localScale = new Vector3(_standingScale.x, crouchHeight, _standingScale.z);
            _currentWalkSpeed = walkSpeed * speedReduction;
            _isCrouched = true;
        }
    }

    void HeadBob()
    {
        if (joint == null)
            return;

        if (_isWalking)
        {
            float speedFactor = _isCrouched ? bobSpeed * speedReduction : bobSpeed;
            _bobTimer += Time.deltaTime * speedFactor;
            joint.localPosition = new Vector3(
                _jointBasePos.x + Mathf.Sin(_bobTimer) * bobAmount.x,
                _jointBasePos.y + Mathf.Sin(_bobTimer) * bobAmount.y,
                _jointBasePos.z + Mathf.Sin(_bobTimer) * bobAmount.z);
        }
        else
        {
            _bobTimer = 0f;
            joint.localPosition = Vector3.Lerp(joint.localPosition, _jointBasePos, Time.deltaTime * bobSpeed);
        }
    }
}
