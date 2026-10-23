using UnityEngine;

public enum EntityState
{
    Patrol,
    ChaseWire,
    Distracted
}

/// <summary>
/// Blind entity: patrol → step on cable → chase along cable → touch = game over.
/// Wire logic filled in subsequent tasks; Task 1 = scaffold + Inspector wiring.
/// </summary>
public class EntityBrain : MonoBehaviour
{
    [Header("References")]
    public PhysicalCable cable;
    public Transform player;
    public Transform[] patrolWaypoints;

    [Header("Patrol")]
    public float patrolSpeed = 2f;
    public bool pingPong;

    [Header("Wire / Chase")]
    public float chaseSpeed = 6f;
    public float wireDetectRadius = 0.6f;
    public float maxWireYDelta = 2f;
    public int minCablePointsToChase = 3;

    [Header("Touch / Game Over")]
    public float touchPlayerRadius = 0.8f;
    public GameOverController gameOver;

    [Header("Distract")]
    public float distractMoveSpeed = 5f;
    public float distractArriveDistance = 0.4f;

    [Header("Debug")]
    public bool drawGizmos = true;

    public EntityState State { get; private set; } = EntityState.Patrol;

    private Vector3[] _pathBuffer;
    private int _pathCount;
    private int _patrolIndex;
    private int _patrolDir = 1;
    private float _chaseDistanceAlong;
    private bool _failed;

    private Transform _distractor;
    private float _inspectSeconds = 2.5f;
    private float _inspectTimer;
    private bool _inspecting;

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
        if (_failed)
            return;

        EnsureBuffer();
        SampleCable();

        switch (State)
        {
            case EntityState.Patrol:
                TickPatrol();
                TryDetectWire();
                break;
            case EntityState.ChaseWire:
                TickChase();
                break;
            case EntityState.Distracted:
                TickDistracted();
                break;
        }

