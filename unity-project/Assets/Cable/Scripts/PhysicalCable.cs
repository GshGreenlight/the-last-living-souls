using UnityEngine;

public class PhysicalCable : MonoBehaviour
{
    [Header("Endpoints")]
    public Transform sourceAnchor;
    public Rigidbody playerBody;

    [Header("Attach Point on Player")]
    public Vector3 localAttachOffset = new Vector3(0f, 0.5f, -0.2f);

    [Header("Cable Geometry")]
    public float totalLength = 24f;
    public float segmentLength = 1f;
    public float segmentRadius = 0.035f;

    [Header("Cable Physics")]
    public float segmentMass = 1.5f;
    public float drag = 1.0f;
    public float angularDrag = 4.0f;

    [Header("Joint")]
    public float angularLimit = 25f;
    public float angularSpring = 80f;
    public float angularDamper = 12f;
    public float playerJointStretch = 0.08f;
    public float playerJointSpring = 8000f;
    public float playerJointDamper = 400f;


    [Header("Reel")]
    public float deployStretch = 0.55f;

    [Header("Manual Retract")]
    public float retractStopStretch = 0.6f;
    public float retractInterval = 0.1f;
    public int minActiveSegments = 2;

    [Header("Collision")]
    public LayerMask cableLayer;
    public PhysicsMaterial cablePhysicsMaterial;

    [Header("Retract Friction Override")]
    public float retractDynamicFriction = 0.02f;
    public float retractStaticFriction = 0.02f;

    [Header("Retract Bend Override")]
    public float retractAngularSpring = 15f;
    public float retractAngularDamper = 4f;

    private PhysicsMaterial runtimeCableMaterial;
    private float restDynamicFriction;
    private float restStaticFriction;
    private PhysicsMaterialCombine restFrictionCombine;

    [HideInInspector] public int segmentCount;

    private bool manualRetractActive;
    private float retractTimer;

    private Rigidbody[] allBodies;
    private CapsuleCollider[] allColliders;
    private GameObject[] allSegments;
    private ConfigurableJoint[] chainJoints;
    private ConfigurableJoint playerJoint;
    private Rigidbody sourceRb;

    private int activeCount;
    private float actualSegLen;
    private bool initialized;

    public int ActiveCount => activeCount;
    public float ActiveMaxLength => activeCount * actualSegLen;
    public bool IsFullyRetracted => activeCount <= minActiveSegments;

    // +2 для sourceAnchor и точки крепления игрока
    public int MaxPossiblePoints => segmentCount + 2;

    private void Start()
    {
        BuildCable();
    }

    [ContextMenu("Rebuild Cable")]
    public void BuildCable()
    {
        ClearChildren();

        segmentCount = Mathf.Max(2, Mathf.RoundToInt(totalLength / segmentLength));
        actualSegLen = totalLength / segmentCount;

        allBodies = new Rigidbody[segmentCount];
        allColliders = new CapsuleCollider[segmentCount];
        allSegments = new GameObject[segmentCount];
        chainJoints = new ConfigurableJoint[segmentCount];

        int layerIndex = ResolveLayerIndex(cableLayer);

        EnsureSourceRigidbody();
        EnsureRuntimeMaterial();

        Vector3 storePos = (sourceAnchor != null ? sourceAnchor.position : transform.position)
                         + Vector3.up * 100f;

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
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.isKinematic = true;
            allBodies[i] = rb;

            CapsuleCollider col = seg.AddComponent<CapsuleCollider>();
            col.direction = 2;
            col.radius = segmentRadius;
            col.height = actualSegLen + segmentRadius * 2f;
            if (cablePhysicsMaterial != null) col.material = runtimeCableMaterial;
            col.enabled = false;
            allColliders[i] = col;
        }

        IgnorePlayerCollisions();

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

        Vector3 dir = (end - start);
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

    private int ResolveLayerIndex(LayerMask mask)
    {
        int value = mask.value;
        for (int i = 0; i < 32; i++)
            if ((value & (1 << i)) != 0)
                return i;
        return 0;
    }

    private void FixedUpdate()
    {
        if (!initialized || allBodies == null || playerBody == null)
            return;

        if (manualRetractActive)
            TryManualRetract();
        else
            TryDeploy();
    }

    private bool TryDeploy()
    {
        if (activeCount >= segmentCount)
            return false;

        int lastIdx = activeCount - 1;
        Vector3 lastPos = allBodies[lastIdx].position;
        Vector3 attachWorld = playerBody.transform.TransformPoint(localAttachOffset);
        float dist = Vector3.Distance(lastPos, attachWorld);

        float threshold = actualSegLen * deployStretch;

        if (dist < threshold)
            return false;

        DestroyPlayerJoint();

        int newIdx = activeCount;

        Transform lastTr = allSegments[lastIdx].transform;
        Vector3 lastForward = lastTr.forward;

        Vector3 newPos = lastPos + lastForward * actualSegLen;
        Quaternion rot = lastTr.rotation;

        ActivateSegment(newIdx, newPos, rot);

        allBodies[newIdx].linearVelocity = allBodies[lastIdx].linearVelocity;
        allBodies[newIdx].angularVelocity = allBodies[lastIdx].angularVelocity;

        AttachPlayerJoint();
        return true;
    }

