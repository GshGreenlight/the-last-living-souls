using UnityEngine;

namespace LastLivingSouls.Cable
{
    /// <summary>
    /// Keeps cable points outside nearby colliders selected by a physics layer mask.
    /// </summary>
    public sealed class CableObstacleField
    {
        const int DepenetrationPasses = 4;
        const int InitialNearbyCapacity = 32;
        const int InitialSweepCapacity = 16;

        Collider[] _nearby = new Collider[InitialNearbyCapacity];
        RaycastHit[] _sweepHits = new RaycastHit[InitialSweepCapacity];

        SphereCollider _cableProbe;
        float _cableRadius;
        float _collisionSkin;
        int _obstacleMask;

        public void Configure(
            SphereCollider cableProbe,
            float cableRadius,
            float collisionSkin,
            LayerMask obstacleMask)
        {
            _cableProbe = cableProbe;
            _cableRadius = Mathf.Max(0.001f, cableRadius);
            _collisionSkin = Mathf.Max(0f, collisionSkin);
            _obstacleMask = obstacleMask.value;

            ConfigureProbe();
        }

        public Vector3 PushOut(Vector3 point)
        {
            if (_cableProbe == null || _obstacleMask == 0)
                return point;

            float queryRadius = _cableRadius + _collisionSkin;

            // Resolving one overlap can move the point into another collider.
            // A few passes handle compound and overlapping obstacles.
            for (int pass = 0; pass < DepenetrationPasses; pass++)
            {
                bool moved = false;
                int count = QueryNearby(point, queryRadius);

                for (int i = 0; i < count; i++)
                {
                    Collider obstacle = _nearby[i];
                    if (obstacle == null ||
                        obstacle == _cableProbe ||
                        !obstacle.enabled)
                    {
                        continue;
                    }

                    if (!Physics.ComputePenetration(
                            _cableProbe,
                            point,
                            Quaternion.identity,
                            obstacle,
                            obstacle.transform.position,
                            obstacle.transform.rotation,
                            out Vector3 direction,
                            out float distance))
                    {
                        continue;
                    }

                    point += direction * (distance + _collisionSkin);
                    moved = true;
                }

                if (!moved)
                    break;
            }

            return point;
        }

        public Vector3 ConstrainMove(Vector3 from, Vector3 to)
        {
            if (_cableProbe == null || _obstacleMask == 0)
                return to;

            Vector3 delta = to - from;
            float distance = delta.magnitude;
            float sweepRadius = _cableRadius + _collisionSkin;

            // Skip a continuous query only for negligible solver movement.
            // Endpoint depenetration still keeps the point outside obstacles.
            if (distance <= Mathf.Max(0.001f, _collisionSkin))
                return PushOut(to);

            // Sphere casts do not report an overlap at their starting point,
            // so make the start valid before checking the travelled segment.
            from = PushOut(from);

            delta = to - from;
            distance = delta.magnitude;
            if (distance < 0.0001f)
                return from;

            Vector3 direction = delta / distance;
            int count = QuerySweep(from, sweepRadius, direction, distance);

            float nearestDistance = distance;
            bool blocked = false;

            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _sweepHits[i];
                Collider obstacle = hit.collider;
                if (obstacle == null ||
                    obstacle == _cableProbe ||
                    !obstacle.enabled)
                {
                    continue;
                }

                if (hit.distance < nearestDistance)
                {
                    nearestDistance = hit.distance;
                    blocked = true;
                }
            }

            if (blocked)
            {
                float safeDistance = Mathf.Max(
                    0f,
                    nearestDistance - _collisionSkin);
                to = from + direction * safeDistance;
            }

            return PushOut(to);
        }

        public bool IsSegmentClear(Vector3 from, Vector3 to)
        {
            if (_cableProbe == null || _obstacleMask == 0)
                return true;

            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.0001f)
                return true;

            int count = QuerySweep(
                from,
                _cableRadius + _collisionSkin,
                delta / distance,
                distance);

            for (int i = 0; i < count; i++)
            {
                Collider obstacle = _sweepHits[i].collider;
                if (obstacle != null &&
                    obstacle != _cableProbe &&
                    obstacle.enabled)
                {
                    return false;
                }
            }

            return true;
        }

        public bool IsOnContour(Vector3 point, float eps = 0.04f)
        {
            if (_cableProbe == null || _obstacleMask == 0)
                return false;

            float contourDistance =
                _cableRadius + _collisionSkin + Mathf.Max(0f, eps);
            float previousProbeRadius = _cableProbe.radius;

            SetProbeWorldRadius(contourDistance);

            try
            {
                int count = QueryNearby(point, contourDistance);
                for (int i = 0; i < count; i++)
                {
                    Collider obstacle = _nearby[i];
                    if (obstacle == null ||
                        obstacle == _cableProbe ||
                        !obstacle.enabled)
                    {
                        continue;
                    }

                    // The probe is a supported primitive, so the obstacle may
                    // also be a non-convex MeshCollider.
                    if (Physics.ComputePenetration(
                            _cableProbe,
                            point,
                            Quaternion.identity,
                            obstacle,
                            obstacle.transform.position,
                            obstacle.transform.rotation,
                            out _,
                            out _))
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                _cableProbe.radius = previousProbeRadius;
            }
        }

        int QueryNearby(Vector3 point, float radius)
        {
            while (true)
            {
                int count = Physics.OverlapSphereNonAlloc(
                    point,
                    radius,
                    _nearby,
                    _obstacleMask,
                    QueryTriggerInteraction.Ignore);

                if (count < _nearby.Length)
                    return count;

                System.Array.Resize(ref _nearby, _nearby.Length * 2);
            }
        }

        int QuerySweep(
            Vector3 origin,
            float radius,
            Vector3 direction,
            float distance)
        {
            while (true)
            {
                int count = Physics.SphereCastNonAlloc(
                    origin,
                    radius,
                    direction,
                    _sweepHits,
                    distance,
                    _obstacleMask,
                    QueryTriggerInteraction.Ignore);

                if (count < _sweepHits.Length)
                    return count;

                System.Array.Resize(ref _sweepHits, _sweepHits.Length * 2);
            }
        }

        void ConfigureProbe()
        {
            if (_cableProbe == null)
                return;

            // ComputePenetration receives the desired world-space probe pose.
            // Keeping the local center at zero makes that pose unambiguous.
            _cableProbe.center = Vector3.zero;

            SetProbeWorldRadius(_cableRadius);
        }

        void SetProbeWorldRadius(float worldRadius)
        {
            if (_cableProbe == null)
                return;

            Vector3 scale = _cableProbe.transform.lossyScale;
            float maxScale = Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z));

            _cableProbe.radius = Mathf.Max(0.001f, worldRadius) /
                                 Mathf.Max(maxScale, 0.0001f);
        }
    }
}
