using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace LastLivingSouls.Cable
{
    /// <summary>
    /// Three-dimensional cable attached to a fixed anchor and the player.
    /// Particles use gravity, collide as spheres/capsules and reel into the
    /// player-side tip on R.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RobotCable : MonoBehaviour, ICableReadout, ICableLeash
    {
        [Header("Input")]
        [SerializeField] InputActionReference tightenAction;

        [Header("Endpoints")]
        [SerializeField] Transform anchor;
        [Tooltip("Optional cable socket on the player. Uses this transform when empty.")]
        [SerializeField] Transform cableTip;

        [Header("Length")]
        [SerializeField, Min(0f)] float maxLength = 40f;
        [Tooltip("Keep this no larger than the cable diameter when segment collision is disabled.")]
        [SerializeField, Min(0.02f)] float solverPointSpacing = 0.12f;
        [SerializeField, Min(0f)] float reelSpeed = 4f;
        [Tooltip("Allowed solver length error before reeling is treated as blocked by an obstacle.")]
        [SerializeField, Min(0f)] float reelBlockedTolerance = 0.01f;
        [Tooltip("How long the reel-blocked UI status remains visible after the last blocked physics step.")]
        [SerializeField, Min(0f)] float reelBlockedStatusHoldTime = 0.15f;

        [Header("3D Simulation")]
        [FormerlySerializedAs("iterationsPerFrame")]
        [SerializeField, Range(1, 32)] int constraintIterations = 12;
        [Tooltip("Extra passes that keep the volume between particles outside obstacles.")]
        [SerializeField, Range(0, 4)] int segmentCollisionIterations = 2;
        [SerializeField, Range(0f, 1f)] float damping = 0.02f;
        [SerializeField, Min(0f)] float gravityScale = 1f;
        [SerializeField, Range(0f, 1f)] float surfaceFriction = 0.2f;

        [Header("Collision")]
        [SerializeField] SphereCollider cableProbe;
        [SerializeField] CapsuleCollider cableSegmentProbe;
        [SerializeField, Min(0f)] float collisionSkin = 0.002f;
        [Tooltip("Must include the ground, ramps and every obstacle that should support the cable.")]
        [SerializeField] LayerMask cableObstacleMask = (1 << 0) | (1 << 8);

        [Header("Visuals")]
        [SerializeField] LineRenderer cableLine;

        [Header("Debug")]
        [SerializeField] bool drawCableCollisionSpheres = true;
        [SerializeField] Color cableCollisionGizmoColor =
            new Color(1f, 0.6f, 0.1f, 0.9f);

        readonly CableObstacleField _obstacles = new CableObstacleField();
        readonly CableRubberBand _solver = new CableRubberBand();

        bool _holdingTaut;
        bool _showReelBlocked;
        float _reelBlockedStatusTimer;

        public float MaxLength => maxLength;
        public float UsedLength { get; private set; }
        public float UsedNormalized => maxLength > 0f
            ? Mathf.Clamp01(UsedLength / maxLength)
            : 0f;
        public string StatusHint => !_holdingTaut
            ? "hold R"
            : _showReelBlocked
                ? "reel blocked"
                : "reeling R...";

        float CableWidth
        {
            get
            {
                if (cableLine == null)
                    return 0.14f;

                AnimationCurve curve = cableLine.widthCurve;
                float curveValue =
                    curve != null && curve.length > 0
                        ? curve.Evaluate(0.5f)
                        : 1f;

                return Mathf.Max(
                    0.002f,
                    cableLine.widthMultiplier * curveValue);
            }
        }

        float CableRadius => CableWidth * 0.5f;
        Vector3 TipPosition => cableTip != null
            ? cableTip.position
            : transform.position;

        void Start()
        {
            if (anchor == null)
            {
                Debug.LogError(
                    $"{nameof(RobotCable)}: assign Cable Anchor in the Inspector.",
                    this);
                enabled = false;
                return;
            }

            if (cableProbe == null)
            {
                Debug.LogWarning(
                    $"{nameof(RobotCable)}: assign a dedicated SphereCollider " +
                    "to Cable Probe. Cable collision is disabled until it is assigned.",
                    this);
            }
            else if (!cableProbe.isTrigger)
            {
                Debug.LogWarning(
                    $"{nameof(RobotCable)}: Cable Probe should be a trigger so it does not " +
                    "participate in normal Rigidbody collisions.",
                    cableProbe);
            }

            if (cableSegmentProbe == null)
            {
                Debug.LogWarning(
                    $"{nameof(RobotCable)}: assign a dedicated CapsuleCollider " +
                    "to Cable Segment Probe. Collision between cable points is disabled.",
                    this);
            }
            else if (!cableSegmentProbe.isTrigger)
            {
                Debug.LogWarning(
                    $"{nameof(RobotCable)}: Cable Segment Probe should be a trigger.",
                    cableSegmentProbe);
            }

            if (cableObstacleMask.value == 0)
            {
                Debug.LogWarning(
                    $"{nameof(RobotCable)}: Cable Obstacle Mask is empty. " +
                    "Ground, ramp and obstacle collision is disabled.",
                    this);
            }

            ConfigureObstacles();
            _solver.Initialize(
                anchor.position,
                TipPosition,
                solverPointSpacing);
            UsedLength = _solver.DeployedLength;
            RefreshVisual();
        }

        void Update()
        {
            _holdingTaut =
                tightenAction != null &&
                tightenAction.action != null &&
                tightenAction.action.IsPressed();

            if (!_holdingTaut)
            {
                _showReelBlocked = false;
                _reelBlockedStatusTimer = 0f;
            }
        }

        void FixedUpdate()
        {
            if (anchor == null)
                return;

            ConfigureObstacles();
            _solver.Simulate(
                anchor.position,
                TipPosition,
                Time.fixedDeltaTime,
                solverPointSpacing,
                constraintIterations,
                segmentCollisionIterations,
                reelBlockedTolerance,
                damping,
                gravityScale,
                surfaceFriction,
                _holdingTaut,
                reelSpeed,
                maxLength,
                _obstacles);

            UpdateReelBlockedStatus(Time.fixedDeltaTime);
            UsedLength = _solver.DeployedLength;
        }

        void UpdateReelBlockedStatus(float deltaTime)
        {
            if (_solver.IsReelBlocked)
            {
                _showReelBlocked = true;
                _reelBlockedStatusTimer = reelBlockedStatusHoldTime;
                return;
            }

            _reelBlockedStatusTimer = Mathf.Max(
                0f,
                _reelBlockedStatusTimer - Mathf.Max(0f, deltaTime));
            _showReelBlocked = _reelBlockedStatusTimer > 0f;
        }

        void LateUpdate()
        {
            RefreshVisual();
        }

        public Vector3 ClampWishVelocity(
            Vector3 wishVelocity,
            float deltaTime)
        {
            IReadOnlyList<Vector3> points = _solver.Work;
            if (wishVelocity.sqrMagnitude < 0.0001f || points.Count < 2)
                return wishVelocity;

            Vector3 tip = TipPosition;
            float allowedLength = _holdingTaut
                ? _solver.DeployedLength
                : maxLength;

            if (CableMath.MeasureLengthToTip(points, tip) <
                allowedLength - 0.02f)
            {
                return wishVelocity;
            }

            Vector3 nextTip =
                tip + wishVelocity * Mathf.Max(deltaTime, 0.0001f);
            if (CableMath.MeasureLengthToTip(points, nextTip) <=
                allowedLength + 0.001f)
            {
                return wishVelocity;
            }

            Vector3 last = points[points.Count - 2];
            Vector3 toLast = last - tip;
            if (toLast.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            toLast.Normalize();
            float toward = Vector3.Dot(wishVelocity, toLast);
            return toward <= 0f
                ? Vector3.zero
                : toLast * toward;
        }

        void ConfigureObstacles()
        {
            _obstacles.Configure(
                cableProbe,
                cableSegmentProbe,
                CableRadius,
                collisionSkin,
                cableObstacleMask,
                transform);
        }

        void RefreshVisual()
        {
            if (cableLine == null)
                return;

            IReadOnlyList<Vector3> points = _solver.Work;
            cableLine.positionCount = points.Count;

            for (int i = 0; i < points.Count; i++)
                cableLine.SetPosition(i, points[i]);
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!drawCableCollisionSpheres)
                return;

            Gizmos.color = cableCollisionGizmoColor;
            float probeRadius = CableRadius + Mathf.Max(0f, collisionSkin);
            IReadOnlyList<Vector3> points = _solver.Work;

            for (int i = 0; i < points.Count; i++)
                DrawCollisionSphere(points[i], probeRadius);
        }

        void DrawCollisionSphere(Vector3 center, float radius)
        {
            Color wireColor = cableCollisionGizmoColor;
            Color fillColor = wireColor;
            fillColor.a *= 0.2f;

            Gizmos.color = fillColor;
            Gizmos.DrawSphere(center, radius);
            Gizmos.color = wireColor;
            Gizmos.DrawWireSphere(center, radius);
        }
#endif
    }
}
