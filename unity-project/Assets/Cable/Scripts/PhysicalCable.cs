using System.Collections.Generic;
using UnityEngine;

public enum ReelState
{
    /// <summary>A: колодка закрыта, кабель не разматывается и не наматывается.</summary>
    Locked,
    /// <summary>B: растягивание, мотор разматывает кабель по следу игрока.</summary>
    Deploying,
    /// <summary>C: затягивание, мотор наматывает кабель обратно.</summary>
    Retracting
}

public enum CableDragMode
{
    /// <summary>Суммарное трение всего кабеля по земле постоянно и делится между сегментами, лежащими на земле.</summary>
    TotalConstant,
    /// <summary>У каждого сегмента одинаковое трение; суммарное трение растёт с длиной кабеля.</summary>
    PerSegmentConstant
}

public class PhysicalCable : MonoBehaviour
{
    [Header("Endpoints")]
    [Tooltip("Точка, где кабель закреплён (антенна). Если нет Rigidbody, он будет добавлен как kinematic.")]
    public Transform sourceAnchor;
    [Tooltip("Rigidbody игрока, к которому прикрепляется свободный конец кабеля.")]
    public Rigidbody playerBody;

    [Header("Attach Point on Player")]
    [Tooltip("Точка крепления кабеля на игроке в его локальных координатах.")]
    public Vector3 localAttachOffset = new Vector3(0f, -0.125f, -0.675f);

    [Header("Cable Geometry")]
    [Tooltip("Максимальная длина кабеля в метрах (вся длина, которую может выдать катушка).")]
    public float totalLength = 100f;
    [Tooltip("Номинальная длина одного сегмента, м. Число сегментов = totalLength / segmentLength. Меньше = гибче и дороже по физике.")]
    public float segmentLength = 0.25f;
    [Tooltip("Радиус капсулы сегмента, м. Определяет толщину коллайдера кабеля.")]
    public float segmentRadius = 0.035f;

    [Header("Cable Physics")]
    [Tooltip("Масса одного сегмента, кг.")]
    public float segmentMass = 1.5f;
    [Tooltip("Линейное сопротивление движению сегмента (Rigidbody.linearDamping). Больше = кабель сильнее гасит скорость и меньше скользит.")]
    public float drag = 1.0f;
    [Tooltip("Угловое сопротивление вращению сегмента (Rigidbody.angularDamping).")]
    public float angularDrag = 4.0f;

    [Header("Joint")]
    [Tooltip("Максимальный угол изгиба в стыке между соседними сегментами, градусы.")]
    public float angularLimit = 25f;
    [Tooltip("Жёсткость пружины, возвращающей стык в прямое положение. Больше = кабель жёстче.")]
    public float angularSpring = 80f;
    [Tooltip("Демпфирование изгиба стыка. Больше = колебания кабеля затухают быстрее.")]
    public float angularDamper = 12f;
    [Tooltip("Минимальная длина верёвки между концом кабеля и игроком (когда кабель втянут или заморожен), м.")]
    public float playerJointStretch = 0.08f;
    [Tooltip("Жёсткость соединения кабеля с игроком, когда верёвка достигла предела длины.")]
    public float playerJointSpring = 8000f;
    [Tooltip("Демпфирование соединения кабеля с игроком.")]
    public float playerJointDamper = 400f;

    [Header("Ground Friction")]
    [Tooltip("Как считается трение о землю. TotalConstant: суммарное трение кабеля не зависит от длины. " +
             "PerSegmentConstant: каждый сегмент трётся одинаково, общее трение растёт с длиной.")]
    public CableDragMode dragMode = CableDragMode.TotalConstant;
    [Tooltip("Только для TotalConstant: сила (Н), нужная чтобы тащить по земле весь кабель.")]
    public float totalDragForce = 120f;
    [Tooltip("Коэффициент трения. PerSegmentConstant: сила на сегмент = μ·m·g. TotalConstant: верхний предел силы на один сегмент.")]
    public float frictionCoefficient = 0.6f;
    [Tooltip("Скорость сегмента (м/с), ниже которой трение плавно спадает до нуля. Меньше = кабель «липче», но менее плавно останавливается.")]
    public float dragStickSpeed = 0.03f;
    [Tooltip("Запас луча вниз от центра сегмента сверх его радиуса, м. Сегмент с землёй под лучом считается лежащим на земле.")]
    public float dragProbeDistance = 0.05f;

