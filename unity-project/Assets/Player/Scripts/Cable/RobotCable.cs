using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LastLivingSouls.Cable
{
    /// <summary>
    /// Floor cable from a fixed machine anchor. Walking lays your route.
    /// Hold R to shrink like a rubber band around obstacle contours.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class RobotCable : MonoBehaviour, ICableReadout, ICableLeash
    {
        [Header("Input")]
        [SerializeField] InputActionReference tightenAction;

        [Header("Anchor")]
        [SerializeField] Transform anchor;
        [SerializeField] float floorY = 0.08f;

        [Header("Length")]
        [SerializeField] float maxLength = 40f;
        [SerializeField] float pointSpacing = 0.35f;

        [Header("Tighten")]
        [SerializeField] float tautStep = 0.75f;
        [SerializeField] int iterationsPerFrame = 24;

        [Header("Collision")]
        [SerializeField] SphereCollider cableProbe;
        [SerializeField, Min(0f)] float collisionSkin = 0.002f;
        [SerializeField] LayerMask cableObstacleMask = 1 << 8;

        [Header("Visuals")]
        [SerializeField] LineRenderer cableLine;

        readonly List<Vector3> _points = new List<Vector3>(256);
        readonly CableObstacleField _obstacles = new CableObstacleField();
        readonly CableRubberBand _rubberBand = new CableRubberBand();

        bool _holdingTaut;
        Vector3 _lastAnchorFloor;

        public float MaxLength => maxLength;
        public float UsedLength { get; private set; }
        public float RemainingLength => Mathf.Max(0f, maxLength - UsedLength);
        public float UsedNormalized => maxLength > 0f ? Mathf.Clamp01(UsedLength / maxLength) : 0f;
        public bool IsTightening => _holdingTaut;
        public string StatusHint => _holdingTaut ? "holding R..." : "hold R";

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

        void Start()
        {
            if (anchor == null)
            {
                Debug.LogError($"{nameof(RobotCable)}: assign CableAnchor in the Inspector.", this);
                enabled = false;
                return;
            }

            if (cableProbe == null)
            {
                Debug.LogWarning(
                    $"{nameof(RobotCable)}: assign a dedicated SphereCollider " +
                    "to Cable Probe. Cable obstacle collision is disabled until it is assigned.",
                    this);
            }
            else if (!cableProbe.isTrigger)
            {
                Debug.LogWarning(
                    $"{nameof(RobotCable)}: Cable Probe should be a trigger so it does not " +
                    "participate in normal Rigidbody collisions.",
                    cableProbe);
            }

            if (cableObstacleMask.value == 0)
            {
                Debug.LogWarning(
                    $"{nameof(RobotCable)}: Cable Obstacle Mask is empty. " +
                    "Cable obstacle collision is disabled.",
                    this);
            }

            _lastAnchorFloor = F(anchor.position);
            _points.Clear();
            _points.Add(_lastAnchorFloor);
            RefreshVisual(F(transform.position));
        }

        void Update()
        {
            SyncPathToMovedAnchor();

            Vector3 tip = F(transform.position);

            bool holding =
                tightenAction != null &&
                tightenAction.action != null &&
                tightenAction.action.IsPressed();

            if (holding)
            {
                if (!_holdingTaut)
                    BeginHoldTighten(tip);
                StepHoldTighten(tip);
            }
            else
            {
                if (_holdingTaut)
                    EndHoldTighten(tip);
                TryLayPoint(tip);
            }

            RefreshVisual(F(transform.position));
        }

        public Vector3 ClampWishVelocity(Vector3 wishVelocity, float deltaTime)
        {
            wishVelocity.y = 0f;
            if (wishVelocity.sqrMagnitude < 0.0001f || _points.Count == 0)
                return wishVelocity;

            Vector3 tip = F(transform.position);
            if (CableMath.MeasureLength(_points, tip) < maxLength - 0.02f)
                return wishVelocity;

            Vector3 nextTip = F(tip + wishVelocity * Mathf.Max(deltaTime, 0.0001f));
            if (CableMath.MeasureLength(_points, nextTip) <= maxLength + 0.001f)
                return wishVelocity;

            Vector3 last = _points[_points.Count - 1];
            Vector3 toLast = last - tip;
            toLast.y = 0f;
            if (toLast.sqrMagnitude < 0.0001f)
                return Vector3.zero;

            toLast.Normalize();
            float toward = Vector3.Dot(wishVelocity, toLast);
            return toward <= 0f ? Vector3.zero : toLast * toward;
        }

        void SyncPathToMovedAnchor()
        {
            if (anchor == null)
                return;

            Vector3 now = F(anchor.position);
            Vector3 delta = now - _lastAnchorFloor;
            delta.y = 0f;

            if (delta.sqrMagnitude > 0.0001f)
            {
                for (int i = 0; i < _points.Count; i++)
                    _points[i] = F(_points[i] + delta);

                _rubberBand.Translate(delta, floorY);
                _lastAnchorFloor = now;
            }

            if (_points.Count == 0)
                _points.Add(now);
            else
                _points[0] = now;

            _rubberBand.PinStart(now, floorY);
        }

        void BeginHoldTighten(Vector3 tip)
        {
            _obstacles.Configure(
                cableProbe,
                CableRadius,
                collisionSkin,
                cableObstacleMask);

            _rubberBand.Begin(
                _points,
                F(anchor.position),
                tip,
                floorY);

            _holdingTaut = true;
        }

        void StepHoldTighten(Vector3 tip)
        {
            _rubberBand.Step(
                F(anchor.position),
                tip,
                floorY,
                tautStep,
                iterationsPerFrame,
                _obstacles);
            CommitWorkToPoints(tip);
        }

        void EndHoldTighten(Vector3 tip)
        {
            _rubberBand.Finish(floorY, _obstacles);
            CommitWorkToPoints(tip);
            _holdingTaut = false;
            _rubberBand.Clear();
        }

        void CommitWorkToPoints(Vector3 tip)
        {
            tip = F(tip);
            List<Vector3> work = _rubberBand.Work;
            _points.Clear();

            for (int i = 0; i < work.Count; i++)
            {
                Vector3 p = F(work[i]);
                if (i == work.Count - 1 && CableMath.HorizontalDistance(p, tip) <= 0.08f)
                    break;
                _points.Add(p);
            }

            if (_points.Count == 0)
                _points.Add(F(anchor.position));
            else
                _points[0] = F(anchor.position);
        }

        void TryLayPoint(Vector3 tip)
        {
            if (_points.Count == 0)
                _points.Add(F(anchor.position));

            _points[0] = F(anchor.position);

            Vector3 last = _points[_points.Count - 1];
            float dist = CableMath.HorizontalDistance(last, tip);
            if (dist < pointSpacing)
                return;
            if (CableMath.MeasureLength(_points, tip) > maxLength)
                return;

            Vector3 dir = tip - last;
            dir.y = 0f;
            float total = dir.magnitude;
            dir /= total;

            float travelled = pointSpacing;
            while (travelled < total)
            {
                Vector3 p = F(last + dir * travelled);
                if (CableMath.MeasureLength(_points, p) > maxLength)
                    break;
                _points.Add(p);
                travelled += pointSpacing;
            }
        }

        void RefreshVisual(Vector3 tip)
        {
            UsedLength = CableMath.MeasureLength(_points, tip);

            if (cableLine == null)
                return;
                
            cableLine.positionCount = _points.Count + 1;

            for (int i = 0; i < _points.Count; i++)
                cableLine.SetPosition(i, _points[i]);

            cableLine.SetPosition(_points.Count, tip);
        }

        Vector3 F(Vector3 world) => CableMath.Floor(world, floorY);

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            for (int i = 0; i < _points.Count; i++)
                Gizmos.DrawSphere(_points[i], 0.06f);

        }
#endif
    }
}
