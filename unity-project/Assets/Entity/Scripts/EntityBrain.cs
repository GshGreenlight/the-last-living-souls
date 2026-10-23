using UnityEngine;

public enum EntityState
{
    Patrol,
    ChaseWire,
    Distracted
}

/// <summary>
/// Task 1 scaffold: Inspector wiring + cable sample buffer.
/// Patrol / chase / game over land in later tasks.
/// </summary>
public class EntityBrain : MonoBehaviour
{
    [Header("References")]
    public PhysicalCable cable;
    public Transform player;
    public Transform[] patrolWaypoints;

    [Header("Patrol (Task 2)")]
    public float patrolSpeed = 2f;
    public bool pingPong;

    [Header("Wire / Chase (Tasks 3–4)")]
    public float chaseSpeed = 6f;
    public float wireDetectRadius = 0.6f;
    public float maxWireYDelta = 2f;
    public int minCablePointsToChase = 3;

    [Header("Touch (Task 5)")]
    public float touchPlayerRadius = 0.8f;

    [Header("Debug")]
    public bool drawGizmos = true;

    public EntityState State { get; private set; } = EntityState.Patrol;

    private Vector3[] _pathBuffer;
    private int _pathCount;

    private void Awake()
    {
        EnsureBuffer();
    }

    private void EnsureBuffer()
    {
        int size = cable != null ? Mathf.Max(8, cable.MaxPossiblePoints) : 64;
        if (_pathBuffer == null || _pathBuffer.Length < size)
            _pathBuffer = new Vector3[size];
    }

    private void Update()
    {
        EnsureBuffer();
        SampleCable();
        // Task 2+: patrol / detect / chase
    }

    private void SampleCable()
    {
        _pathCount = CablePolylineQuery.Sample(cable, _pathBuffer);
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, wireDetectRadius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, touchPlayerRadius);

        if (patrolWaypoints == null)
            return;

        Gizmos.color = Color.cyan;
        for (int i = 0; i < patrolWaypoints.Length; i++)
        {
            if (patrolWaypoints[i] == null)
                continue;
            Gizmos.DrawSphere(patrolWaypoints[i].position, 0.15f);
            int j = pingPong ? i : (i + 1) % patrolWaypoints.Length;
            if (j < patrolWaypoints.Length && patrolWaypoints[j] != null)
                Gizmos.DrawLine(patrolWaypoints[i].position, patrolWaypoints[j].position);
        }
    }
}