    [Header("Deploy (State B)")]
    [Tooltip("Новый сегмент выкладывается, когда горизонтальное расстояние от конца кабеля до следа игрока достигает длины сегмента, умноженной на это значение.")]
    [Range(0.5f, 1f)] public float deployDistanceRatio = 0.9f;
    [Tooltip("Запас длины верёвки игрока в состоянии B сверх (высота крепления над землёй + длина сегмента), м.")]
    public float deployRopeExtra = 0.25f;
    [Tooltip("Максимальный поворот нового сегмента относительно предыдущего, доля от angularLimit. Меньше = плавнее изгибы при выкладке.")]
    [Range(0.1f, 1f)] public float maxBendFraction = 0.8f;
    [Tooltip("Проецировать точку следа игрока на землю, чтобы кабель ложился на пол, а не висел на высоте крепления.")]
    public bool layOnGround = true;
    [Tooltip("Слои земли для лучей укладки и трения. Указывайте только слой земли (не Everything), иначе луч попадёт в игрока и прочие объекты.")]
    public LayerMask layGroundMask = ~0;
    [Tooltip("Длина луча вниз от точки крепления для поиска земли при укладке, м.")]
    public float layRayDistance = 2f;
    [Tooltip("Сколько длины кабеля можно выложить на метр реального перемещения игрока. 1 = строго по пройденному пути, чуть больше даёт запас на изгибы.")]
    [Range(0.8f, 2f)] public float deployBudgetRatio = 1.2f;
    [Tooltip("Максимум накопленного запаса выкладки в сегментах. Защищает от выкладки кабеля «пачкой».")]
    [Min(1f)] public float deployBudgetCapSegments = 2f;

    [Header("Reel-in (State C)")]
    [Tooltip("Скорость, с которой верёвка игрока укорачивается при затягивании, м/с.")]
    public float reelInSpeed = 3f;
    [Tooltip("Сегмент втягивается, только если расстояние от него до игрока не больше длины сегмента × это значение. Иначе мотор ждёт, пока провисание выберется.")]
    public float retractStopStretch = 1.6f;
    [Tooltip("Минимальный интервал между втягиванием двух соседних сегментов, с. Определяет скорость наматывания.")]
    public float retractInterval = 0.1f;
    [Tooltip("Количество сегментов, которое остаётся после полного втягивания. При достижении этого числа затягивание прекращается.")]
    public int minActiveSegments = 2;

    [Header("Tension (ограничение движения игрока)")]
    [Tooltip("Сила в соединении с игроком (Н), с которой начинается натяжение (Tension01 > 0). Должна быть заметно выше силы трения кабеля при волочении.")]
    public float tensionForceStart = 250f;
    [Tooltip("Сила в соединении с игроком (Н), при которой натяжение считается максимальным (Tension01 = 1).")]
    public float tensionForceMax = 600f;
    [Tooltip("Скорость сглаживания натяжения. Больше = натяжение быстрее реагирует на изменение силы, но может дрожать.")]
    public float tensionSmoothing = 15f;

    [Header("Collision")]
    [Tooltip("Слой, на который помещаются сегменты кабеля. Выберите один выделенный слой. Он исключается из лучей трения и укладки.")]
    public LayerMask cableLayer;

    [Header("Retract Bend")]
    [Tooltip("Жёсткость изгиба стыков во время затягивания (C). Мягче обычной, чтобы кабель легче наматывался.")]
    public float retractAngularSpring = 15f;
    [Tooltip("Демпфирование изгиба стыков во время затягивания (C).")]
    public float retractAngularDamper = 4f;

    [Header("Debug")]
    [Tooltip("Показывать в углу экрана состояние катушки, силу в соединении с игроком и натяжение. Создаёт мусор, в профилировании выключать.")]
    public bool showDebug;

    [HideInInspector] public int segmentCount;

    private float deployBudget;
    private Vector3 lastPlayerFlatPos;
    private float playerLimit;
    private bool frozenAtMax;
    private float tension01;
    private float retractTimer;

    private Rigidbody[] allBodies;
    private CapsuleCollider[] allColliders;
    private GameObject[] allSegments;
    private ConfigurableJoint[] chainJoints;
    private ConfigurableJoint playerJoint;
    private Rigidbody sourceRb;
    private Collider[] playerColliders;

    private PhysicsMaterial cableMaterial;
    private bool[] groundedFlags;
    private int groundProbeMask;
    private readonly List<int> interpRestore = new List<int>();

    private int activeCount;
    private float actualSegLen;
    private bool initialized;

