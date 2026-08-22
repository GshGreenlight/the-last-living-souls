using System.Collections.Generic;
using UnityEngine;

namespace LastLivingSouls.Cable
{
    /// <summary>
    /// Three-dimensional Verlet/PBD cable solver. The historical class name is
    /// kept so existing project references and the script meta file stay stable.
    /// </summary>
    public sealed class CableRubberBand
    {
        const float MinimumSpacing = 0.02f;
        const float DistanceEpsilon = 0.0001f;

        readonly List<Vector3> _positions = new List<Vector3>(512);
        readonly List<Vector3> _previousPositions = new List<Vector3>(512);
        readonly List<Vector3> _seedPath = new List<Vector3>(2);

        public IReadOnlyList<Vector3> Work => _positions;
        public float DeployedLength { get; private set; }
        public bool IsReelBlocked { get; private set; }
        public bool IsInitialized => _positions.Count >= 2;

        public void Initialize(
            Vector3 start,
            Vector3 tip,
            float particleSpacing)
        {
            particleSpacing = Mathf.Max(MinimumSpacing, particleSpacing);

            _seedPath.Clear();
            _seedPath.Add(start);
            _seedPath.Add(tip);
            CableMath.Densify(_seedPath, _positions, particleSpacing);

            if (_positions.Count < 2)
            {
                _positions.Clear();
                _positions.Add(start);
                _positions.Add(tip);
            }

            _previousPositions.Clear();
            _previousPositions.AddRange(_positions);
            DeployedLength = Vector3.Distance(start, tip);
            PinEndpoints(start, tip);
        }

        public void Simulate(
            Vector3 start,
            Vector3 tip,
            float deltaTime,
            float particleSpacing,
            int constraintIterations,
            int segmentCollisionIterations,
            float reelBlockedTolerance,
            float damping,
            float gravityScale,
            float surfaceFriction,
            bool reelIn,
            float reelSpeed,
            float maxLength,
            CableObstacleField obstacles)
        {
            particleSpacing = Mathf.Max(MinimumSpacing, particleSpacing);
            deltaTime = Mathf.Max(0f, deltaTime);
            constraintIterations = Mathf.Max(1, constraintIterations);
            segmentCollisionIterations = Mathf.Max(
                0,
                segmentCollisionIterations);
            reelBlockedTolerance = Mathf.Max(0f, reelBlockedTolerance);
            damping = Mathf.Clamp01(damping);
            gravityScale = Mathf.Max(0f, gravityScale);
            surfaceFriction = Mathf.Clamp01(surfaceFriction);
            reelSpeed = Mathf.Max(0f, reelSpeed);
            maxLength = Mathf.Max(0f, maxLength);

            if (!IsInitialized)
                Initialize(start, tip, particleSpacing);

            TrackMovingTip(tip, particleSpacing, reelIn);
            PinEndpoints(start, tip);

            float previousDeployedLength = DeployedLength;
            float minimumLength = Vector3.Distance(start, tip);
            if (reelIn)
            {
                DeployedLength = Mathf.Max(
                    minimumLength,
                    DeployedLength - reelSpeed * deltaTime);
            }
            else
            {
                float requiredLength = CableMath.MeasureLength(_positions);
                DeployedLength = Mathf.Max(
                    minimumLength,
                    Mathf.Min(
                        maxLength,
                        Mathf.Max(DeployedLength, requiredLength)));
            }

            bool attemptedShortening =
                reelIn &&
                DeployedLength < previousDeployedLength - DistanceEpsilon;
            bool removedParticle = EnsureParticleCount(
                particleSpacing,
                reelIn,
                out Vector3 removedPosition,
                out Vector3 removedPreviousPosition,
                out int removedIndex);
            PinEndpoints(start, tip);

            Integrate(deltaTime, damping, gravityScale);
            ResolveCollisions(obstacles, surfaceFriction, true);

            float segmentLimit = _positions.Count > 1
                ? DeployedLength / (_positions.Count - 1)
                : 0f;

            for (int iteration = 0;
                 iteration < constraintIterations;
                 iteration++)
            {
                SolveDistanceConstraints(segmentLimit, reelIn);
                ResolveCollisions(obstacles, surfaceFriction, false);
                PinEndpoints(start, tip);
            }

            // Point spheres cannot see an obstacle that lies only between two
            // particles. These limited extra passes resolve the full segment
            // volume without multiplying every distance-constraint iteration.
            for (int iteration = 0;
                 iteration < segmentCollisionIterations;
                 iteration++)
            {
                SolveDistanceConstraints(segmentLimit, reelIn);
                ResolveCollisions(obstacles, surfaceFriction, false);
                PinEndpoints(start, tip);
                ResolveSegmentCollisions(obstacles);
                PinEndpoints(start, tip);
            }

            float safePathLength = CableMath.MeasureLength(_positions);
            IsReelBlocked =
                attemptedShortening &&
                safePathLength > DeployedLength + reelBlockedTolerance;

            if (!IsReelBlocked)
                return;

            // The collision-safe route cannot fit into the requested reel
            // length. Keep the last accepted spool length, but retain the
            // solved positions so the cable can continue sliding along the
            // obstacle and succeed on a later physics step.
            DeployedLength = previousDeployedLength;

            if (removedParticle)
            {
                _positions.Insert(removedIndex, removedPosition);
                _previousPositions.Insert(
                    removedIndex,
                    removedPreviousPosition);
                PinEndpoints(start, tip);
            }
        }