    public void SetManualRetract(bool active)
    {
        manualRetractActive = active;
        retractTimer = 0f;

        ApplyChainJointSoftness(active);

        if (runtimeCableMaterial == null)
            return;

        if (active)
        {
            runtimeCableMaterial.dynamicFriction = retractDynamicFriction;
            runtimeCableMaterial.staticFriction = retractStaticFriction;
            runtimeCableMaterial.frictionCombine = PhysicsMaterialCombine.Minimum;
        }
        else
        {
            runtimeCableMaterial.dynamicFriction = restDynamicFriction;
            runtimeCableMaterial.staticFriction = restStaticFriction;
            runtimeCableMaterial.frictionCombine = restFrictionCombine;
        }
    }

    private void ApplyChainJointSoftness(bool retracting)
    {
        if (chainJoints == null)
            return;

        float spring = retracting ? retractAngularSpring : angularSpring;
        float damper = retracting ? retractAngularDamper : angularDamper;

        JointDrive drive = new JointDrive
        {
            positionSpring = spring,
            positionDamper = damper,
            maximumForce = float.MaxValue
        };

        for (int i = 0; i < activeCount; i++)
        {
            if (chainJoints[i] == null) continue;

            chainJoints[i].angularXDrive = drive;
            chainJoints[i].angularYZDrive = drive;
        }
    }


    private void TryManualRetract()
    {
        if (activeCount <= minActiveSegments)
            return;

        int lastIdx = activeCount - 1;
        Vector3 lastPos = allBodies[lastIdx].position;
        Vector3 attachWorld = playerBody.transform.TransformPoint(localAttachOffset);

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

    private void ActivateSegment(int index, Vector3 position, Quaternion rotation)
    {
        allSegments[index].transform.position = position;
        allSegments[index].transform.rotation = rotation;

        Rigidbody rb = allBodies[index];
        rb.position = position;
        rb.isKinematic = false;
        rb.linearVelocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        allColliders[index].enabled = true;

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

        allColliders[index].enabled = false;

        Vector3 storePos = (sourceAnchor != null ? sourceAnchor.position : Vector3.zero)
                        + Vector3.up * 100f;

        rb.position = storePos;
        allSegments[index].transform.position = storePos;

        activeCount = index;
    }

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

        float spring = manualRetractActive ? retractAngularSpring : angularSpring;
        float damper = manualRetractActive ? retractAngularDamper : angularDamper;

        JointDrive drive = new JointDrive
        {
            positionSpring = spring,
            positionDamper = damper,
            maximumForce = float.MaxValue
        };

        j.angularXDrive = drive;
        j.angularYZDrive = drive;
        j.enablePreprocessing = false;
    }

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

        playerJoint.linearLimit = new SoftJointLimit { limit = playerJointStretch };
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

    private void DestroyPlayerJoint()
    {
        if (playerJoint != null)
        {
            playerJoint.connectedBody = null;
            DestroyImmediate(playerJoint);
            playerJoint = null;
        }
    }

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

    private void EnsureRuntimeMaterial()
    {
        if (cablePhysicsMaterial == null)
        {
            runtimeCableMaterial = null;
            return;
        }

        runtimeCableMaterial = new PhysicsMaterial(cablePhysicsMaterial.name + "_Runtime")
        {
            dynamicFriction = cablePhysicsMaterial.dynamicFriction,
            staticFriction = cablePhysicsMaterial.staticFriction,
            bounciness = cablePhysicsMaterial.bounciness,
            frictionCombine = cablePhysicsMaterial.frictionCombine,
            bounceCombine = cablePhysicsMaterial.bounceCombine
        };

        restDynamicFriction = runtimeCableMaterial.dynamicFriction;
        restStaticFriction = runtimeCableMaterial.staticFriction;
        restFrictionCombine = runtimeCableMaterial.frictionCombine;
    }

    private void IgnorePlayerCollisions()
    {
        if (playerBody == null) return;

        Collider[] playerCols = playerBody.GetComponentsInChildren<Collider>();

        for (int i = 0; i < segmentCount; i++)
            foreach (Collider pc in playerCols)
                Physics.IgnoreCollision(allColliders[i], pc);
    }

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

    private void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            DestroyImmediate(transform.GetChild(i).gameObject);

        allBodies = null;
        allSegments = null;
        allColliders = null;
        chainJoints = null;
        playerJoint = null;
        activeCount = 0;
        initialized = false;
    }

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
        {
            Vector3 attachWorld = playerBody.transform.TransformPoint(localAttachOffset);
            len += Vector3.Distance(prev, attachWorld);
        }

        return len;
    }

    public Vector3 GetPullDirectionXZ()
    {
        if (allBodies == null || activeCount < 1 || playerBody == null)
            return Vector3.zero;

        Vector3 attachWorld = playerBody.transform.TransformPoint(localAttachOffset);
        Vector3 lastSeg = allBodies[activeCount - 1].position;

        Vector3 dir = lastSeg - attachWorld;
        dir.y = 0f;

        return dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;
    }

    /// <summary>
    /// Заполняет buffer позициями: sourceAnchor -> все активные сегменты -> точка крепления игрока.
    /// Возвращает реальное число заполненных точек.
    /// Buffer должен быть размером не меньше MaxPossiblePoints.
    /// </summary>
    public int GetActivePositions(Vector3[] buffer)
    {
        if (allBodies == null || activeCount < 1)
            return 0;

        int count = 0;

        if (sourceAnchor != null)
            buffer[count++] = sourceAnchor.position;

        for (int i = 0; i < activeCount; i++)
            buffer[count++] = allBodies[i].position;

        if (playerBody != null)
            buffer[count++] = playerBody.transform.TransformPoint(localAttachOffset);

        return count;
    }
}