    private float peakForce;
    private float lastSegDrag;
    private int lastGroundedCount;

    /// <summary>Текущее состояние катушки.</summary>
    public ReelState State { get; private set; } = ReelState.Locked;

    /// <summary>Количество активных (выложенных) сегментов.</summary>
    public int ActiveCount => activeCount;

    /// <summary>Длина выложенного кабеля по номинальной длине сегментов, м.</summary>
    public float ActiveMaxLength => activeCount * actualSegLen;

    /// <summary>Кабель полностью втянут (осталось не больше minActiveSegments).</summary>
    public bool IsFullyRetracted => activeCount <= minActiveSegments;

    /// <summary>Нормализованное натяжение от 0 до 1.</summary>
    public float Tension01 => tension01;

    /// <summary>
    /// Катушка не может выдавать кабель: состояние A либо B при исчерпанной длине.
    /// Именно тогда движение игрока ограничивается по натяжению.
    /// </summary>
    public bool IsReelLocked =>
        State == ReelState.Locked ||
        (State == ReelState.Deploying && activeCount >= segmentCount);

    /// <summary>Максимальное число точек линии: все сегменты плюс sourceAnchor и точка крепления игрока.</summary>
    public int MaxPossiblePoints => segmentCount + 2;

    /// <summary>Строит кабель при старте сцены.</summary>
    private void Start()
    {
        BuildCable();
    }

    /// <summary>Уничтожает созданный в коде материал кабеля.</summary>
    private void OnDestroy()
    {
        if (cableMaterial != null)
            Destroy(cableMaterial);
    }

    /// <summary>
    /// Создаёт пул сегментов на складе, затем активирует начальный отрезок кабеля
    /// от sourceAnchor до игрока и крепит его к игроку.
    /// </summary>
    [ContextMenu("Rebuild Cable")]
    public void BuildCable()
    {
        ClearChildren();

        State = ReelState.Locked;
        playerLimit = playerJointStretch;
        frozenAtMax = false;
        tension01 = 0f;

        segmentCount = Mathf.Max(2, Mathf.RoundToInt(totalLength / segmentLength));
        actualSegLen = totalLength / segmentCount;

        allBodies = new Rigidbody[segmentCount];
        allColliders = new CapsuleCollider[segmentCount];
        allSegments = new GameObject[segmentCount];
        chainJoints = new ConfigurableJoint[segmentCount];
        groundedFlags = new bool[segmentCount];

        groundProbeMask = layGroundMask.value & ~cableLayer.value;

        int layerIndex = ResolveLayerIndex(cableLayer);

        EnsureSourceRigidbody();
        EnsureCableMaterial();
        CachePlayerColliders();

        Vector3 storePos = GetStorePosition();

        for (int i = 0; i < segmentCount; i++)
        {
            GameObject seg = new GameObject($"CableSeg_{i:D3}");
            seg.transform.SetParent(transform);
            seg.transform.position = storePos;
            seg.layer = layerIndex;
            allSegments[i] = seg;

            Rigidbody rb = seg.AddComponent<Rigidbody>();
            rb.mass = segmentMass;
            rb.linearDamping = drag;
            rb.angularDamping = angularDrag;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.isKinematic = true;
            allBodies[i] = rb;

            CapsuleCollider col = seg.AddComponent<CapsuleCollider>();
            col.direction = 2;
            col.radius = segmentRadius;
            col.height = actualSegLen + segmentRadius * 2f;
            col.sharedMaterial = cableMaterial;
            col.enabled = false;
            allColliders[i] = col;
        }

        for (int i = 0; i < segmentCount; i++)
            IgnorePlayerFor(i);

        activeCount = 0;

        Vector3 start = sourceAnchor != null ? sourceAnchor.position : transform.position;
        Vector3 end = playerBody != null
            ? playerBody.transform.TransformPoint(localAttachOffset)
            : start + Vector3.forward * 2f;

        float distToPlayer = Vector3.Distance(start, end);

        int needed = Mathf.Clamp(
            Mathf.CeilToInt(distToPlayer / actualSegLen),
            3,
            segmentCount
        );

        Vector3 dir = end - start;
        if (dir.sqrMagnitude > 0.001f) dir.Normalize();
        else dir = Vector3.forward;

        Quaternion rot = Quaternion.LookRotation(dir);

        for (int i = 0; i < needed; i++)
        {
            Vector3 pos = start + dir * (actualSegLen * (i + 0.5f));
            ActivateSegment(i, pos, rot);
        }

        AttachPlayerJoint();
        initialized = true;
    }