        void TrackMovingTip(
            Vector3 tip,
            float particleSpacing,
            bool reelIn)
        {
            if (_positions.Count < 2)
                return;

            int last = _positions.Count - 1;

            // The reel is carried by the player. While it is active, the
            // moving tip consumes cable instead of laying new particles behind
            // itself. Distance constraints and capsule collision move the last
            // interior particle toward the player without cutting obstacles.
            if (reelIn)
            {
                _positions[last] = tip;
                _previousPositions[last] = tip;
                return;
            }

            Vector3 lastInterior = _positions[last - 1];
            float distance = Vector3.Distance(lastInterior, tip);

            while (distance > particleSpacing)
            {
                Vector3 sample = Vector3.MoveTowards(
                    lastInterior,
                    tip,
                    particleSpacing);

                // Convert the old moving endpoint into an interior particle,
                // then append a new endpoint at the current player position.
                _positions[last] = sample;
                _previousPositions[last] = sample;
                _positions.Add(tip);
                _previousPositions.Add(tip);

                last = _positions.Count - 1;
                lastInterior = sample;
                distance = Vector3.Distance(lastInterior, tip);
            }

            _positions[last] = tip;
            _previousPositions[last] = tip;
        }

        bool EnsureParticleCount(
            float particleSpacing,
            bool reelIn,
            out Vector3 removedPosition,
            out Vector3 removedPreviousPosition,
            out int removedIndex)
        {
            removedPosition = default;
            removedPreviousPosition = default;
            removedIndex = -1;

            int desiredSegments = Mathf.Max(
                1,
                Mathf.CeilToInt(DeployedLength / particleSpacing));

            while (_positions.Count - 1 < desiredSegments)
                SplitLongestSegment();

            // Removing at most one particle per physics step makes the cable
            // visibly enter the reel carried by the player instead of snapping.
            if (reelIn &&
                _positions.Count > 2 &&
                _positions.Count - 1 > desiredSegments)
            {
                removedIndex = _positions.Count - 2;
                removedPosition = _positions[removedIndex];
                removedPreviousPosition =
                    _previousPositions[removedIndex];
                _positions.RemoveAt(removedIndex);
                _previousPositions.RemoveAt(removedIndex);
                return true;
            }

            return false;
        }

        void SplitLongestSegment()
        {
            if (_positions.Count < 2)
                return;

            int longestIndex = 0;
            float longestDistanceSq = -1f;

            for (int i = 0; i < _positions.Count - 1; i++)
            {
                float distanceSq =
                    (_positions[i + 1] - _positions[i]).sqrMagnitude;
                if (distanceSq > longestDistanceSq)
                {
                    longestDistanceSq = distanceSq;
                    longestIndex = i;
                }
            }

            Vector3 position = Vector3.Lerp(
                _positions[longestIndex],
                _positions[longestIndex + 1],
                0.5f);
            Vector3 previous = Vector3.Lerp(
                _previousPositions[longestIndex],
                _previousPositions[longestIndex + 1],
                0.5f);

            _positions.Insert(longestIndex + 1, position);
            _previousPositions.Insert(longestIndex + 1, previous);
        }