        TryTouchPlayer();
    }

    private void SampleCable()
    {
        _pathCount = CablePolylineQuery.Sample(cable, _pathBuffer);
    }

    private void TickPatrol()
    {
        if (patrolWaypoints == null || patrolWaypoints.Length == 0)
            return;

        Transform target = patrolWaypoints[Mathf.Clamp(_patrolIndex, 0, patrolWaypoints.Length - 1)];
        if (target == null)
            return;

        MoveToward(target.position, patrolSpeed);

        Vector3 flat = transform.position;
        Vector3 goal = target.position;
        flat.y = 0f;
        goal.y = 0f;
        if (Vector3.Distance(flat, goal) > 0.15f)
            return;

        if (pingPong)
        {
            if (_patrolIndex >= patrolWaypoints.Length - 1)
                _patrolDir = -1;
            else if (_patrolIndex <= 0)
                _patrolDir = 1;
            _patrolIndex += _patrolDir;
        }
        else
        {
            _patrolIndex = (_patrolIndex + 1) % patrolWaypoints.Length;
        }
    }

    private void TryDetectWire()
    {
        if (_pathCount < minCablePointsToChase)
            return;

        var nearest = CablePolylineQuery.FindNearestXZ(
            transform.position, _pathBuffer, _pathCount, maxWireYDelta);

        if (!nearest.Valid || nearest.Distance > wireDetectRadius)
            return;

        _chaseDistanceAlong = CablePolylineQuery.DistanceAlongPathToNearest(
            _pathBuffer, _pathCount, nearest.SegmentIndex, nearest.SegmentT);
        State = EntityState.ChaseWire;
    }

    private void TickChase()
    {
        if (_pathCount < minCablePointsToChase)
        {
            State = EntityState.Patrol;
            return;
        }

        var nearest = CablePolylineQuery.FindNearestXZ(
            transform.position, _pathBuffer, _pathCount, maxWireYDelta);

        // Retracted / lost the cable
        if (!nearest.Valid || nearest.Distance > wireDetectRadius * 3f)
        {
            State = EntityState.Patrol;
            return;
        }

        _chaseDistanceAlong += chaseSpeed * Time.deltaTime;

        if (!CablePolylineQuery.GetPointAlongPath(
                _pathBuffer, _pathCount, _chaseDistanceAlong,
                out Vector3 target, out _, out bool atEnd))
        {
            State = EntityState.Patrol;
            return;
        }

        MoveToward(target, chaseSpeed);

        if (atEnd && player != null)
            MoveToward(player.position, chaseSpeed);
    }

    private void TryTouchPlayer()
    {
        if (player == null)
            return;

        Vector3 a = transform.position;
        Vector3 b = player.position;
        a.y = 0f;
        b.y = 0f;
        if (Vector3.Distance(a, b) > touchPlayerRadius)
            return;

        TriggerGameOver();
    }

    private void TriggerGameOver()
    {
        if (_failed)
            return;

        _failed = true;
        if (gameOver != null)
            gameOver.Trigger();
        else
            Debug.Log("GAME OVER — entity touched player (assign GameOverController)");
    }

    /// <summary>
    /// Walk to distractor, inspect, then return to Patrol (nearest waypoint).
    /// Works from Patrol or ChaseWire. Ignored if already distracted or game over.
    /// </summary>
    public void DistractToward(Transform distractor, float inspectSeconds = 2.5f)
    {
        if (_failed || distractor == null)
            return;

        // Already busy with a distractor — ignore second pull (wiki-lite).
        if (State == EntityState.Distracted)
            return;

        _distractor = distractor;
        _inspectSeconds = Mathf.Max(0.1f, inspectSeconds);
        _inspectTimer = 0f;
        _inspecting = false;
        State = EntityState.Distracted;
    }

    private void TickDistracted()
    {
        if (_distractor == null)
        {
            EnterPatrolNearestWaypoint();
            return;
        }

        if (!_inspecting)
        {
            MoveToward(_distractor.position, distractMoveSpeed);

            Vector3 flat = transform.position;
            Vector3 goal = _distractor.position;
            flat.y = 0f;
            goal.y = 0f;
            if (Vector3.Distance(flat, goal) > distractArriveDistance)
                return;

            _inspecting = true;
            _inspectTimer = 0f;
            return;
        }

        _inspectTimer += Time.deltaTime;
        if (_inspectTimer < _inspectSeconds)
            return;

        _distractor = null;
        _inspecting = false;
        EnterPatrolNearestWaypoint();
    }

    private void EnterPatrolNearestWaypoint()
    {
        State = EntityState.Patrol;
        PickNearestPatrolIndex();
    }

    private void PickNearestPatrolIndex()
    {
        if (patrolWaypoints == null || patrolWaypoints.Length == 0)
            return;

        float best = float.PositiveInfinity;
        int bestIndex = 0;
        Vector3 pos = transform.position;
        pos.y = 0f;

        for (int i = 0; i < patrolWaypoints.Length; i++)
        {
            if (patrolWaypoints[i] == null)
                continue;

            Vector3 wp = patrolWaypoints[i].position;
            wp.y = 0f;
            float d = Vector3.Distance(pos, wp);
            if (d < best)
            {
                best = d;
                bestIndex = i;
            }
        }

        _patrolIndex = bestIndex;
        _patrolDir = 1;
    }

    private void MoveToward(Vector3 worldTarget, float speed)
    {
        Vector3 pos = transform.position;
        Vector3 next = Vector3.MoveTowards(pos, worldTarget, speed * Time.deltaTime);
        // Keep current Y unless target is a waypoint with meaningful height
        next.y = pos.y;
        Vector3 flatDir = worldTarget - pos;
        flatDir.y = 0f;
        if (flatDir.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                Quaternion.LookRotation(flatDir.normalized, Vector3.up),
                10f * Time.deltaTime);

        transform.position = new Vector3(next.x, pos.y, next.z);
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