    /// <summary>Возвращает индекс первого установленного слоя в маске (0, если маска пуста).</summary>
    private int ResolveLayerIndex(LayerMask mask)
    {
        int value = mask.value;
        for (int i = 0; i < 32; i++)
            if ((value & (1 << i)) != 0)
                return i;
        return 0;
    }

    /// <summary>Каждый физ. шаг выполняет логику текущего состояния катушки, применяет трение и обновляет натяжение.</summary>
    private void FixedUpdate()
    {
        if (!initialized || allBodies == null || playerBody == null)
            return;

        RestoreInterpolation();

        switch (State)
        {
            case ReelState.Deploying:
            {
                AccumulateDeployBudget();
                Vector3 target = GetLayTarget();
                if (!frozenAtMax)
                    SetPlayerLimit(GetDeployRopeLimit(target));
                TryDeploy(target);
                break;
            }

            case ReelState.Retracting:
                if (playerLimit > playerJointStretch)
                {
                    SetPlayerLimit(Mathf.MoveTowards(
                        playerLimit, playerJointStretch, reelInSpeed * Time.fixedDeltaTime));
                }
                TryManualRetract();
                break;
        }

        ApplyGroundDrag();
        UpdateTension();
    }

    /// <summary>Возвращает интерполяцию сегментам, у которых она была отключена на время активации.</summary>
    private void RestoreInterpolation()
    {
        if (interpRestore.Count == 0)
            return;

        for (int i = 0; i < interpRestore.Count; i++)
            allBodies[interpRestore[i]].interpolation = RigidbodyInterpolation.Interpolate;

        interpRestore.Clear();
    }

    /// <summary>
    /// Переключает состояние катушки (вызывается из RigidbodyFPS): меняет жёсткость изгиба
    /// и настраивает лимит верёвки игрока под новое состояние.
    /// </summary>
    public void SetReelState(ReelState newState)
    {
        if (newState == State)
            return;

        ReelState prev = State;
        State = newState;
        retractTimer = 0f;
        frozenAtMax = false;

        bool nowRetracting = newState == ReelState.Retracting;
        if (nowRetracting != (prev == ReelState.Retracting))
            ApplyBendDrive(nowRetracting);

        if (!initialized || playerBody == null)
            return;

        switch (newState)
        {
            case ReelState.Locked:
            case ReelState.Retracting:
                FreezeCurrentGap();
                break;

            case ReelState.Deploying:
                deployBudget = actualSegLen * 0.5f;
                lastPlayerFlatPos = playerBody.position;
                lastPlayerFlatPos.y = 0f;
                SetPlayerLimit(GetDeployRopeLimit(GetLayTarget()));
                break;
        }
    }

    /// <summary>Задаёт максимальную длину верёвки между концом кабеля и игроком (linearLimit соединения с игроком).</summary>
    private void SetPlayerLimit(float limit)
    {
        if (playerJoint != null && Mathf.Abs(limit - playerLimit) < 0.005f)
            return;

        playerLimit = limit;

        if (playerJoint != null)
            playerJoint.linearLimit = new SoftJointLimit { limit = limit };
    }

    /// <summary>
    /// Фиксирует текущее расстояние от конца кабеля до игрока как длину верёвки.
    /// Это реальный зазор, поэтому рывка не возникает.
    /// </summary>
    private void FreezeCurrentGap()
    {
        float limit = playerJointStretch;

        if (playerBody != null && activeCount > 0)
        {
            float gap = Vector3.Distance(GetLastFrontEnd(), GetAttachWorld());
            limit = Mathf.Max(gap, playerJointStretch);
        }

        SetPlayerLimit(limit);
    }

    /// <summary>Считает лимит верёвки в состоянии B: высота крепления над точкой укладки + длина сегмента + запас.</summary>
    private float GetDeployRopeLimit(Vector3 layTarget)
    {
        float height = Mathf.Max(0f, GetAttachWorld().y - layTarget.y);
        return height + actualSegLen + deployRopeExtra;
    }

    /// <summary>Создаёт параметры привода изгиба стыка: мягкие для затягивания или обычные.</summary>
    private JointDrive CreateBendDrive(bool soft)
    {
        return new JointDrive
        {
            positionSpring = soft ? retractAngularSpring : angularSpring,
            positionDamper = soft ? retractAngularDamper : angularDamper,
            maximumForce = float.MaxValue
        };
    }