        void Integrate(float deltaTime, float damping, float gravityScale)
        {
            float velocityScale = 1f - damping;
            Vector3 acceleration =
                Physics.gravity * gravityScale * deltaTime * deltaTime;

            for (int i = 1; i < _positions.Count - 1; i++)
            {
                Vector3 current = _positions[i];
                Vector3 velocity =
                    (current - _previousPositions[i]) * velocityScale;

                _previousPositions[i] = current;
                _positions[i] = current + velocity + acceleration;
            }
        }

        void SolveDistanceConstraints(
            float segmentLimit,
            bool solveFromTip)
        {
            if (solveFromTip)
            {
                for (int i = _positions.Count - 2; i >= 0; i--)
                    SolveDistanceConstraint(i, segmentLimit);

                return;
            }

            for (int i = 0; i < _positions.Count - 1; i++)
                SolveDistanceConstraint(i, segmentLimit);
        }

        void SolveDistanceConstraint(int i, float segmentLimit)
        {
            Vector3 a = _positions[i];
            Vector3 b = _positions[i + 1];
            Vector3 delta = b - a;
            float distance = delta.magnitude;

            // This is a rope constraint: segments may compress and sag but
            // may not stretch beyond their share of the deployed length.
            if (distance <= segmentLimit || distance <= DistanceEpsilon)
                return;

            Vector3 correction =
                delta / distance * (distance - segmentLimit);
            bool aPinned = i == 0;
            bool bPinned = i + 1 == _positions.Count - 1;

            if (aPinned && bPinned)
                return;
            if (aPinned)
            {
                _positions[i + 1] -= correction;
            }
            else if (bPinned)
            {
                _positions[i] += correction;
            }
            else
            {
                _positions[i] += correction * 0.5f;
                _positions[i + 1] -= correction * 0.5f;
            }
        }

        void ResolveCollisions(
            CableObstacleField obstacles,
            float surfaceFriction,
            bool updateVelocity)
        {
            if (obstacles == null)
                return;

            for (int i = 1; i < _positions.Count - 1; i++)
            {
                Vector3 predicted = _positions[i];
                Vector3 resolved = obstacles.PushOut(predicted);
                Vector3 correction = resolved - predicted;
                _positions[i] = resolved;

                if (!updateVelocity ||
                    correction.sqrMagnitude <=
                        DistanceEpsilon * DistanceEpsilon)
                {
                    continue;
                }

                Vector3 normal = correction.normalized;
                Vector3 velocity = predicted - _previousPositions[i];
                float normalSpeed = Vector3.Dot(velocity, normal);

                if (normalSpeed < 0f)
                    velocity -= normal * normalSpeed;

                Vector3 outwardVelocity =
                    normal * Mathf.Max(0f, Vector3.Dot(velocity, normal));
                Vector3 tangentVelocity =
                    Vector3.ProjectOnPlane(velocity, normal) *
                    (1f - surfaceFriction);

                _previousPositions[i] =
                    resolved - outwardVelocity - tangentVelocity;
            }
        }

        void ResolveSegmentCollisions(CableObstacleField obstacles)
        {
            if (obstacles == null)
                return;

            int last = _positions.Count - 1;
            for (int i = 0; i < last; i++)
            {
                Vector3 oldA = _positions[i];
                Vector3 oldB = _positions[i + 1];
                Vector3 a = oldA;
                Vector3 b = oldB;
                bool aPinned = i == 0;
                bool bPinned = i + 1 == last;

                obstacles.PushSegmentOut(
                    ref a,
                    ref b,
                    aPinned,
                    bPinned);

                // Shift the Verlet history by the same correction. Otherwise
                // collision recovery would create artificial cable velocity.
                if (!aPinned)
                {
                    _positions[i] = a;
                    _previousPositions[i] += a - oldA;
                }

                if (!bPinned)
                {
                    _positions[i + 1] = b;
                    _previousPositions[i + 1] += b - oldB;
                }
            }
        }

        void PinEndpoints(Vector3 start, Vector3 tip)
        {
            if (_positions.Count < 2)
                return;

            int last = _positions.Count - 1;
            _positions[0] = start;
            _previousPositions[0] = start;
            _positions[last] = tip;
            _previousPositions[last] = tip;
        }
    }
}
