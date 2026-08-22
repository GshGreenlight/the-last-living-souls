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

        Collider[] _nearby = new Collider[InitialNearbyCapacity];

        SphereCollider _cableProbe;
        CapsuleCollider _cableSegmentProbe;
        float _cableRadius;
        float _collisionSkin;
        int _obstacleMask;
        Transform _ignoredRoot;

        public void Configure(
            SphereCollider cableProbe,
            CapsuleCollider cableSegmentProbe,
            float cableRadius,
            float collisionSkin,
            LayerMask obstacleMask,
            Transform ignoredRoot)
        {
            _cableProbe = cableProbe;
            _cableSegmentProbe = cableSegmentProbe;
            _cableRadius = Mathf.Max(0.001f, cableRadius);
            _collisionSkin = Mathf.Max(0f, collisionSkin);
            _obstacleMask = obstacleMask.value;
            _ignoredRoot = ignoredRoot;

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
                    if (!IsObstacle(obstacle))
                        continue;

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

        /// <summary>
        /// Keeps the complete volume between two neighbouring cable particles
        /// outside obstacles. Free endpoints receive the capsule's minimum
        /// translation; pinned cable endpoints remain fixed.
        /// </summary>
        public void PushSegmentOut(
            ref Vector3 a,
            ref Vector3 b,
            bool aPinned,
            bool bPinned)
        {
            if (_cableSegmentProbe == null ||
                _obstacleMask == 0 ||
                (aPinned && bPinned))
            {
                return;
            }

            float queryRadius = _cableRadius + _collisionSkin;

            for (int pass = 0; pass < DepenetrationPasses; pass++)
            {
                bool moved = false;
                int count = QueryNearby(a, b, queryRadius);

                for (int i = 0; i < count; i++)
                {
                    Collider obstacle = _nearby[i];
                    if (!IsObstacle(obstacle))
                        continue;

                    ConfigureSegmentProbe(
                        a,
                        b,
                        out Vector3 probePosition,
                        out Quaternion probeRotation);

                    if (!Physics.ComputePenetration(
                            _cableSegmentProbe,
                            probePosition,
                            probeRotation,
                            obstacle,
                            obstacle.transform.position,
                            obstacle.transform.rotation,
                            out Vector3 direction,
                            out float distance))
                    {
                        continue;
                    }

                    Vector3 correction =
                        direction * (distance + _collisionSkin);

                    if (aPinned)
                    {
                        // Moving the free end twice as far gives the capsule
                        // midpoint the requested minimum translation.
                        b += correction * 2f;
                    }
                    else if (bPinned)
                    {
                        a += correction * 2f;
                    }
                    else
                    {
                        a += correction;
                        b += correction;
                    }

                    moved = true;
                }

                if (!moved)
                    break;
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

        int QueryNearby(Vector3 a, Vector3 b, float radius)
        {
            while (true)
            {
                int count = Physics.OverlapCapsuleNonAlloc(
                    a,
                    b,
                    radius,
                    _nearby,
                    _obstacleMask,
                    QueryTriggerInteraction.Ignore);

                if (count < _nearby.Length)
                    return count;

                System.Array.Resize(ref _nearby, _nearby.Length * 2);
            }
        }

        bool IsObstacle(Collider obstacle)
        {
            return obstacle != null &&
                   obstacle != _cableProbe &&
                   obstacle != _cableSegmentProbe &&
                   obstacle.enabled &&
                   (_ignoredRoot == null ||
                    !obstacle.transform.IsChildOf(_ignoredRoot));
        }

        void ConfigureProbe()
        {
            if (_cableProbe != null)
            {
                // ComputePenetration receives the desired world-space probe
                // pose. A zero local center keeps that pose unambiguous.
                _cableProbe.center = Vector3.zero;
                SetProbeWorldRadius(_cableRadius);
            }

            if (_cableSegmentProbe == null)
                return;

            _cableSegmentProbe.center = Vector3.zero;
            _cableSegmentProbe.direction = 2;
        }

        void ConfigureSegmentProbe(
            Vector3 a,
            Vector3 b,
            out Vector3 position,
            out Quaternion rotation)
        {
            Vector3 segment = b - a;
            float length = segment.magnitude;

            position = (a + b) * 0.5f;
            rotation = length > 0.0001f
                ? Quaternion.FromToRotation(Vector3.forward, segment / length)
                : Quaternion.identity;

            Vector3 scale = _cableSegmentProbe.transform.lossyScale;
            float radialScale = Mathf.Max(
                Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)),
                0.0001f);
            float axialScale = Mathf.Max(Mathf.Abs(scale.z), 0.0001f);
            float localRadius = _cableRadius / radialScale;

            _cableSegmentProbe.radius = localRadius;
            _cableSegmentProbe.height = Mathf.Max(
                localRadius * 2f,
                length / axialScale + localRadius * 2f);
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