    /// <summary>Переключает жёсткость изгиба у всех активных стыков цепи.</summary>
    private void ApplyBendDrive(bool soft)
    {
        if (chainJoints == null)
            return;

        JointDrive drive = CreateBendDrive(soft);

        for (int i = 0; i < activeCount; i++)
        {
            ConfigurableJoint j = chainJoints[i];
            if (j == null) continue;

            j.angularXDrive = drive;
            j.angularYZDrive = drive;
        }
    }

    /// <summary>
    /// Прикладывает силу трения к сегментам, лежащим на земле, против их горизонтальной скорости.
    /// Трение полностью моделируется силой, материал кабеля при этом без трения.
    /// </summary>
    private void ApplyGroundDrag()
    {
        if (activeCount < 1 || groundedFlags == null)
            return;

        float probeLen = segmentRadius + dragProbeDistance;

        int grounded = 0;
        for (int i = 0; i < activeCount; i++)
        {
            groundedFlags[i] = Physics.Raycast(
                allBodies[i].position, Vector3.down, probeLen,
                groundProbeMask, QueryTriggerInteraction.Ignore);

            if (groundedFlags[i]) grounded++;
        }

        lastGroundedCount = grounded;

        if (grounded == 0)
        {
            lastSegDrag = 0f;
            return;
        }

        float physicalMax = frictionCoefficient * segmentMass * Physics.gravity.magnitude;
        float perSeg = dragMode == CableDragMode.TotalConstant
            ? Mathf.Min(totalDragForce / grounded, physicalMax)
            : physicalMax;

        lastSegDrag = perSeg;

        float dt = Time.fixedDeltaTime;
        float stick = Mathf.Max(0.0001f, dragStickSpeed);

        for (int i = 0; i < activeCount; i++)
        {
            if (!groundedFlags[i]) continue;

            Rigidbody rb = allBodies[i];
            if (rb.IsSleeping()) continue;

            Vector3 v = rb.linearVelocity;
            v.y = 0f;

            float speed = v.magnitude;
            if (speed < 1e-4f) continue;

            float mag = perSeg * Mathf.Min(1f, speed / stick);
            mag = Mathf.Min(mag, rb.mass * speed / dt);

            rb.AddForce(-v / speed * mag, ForceMode.Force);
        }
    }

    /// <summary>
    /// Состояние B: выкладывает новый сегмент, когда конец кабеля отстал от следа игрока
    /// и хватает бюджета длины. При исчерпании длины замораживает верёвку.
    /// </summary>
    private void TryDeploy(Vector3 target)
    {
        if (activeCount < 1)
            return;

        if (activeCount >= segmentCount)
        {
            if (!frozenAtMax)
            {
                FreezeCurrentGap();
                frozenAtMax = true;
            }
            return;
        }

        int lastIdx = activeCount - 1;
        Rigidbody lastRb = allBodies[lastIdx];
        Vector3 lastForward = lastRb.rotation * Vector3.forward;

        Vector3 joinPoint = lastRb.position + lastForward * (actualSegLen * 0.5f);
        Vector3 delta = target - joinPoint;

        float flatDist = new Vector2(delta.x, delta.z).magnitude;
        if (flatDist < actualSegLen * deployDistanceRatio)
            return;

        if (deployBudget < actualSegLen)
            return;

        Vector3 dir = delta.normalized;

        float maxAngleRad = angularLimit * maxBendFraction * Mathf.Deg2Rad;
        dir = Vector3.RotateTowards(lastForward, dir, maxAngleRad, 0f);

        Vector3 newPos = joinPoint + dir * (actualSegLen * 0.5f);
        Quaternion rot = Quaternion.LookRotation(dir);

        DestroyPlayerJoint();
        ActivateSegment(activeCount, newPos, rot);
        AttachPlayerJoint();

        deployBudget -= actualSegLen;
    }

