using System.Collections.Generic;
using UnityEngine;

namespace LastLivingSouls.Cable
{
    /// <summary>
    /// Live obstacle colliders used to keep cable points outside their real 3D shapes.
    /// </summary>
    public sealed class CableObstacleField
    {
        const int DepenetrationPasses = 4;

        readonly List<Collider> _colliders = new List<Collider>(32);

        SphereCollider _cableProbe;
        float _cableRadius;
        float _collisionSkin;

        public IReadOnlyList<Collider> Colliders => _colliders;

        public void Collect(
            Transform ignoreRoot,
            SphereCollider cableProbe,
            float cableRadius,
            float collisionSkin)
        {
            _colliders.Clear();

            _cableProbe = cableProbe;
            _cableRadius = Mathf.Max(0.001f, cableRadius);
            _collisionSkin = Mathf.Max(0f, collisionSkin);

            ConfigureProbe();

            CableObstacle[] obstacles =
                Object.FindObjectsByType<CableObstacle>(
                    FindObjectsInactive.Exclude,
                    FindObjectsSortMode.None);

            foreach (CableObstacle obstacle in obstacles)
            {
                Collider[] colliders =
                    obstacle.GetComponentsInChildren<Collider>(
                        includeInactive: false);

                foreach (Collider col in colliders)
                {
                    if (col == null ||
                        col == _cableProbe ||
                        !col.enabled ||
                        col.isTrigger)
                    {
                        continue;
                    }

                    if (ignoreRoot != null &&
                        (col.transform == ignoreRoot ||
                         col.transform.IsChildOf(ignoreRoot)))
                    {
                        continue;
                    }

                    _colliders.Add(col);
                }
            }
        }

        public Vector3 PushOut(Vector3 point)
        {
            if (_cableProbe == null)
                return point;

            float broadPhaseRadius = _cableRadius + _collisionSkin;
            float broadPhaseRadiusSqr = broadPhaseRadius * broadPhaseRadius;

            // Resolving one overlap can move the point into another collider.
            // A few passes handle compound and overlapping obstacles.
            for (int pass = 0; pass < DepenetrationPasses; pass++)
            {
                bool moved = false;

                for (int i = 0; i < _colliders.Count; i++)
                {
                    Collider obstacle = _colliders[i];
                    if (obstacle == null || !obstacle.enabled)
                        continue;

                    // Bounds are only a cheap broad-phase rejection here. The
                    // actual contact is calculated against the real collider.
                    if (obstacle.bounds.SqrDistance(point) > broadPhaseRadiusSqr)
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

        public bool IsOnContour(Vector3 point, float eps = 0.04f)
        {
            float contourDistance = _cableRadius + _collisionSkin + Mathf.Max(0f, eps);
            float contourDistanceSqr = contourDistance * contourDistance;

            for (int i = 0; i < _colliders.Count; i++)
            {
                Collider obstacle = _colliders[i];
                if (obstacle == null || !obstacle.enabled)
                    continue;

                if (obstacle.bounds.SqrDistance(point) > contourDistanceSqr)
                    continue;

                Vector3 closest = obstacle.ClosestPoint(point);
                if ((closest - point).sqrMagnitude <= contourDistanceSqr)
                    return true;
            }

            return false;
        }

        void ConfigureProbe()
        {
            if (_cableProbe == null)
                return;

            // ComputePenetration receives the desired world-space probe pose.
            // Keeping the local center at zero makes that pose unambiguous.
            _cableProbe.center = Vector3.zero;

            Vector3 scale = _cableProbe.transform.lossyScale;
            float maxScale = Mathf.Max(
                Mathf.Abs(scale.x),
                Mathf.Abs(scale.y),
                Mathf.Abs(scale.z));

            _cableProbe.radius = _cableRadius / Mathf.Max(maxScale, 0.0001f);
        }
    }
}
