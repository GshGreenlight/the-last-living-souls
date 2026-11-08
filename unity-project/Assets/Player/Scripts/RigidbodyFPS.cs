using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(CapsuleCollider))]
public class RigidbodyFPS : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Объект камеры (или её родитель), который наклоняется вверх/вниз. Поворот влево/вправо выполняет сам игрок.")]
    public Transform cameraPivot;
    [Tooltip("Физический кабель, привязанный к игроку. Может быть пустым, тогда управление катушкой отключено.")]
    public PhysicalCable cable;

    [Header("Input")]
    [Tooltip("Действие движения (Vector2): WASD или стик.")]
    public InputActionReference moveAction;
    [Tooltip("Действие обзора (Vector2): движение мыши или правый стик.")]
    public InputActionReference lookAction;

    [Header("Movement")]
    [Tooltip("Максимальная горизонтальная скорость, м/с.")]
    public float maxSpeed = 4f;
    [Tooltip("Ускорение при наличии ввода, м/с². Время разгона до maxSpeed ≈ maxSpeed / acceleration.")]
    public float acceleration = 20f;
    [Tooltip("Торможение при отсутствии ввода, м/с².")]
    public float deceleration = 40f;

    [Header("Ground Check")]
    [Tooltip("Радиус сферы проверки земли под ногами, м.")]
    public float groundCheckRadius = 0.25f;
    [Tooltip("Дополнительная дистанция проверки земли ниже ног, м.")]
    public float groundCheckDistance = 0.15f;
    [Tooltip("Слои, считающиеся землёй для проверки.")]
    public LayerMask groundMask = ~0;

    [Header("Look")]
    [Tooltip("Чувствительность мыши (градусов на единицу ввода).")]
    public float mouseSensitivity = 0.1f;
    [Tooltip("Минимальный угол наклона камеры (вниз), градусы.")]
    public float minPitch = -80f;
    [Tooltip("Максимальный угол наклона камеры (вверх), градусы.")]
    public float maxPitch = 80f;

    [Header("Cable Reel Input")]
    [Tooltip("Кнопка «Растягивание». Вместе с WASD открывает колодку и разматывает катушку (состояние B).")]
    public InputActionReference extendAction;
    [Tooltip("Кнопка «Затягивание». Пока зажата, колодка открыта и провод наматывается (состояние C). Приоритет выше, чем у B.")]
    public InputActionReference retractAction;

    [Header("Cable Tension Limit (состояние A, и B при исчерпанной длине)")]
    [Tooltip("Жёсткость «стены» при натяжении. Доля запрещённого движения от кабеля = clamp01(натяжение × это значение). " +
             "1 = мягко (полный запрет только при максимальном натяжении), 2–3 = жёсткая стена.")]
    [Range(1f, 4f)] public float tensionBlockSharpness = 2f;

    [Header("Reel Events")]
    [Tooltip("Вызывается при входе в состояние B (начало разматывания).")]
    public UnityEvent onDeployStart;
    [Tooltip("Вызывается при выходе из состояния B.")]
    public UnityEvent onDeployEnd;
    [Tooltip("Вызывается при входе в состояние C (начало затягивания).")]
    public UnityEvent onRetractStart;
    [Tooltip("Вызывается при выходе из состояния C.")]
    public UnityEvent onRetractEnd;

    private ReelState reelState = ReelState.Locked;
    private Rigidbody rb;
    private Collider ownCollider;
    private readonly Collider[] groundCheckResults = new Collider[8];
    private float pitch;
    private float pendingYaw;
    private bool grounded;

    /// <summary>Текущее состояние катушки.</summary>
    public ReelState CurrentReelState => reelState;

    /// <summary>Идёт затягивание кабеля (состояние C).</summary>
    public bool IsRetracting => reelState == ReelState.Retracting;

    /// <summary>Идёт разматывание кабеля (состояние B).</summary>
    public bool IsDeploying => reelState == ReelState.Deploying;

    /// <summary>Настраивает Rigidbody игрока, считывает начальный наклон камеры и захватывает курсор.</summary>
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

    /// <summary>Включает все используемые действия ввода.</summary>
    private void OnEnable()
    {
        moveAction?.action.Enable();
        lookAction?.action.Enable();
        extendAction?.action.Enable();
        retractAction?.action.Enable();
    }

    /// <summary>Выключает все используемые действия ввода.</summary>
    private void OnDisable()
    {
        moveAction?.action.Disable();
        lookAction?.action.Disable();
        extendAction?.action.Disable();
        retractAction?.action.Disable();
    }

    /// <summary>Каждый кадр обрабатывает обзор мышью и ввод управления катушкой.</summary>
    private void Update()
    {
        HandleLookInput();
        HandleReelInput();
    }

    /// <summary>
    /// Каждый физ. шаг проверяет землю, применяет накопленный поворот и двигает игрока.
    /// При затягивании кабеля игрок стоит на месте.
    /// </summary>
    private void FixedUpdate()
    {
        CheckGround();

        if (pendingYaw != 0f)
        {
            Quaternion deltaRot = Quaternion.Euler(0f, pendingYaw, 0f);
            rb.MoveRotation(rb.rotation * deltaRot);
            pendingYaw = 0f;
        }

        if (IsRetracting)
        {
            Vector3 vel = rb.linearVelocity;
            rb.linearVelocity = new Vector3(0f, vel.y, 0f);
        }
        else
        {
            HandleMovement();
        }
    }

    /// <summary>Считывает ввод мыши: копит поворот корпуса по горизонтали и сразу наклоняет камеру по вертикали.</summary>
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

    /// <summary>Определяет, стоит ли игрок на земле, по пересечению сферы под ногами с коллайдерами слоя groundMask.</summary>
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

    /// <summary>
    /// Двигает игрока: разгоняет или тормозит горизонтальную скорость к желаемой
    /// и передаёт её в физику силой. В воздухе управление отсутствует.
    /// </summary>
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

        Vector3 wishVel = wishDir * maxSpeed;
        float rate = hasInput ? acceleration : deceleration;

        Vector3 newHVel = hVel;

        if (grounded)
        {
            newHVel = Vector3.MoveTowards(hVel, wishVel, rate * Time.fixedDeltaTime);
            newHVel = LimitByCableTension(newHVel);
        }

        Vector3 deltaV = newHVel - hVel;
        Vector3 forceNeeded = deltaV * rb.mass / Time.fixedDeltaTime;

        rb.AddForce(forceNeeded, ForceMode.Force);
    }

    /// <summary>
    /// Убирает из горизонтальной скорости составляющую, направленную от кабеля.
    /// Доля убираемого = clamp01(натяжение × tensionBlockSharpness).
    /// Движение к кабелю и по касательной остаётся свободным.
    /// </summary>
    private Vector3 LimitByCableTension(Vector3 hVel)
    {
        if (cable == null || !cable.IsReelLocked)
            return hVel;

        float block = Mathf.Clamp01(cable.Tension01 * tensionBlockSharpness);
        if (block <= 0.001f)
            return hVel;

        Vector3 pull = cable.GetPullDirectionXZ();
        if (pull.sqrMagnitude < 0.0001f)
            return hVel;

        Vector3 away = -pull;
        float along = Vector3.Dot(hVel, away);

        if (along <= 0f)
            return hVel;

        return hVel - away * (along * block);
    }

    /// <summary>Определяет по вводу нужное состояние катушки и применяет его.</summary>
    private void HandleReelInput()
    {
        if (cable == null)
            return;

        ApplyReelState(DetermineReelState());
    }

    /// <summary>
    /// Выбирает состояние катушки по вводу: C (затягивание, если кабель не втянут полностью),
    /// затем B (кнопка растягивания + движение), иначе A.
    /// </summary>
    private ReelState DetermineReelState()
    {
        if (retractAction != null &&
            retractAction.action.IsPressed() &&
            !cable.IsFullyRetracted)
        {
            return ReelState.Retracting;
        }

        if (extendAction != null && extendAction.action.IsPressed() && moveAction != null)
        {
            Vector2 move = moveAction.action.ReadValue<Vector2>();
            if (move.sqrMagnitude > 0.01f)
                return ReelState.Deploying;
        }

        return ReelState.Locked;
    }

    /// <summary>
    /// Переключает состояние катушки: уведомляет кабель, замораживает или размораживает игрока при затягивании
    /// и вызывает события выхода из старого и входа в новое состояние.
    /// </summary>
    private void ApplyReelState(ReelState next)
    {
        if (next == reelState)
            return;

        ReelState prev = reelState;
        reelState = next;

        cable.SetReelState(next);

        if (prev == ReelState.Retracting)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotation;
            onRetractEnd?.Invoke();
        }
        else if (prev == ReelState.Deploying)
        {
            onDeployEnd?.Invoke();
        }

        if (next == ReelState.Retracting)
        {
            rb.constraints = RigidbodyConstraints.FreezeRotation
                           | RigidbodyConstraints.FreezePositionX
                           | RigidbodyConstraints.FreezePositionZ;

            onRetractStart?.Invoke();
        }
        else if (next == ReelState.Deploying)
        {
            onDeployStart?.Invoke();
        }
    }
}
