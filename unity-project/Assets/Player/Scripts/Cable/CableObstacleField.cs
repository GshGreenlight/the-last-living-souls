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