    /// <summary>Возвращает точку, куда должен лечь кабель: проекцию точки крепления на землю или саму точку крепления.</summary>
    private Vector3 GetLayTarget()
    {
        Vector3 attach = GetAttachWorld();

        if (!layOnGround)
            return attach;

        if (Physics.Raycast(attach, Vector3.down, out RaycastHit hit,
                            layRayDistance, groundProbeMask, QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * segmentRadius;
        }

        return attach;
    }

    /// <summary>Накапливает бюджет выкладки по реальному горизонтальному перемещению игрока, с ограничением сверху.</summary>
    private void AccumulateDeployBudget()
    {
        Vector3 p = playerBody.position;
        p.y = 0f;

        float moved = Vector3.Distance(p, lastPlayerFlatPos);
        lastPlayerFlatPos = p;

        deployBudget = Mathf.Min(
            deployBudget + moved * deployBudgetRatio,
            actualSegLen * deployBudgetCapSegments);
    }

    /// <summary>
    /// Состояние C: по таймеру убирает последний сегмент, если провисание уже выбрано
    /// и осталось больше минимального числа сегментов.
    /// </summary>
    private void TryManualRetract()
    {
        if (activeCount <= minActiveSegments)
            return;

        int lastIdx = activeCount - 1;
        Vector3 lastPos = allBodies[lastIdx].position;
        Vector3 attachWorld = GetAttachWorld();

        float stretch = Vector3.Distance(lastPos, attachWorld);
        float stopThreshold = actualSegLen * retractStopStretch;

        if (stretch > stopThreshold)
            return;

        retractTimer += Time.fixedDeltaTime;
        if (retractTimer < retractInterval)
            return;

        retractTimer = 0f;

        DestroyPlayerJoint();
        DeactivateSegment(lastIdx);
        AttachPlayerJoint();
    }

    /// <summary>Считает сглаженное натяжение 0..1 по силе в соединении с игроком.</summary>
    private void UpdateTension()
    {
        float raw = 0f;

        if (playerJoint != null)
        {
            float force = playerJoint.currentForce.magnitude;
            raw = Mathf.InverseLerp(tensionForceStart, tensionForceMax, force);
        }

        float k = 1f - Mathf.Exp(-tensionSmoothing * Time.fixedDeltaTime);
        tension01 = Mathf.Lerp(tension01, raw, k);
    }

    /// <summary>
    /// Достаёт сегмент со склада и ставит его в заданную позу, затем соединяет с предыдущим сегментом
    /// (или с sourceAnchor для первого). Интерполяция отключается на один шаг, чтобы не было «полёта» со склада.
    /// </summary>
    private void ActivateSegment(int index, Vector3 position, Quaternion rotation)
    {
        Rigidbody rb = allBodies[index];

        rb.interpolation = RigidbodyInterpolation.None;
        interpRestore.Add(index);

        allSegments[index].transform.SetPositionAndRotation(position, rotation);

        rb.position = position;
        rb.rotation = rotation;
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        groundedFlags[index] = false;
        allColliders[index].enabled = true;
        IgnorePlayerFor(index);

        Rigidbody connectedBody = (index == 0) ? sourceRb : allBodies[index - 1];

        ConfigurableJoint j = allSegments[index].AddComponent<ConfigurableJoint>();

        if (index == 0)
            SetupSourceJoint(j);
        else
            SetupChainJoint(j, connectedBody);

        chainJoints[index] = j;

        if (index >= activeCount)
            activeCount = index + 1;

        IgnoreNeighborCollision(index);
    }

    /// <summary>Убирает сегмент: удаляет его джойнт, отключает коллайдер и возвращает на склад.</summary>
    private void DeactivateSegment(int index)
    {
        if (chainJoints[index] != null)
        {
            chainJoints[index].connectedBody = null;
            DestroyImmediate(chainJoints[index]);
            chainJoints[index] = null;
        }

        Rigidbody rb = allBodies[index];
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;
        rb.isKinematic = true;

        groundedFlags[index] = false;
        allColliders[index].enabled = false;

        Vector3 storePos = GetStorePosition();
        rb.position = storePos;
        allSegments[index].transform.position = storePos;

        activeCount = index;
    }

    /// <summary>Возвращает позицию склада неактивных сегментов: на 100 м выше источника.</summary>
    private Vector3 GetStorePosition()
    {
        return (sourceAnchor != null ? sourceAnchor.position : transform.position)
             + Vector3.up * 100f;
    }

    /// <summary>Настраивает джойнт между соседними сегментами: фиксированные позиции, ограниченный изгиб с пружиной.</summary>
    private void SetupChainJoint(ConfigurableJoint j, Rigidbody connected)
    {
        j.connectedBody = connected;
        j.anchor = new Vector3(0, 0, -actualSegLen * 0.5f);
        j.connectedAnchor = new Vector3(0, 0, actualSegLen * 0.5f);
        j.autoConfigureConnectedAnchor = false;

        j.xMotion = ConfigurableJointMotion.Locked;
        j.yMotion = ConfigurableJointMotion.Locked;
        j.zMotion = ConfigurableJointMotion.Locked;

        j.angularXMotion = ConfigurableJointMotion.Limited;
        j.angularYMotion = ConfigurableJointMotion.Limited;
        j.angularZMotion = ConfigurableJointMotion.Limited;

        j.lowAngularXLimit = new SoftJointLimit { limit = -angularLimit };
        j.highAngularXLimit = new SoftJointLimit { limit = angularLimit };
        j.angularYLimit = new SoftJointLimit { limit = angularLimit };
        j.angularZLimit = new SoftJointLimit { limit = angularLimit };

        JointDrive drive = CreateBendDrive(State == ReelState.Retracting);
        j.angularXDrive = drive;
        j.angularYZDrive = drive;
        j.enablePreprocessing = false;
    }

    /// <summary>Настраивает джойнт первого сегмента с sourceAnchor: позиция зафиксирована, вращение свободно.</summary>
    private void SetupSourceJoint(ConfigurableJoint j)
    {
        j.connectedBody = sourceRb;
        j.anchor = new Vector3(0, 0, -actualSegLen * 0.5f);
        j.connectedAnchor = Vector3.zero;
        j.autoConfigureConnectedAnchor = false;

        j.xMotion = ConfigurableJointMotion.Locked;
        j.yMotion = ConfigurableJointMotion.Locked;
        j.zMotion = ConfigurableJointMotion.Locked;

        j.angularXMotion = ConfigurableJointMotion.Free;
        j.angularYMotion = ConfigurableJointMotion.Free;
        j.angularZMotion = ConfigurableJointMotion.Free;

        j.enablePreprocessing = false;
    }

    /// <summary>Создаёт соединение последнего активного сегмента с игроком в виде верёвки с текущим лимитом длины.</summary>
    private void AttachPlayerJoint()
    {
        DestroyPlayerJoint();

        if (playerBody == null || activeCount < 1)
            return;

        int lastIdx = activeCount - 1;

        playerJoint = allSegments[lastIdx].AddComponent<ConfigurableJoint>();
        playerJoint.connectedBody = playerBody;
        playerJoint.anchor = new Vector3(0, 0, actualSegLen * 0.5f);
        playerJoint.connectedAnchor = localAttachOffset;
        playerJoint.autoConfigureConnectedAnchor = false;

        playerJoint.xMotion = ConfigurableJointMotion.Limited;
        playerJoint.yMotion = ConfigurableJointMotion.Limited;
        playerJoint.zMotion = ConfigurableJointMotion.Limited;

        playerJoint.linearLimit = new SoftJointLimit { limit = playerLimit };
        playerJoint.linearLimitSpring = new SoftJointLimitSpring
        {
            spring = playerJointSpring,
            damper = playerJointDamper
        };

        playerJoint.angularXMotion = ConfigurableJointMotion.Free;
        playerJoint.angularYMotion = ConfigurableJointMotion.Free;
        playerJoint.angularZMotion = ConfigurableJointMotion.Free;

        playerJoint.enablePreprocessing = false;
    }

    /// <summary>Удаляет соединение кабеля с игроком, если оно существует.</summary>
    private void DestroyPlayerJoint()
    {
        if (playerJoint != null)
        {
            playerJoint.connectedBody = null;
            DestroyImmediate(playerJoint);
            playerJoint = null;
        }
    }

    /// <summary>Гарантирует наличие Rigidbody (kinematic) на sourceAnchor, чтобы к нему можно было подключить джойнт.</summary>
    private void EnsureSourceRigidbody()
    {
        if (sourceAnchor == null) return;

        sourceRb = sourceAnchor.GetComponent<Rigidbody>();

        if (sourceRb == null)
        {
            sourceRb = sourceAnchor.gameObject.AddComponent<Rigidbody>();
            sourceRb.isKinematic = true;
            sourceRb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    /// <summary>
    /// Создаёт один раз материал кабеля без трения (режим Minimum).
    /// Всё трение кабеля моделируется силой в ApplyGroundDrag.
    /// </summary>
    private void EnsureCableMaterial()
    {
        if (cableMaterial != null)
            return;

        cableMaterial = new PhysicsMaterial("Cable_Frictionless")
        {
            dynamicFriction = 0f,
            staticFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };
    }

    /// <summary>Запоминает все коллайдеры игрока для отключения столкновений с кабелем.</summary>
    private void CachePlayerColliders()
    {
        playerColliders = playerBody != null
            ? playerBody.GetComponentsInChildren<Collider>()
            : null;
    }

    /// <summary>Отключает столкновения сегмента со всеми коллайдерами игрока.</summary>
    private void IgnorePlayerFor(int index)
    {
        if (playerColliders == null) return;

        foreach (Collider pc in playerColliders)
            if (pc != null)
                Physics.IgnoreCollision(allColliders[index], pc);
    }

    /// <summary>Отключает столкновения сегмента с двумя соседями с каждой стороны, чтобы цепь не распиралась.</summary>
    private void IgnoreNeighborCollision(int index)
    {
        Collider col = allColliders[index];

        for (int offset = 1; offset <= 2; offset++)
        {
            int n = index - offset;
            if (n >= 0 && allColliders[n].enabled)
                Physics.IgnoreCollision(col, allColliders[n]);

            n = index + offset;
            if (n < segmentCount && allColliders[n].enabled)
                Physics.IgnoreCollision(col, allColliders[n]);
        }
    }

    /// <summary>Удаляет все сегменты-дети и сбрасывает внутреннее состояние перед пересборкой.</summary>
    private void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        allBodies = null;
        allSegments = null;
        allColliders = null;
        chainJoints = null;
        groundedFlags = null;
        playerJoint = null;
        interpRestore.Clear();
        activeCount = 0;
        initialized = false;
    }

    /// <summary>Возвращает мировую позицию точки крепления кабеля на игроке.</summary>
    private Vector3 GetAttachWorld()
    {
        return playerBody.transform.TransformPoint(localAttachOffset);
    }

    /// <summary>Возвращает передний конец последнего активного сегмента по физическому состоянию.</summary>
    private Vector3 GetLastFrontEnd()
    {
        Rigidbody last = allBodies[activeCount - 1];
        return last.position + last.rotation * new Vector3(0f, 0f, actualSegLen * 0.5f);
    }

    /// <summary>Возвращает фактическую длину кабеля по ломаной: sourceAnchor, центры сегментов, точка крепления игрока.</summary>
    public float GetChainLength()
    {
        if (allBodies == null || activeCount < 1)
            return 0f;

        float len = 0f;

        Vector3 prev = sourceAnchor != null
            ? sourceAnchor.position
            : allBodies[0].position;

        for (int i = 0; i < activeCount; i++)
        {
            len += Vector3.Distance(prev, allBodies[i].position);
            prev = allBodies[i].position;
        }

        if (playerBody != null)
            len += Vector3.Distance(prev, GetAttachWorld());

        return len;
    }

    /// <summary>
    /// Возвращает горизонтальное направление, в котором кабель тянет игрока (от точки крепления к кабелю).
    /// Движение против этого направления увеличивает натяжение.
    /// </summary>
    public Vector3 GetPullDirectionXZ()
    {
        if (allBodies == null || activeCount < 1 || playerBody == null)
            return Vector3.zero;

        Vector3 attach = GetAttachWorld();

        Vector3 dir = GetLastFrontEnd() - attach;
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.01f)
        {
            dir = allBodies[activeCount - 1].position - attach;
            dir.y = 0f;
        }

        return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;
    }

    /// <summary>
    /// Заполняет buffer позициями: sourceAnchor, все активные сегменты, точка крепления игрока.
    /// Используются интерполированные позиции transform. Размер buffer должен быть не меньше MaxPossiblePoints.
    /// Возвращает число записанных точек.
    /// </summary>
    public int GetActivePositions(Vector3[] buffer)
    {
        if (allBodies == null || activeCount < 1)
            return 0;

        int count = 0;

        if (sourceAnchor != null)
            buffer[count++] = sourceAnchor.position;

        for (int i = 0; i < activeCount; i++)
            buffer[count++] = allSegments[i].transform.position;

        if (playerBody != null)
            buffer[count++] = playerBody.transform.TransformPoint(localAttachOffset);

        return count;
    }

    /// <summary>Выводит на экран отладочную строку с состоянием катушки, силой и натяжением (при включённом showDebug).</summary>
    private void OnGUI()
    {
        if (!showDebug || playerJoint == null) return;

        float f = playerJoint.currentForce.magnitude;
        peakForce = Mathf.Max(peakForce * 0.995f, f);

        GUI.Label(new Rect(10, 10, 1000, 24),
            $"{State}  seg {activeCount}/{segmentCount}  F={f:F0} Н  peak={peakForce:F0}  " +
            $"t={tension01:F2}  limit={playerLimit:F2}  drag/seg={lastSegDrag:F2} Н  на земле={lastGroundedCount}");
    }
}
