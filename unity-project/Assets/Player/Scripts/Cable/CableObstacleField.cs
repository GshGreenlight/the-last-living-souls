using System.Collections.Generic;
using UnityEngine;

namespace LastLivingSouls.Cable
{
    public struct CableRectXZ
    {
        public float MinX, MaxX, MinZ, MaxZ;
        public string Name;
    }

    /// <summary>XZ obstacle footprints the hose cannot cut through.</summary>
    public sealed class CableObstacleField
    {
        readonly List<CableRectXZ> _rects = new List<CableRectXZ>(32);

        public IReadOnlyList<CableRectXZ> Rects => _rects;

        public void Collect(Transform ignoreRoot, float padding)
        {
            _rects.Clear();

            Collider[] all = Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                Collider col = all[i];
                if (col == null || !col.enabled || col.isTrigger)
                    continue;
                if (ignoreRoot != null &&
                    (col.transform == ignoreRoot || col.transform.IsChildOf(ignoreRoot)))
                    continue;
                if (col.gameObject.name == "Ground")
                    continue;

                Bounds b = col.bounds;
                if (b.size.y < 0.2f && b.size.x > 5f && b.size.z > 5f)
                    continue;

                _rects.Add(new CableRectXZ
                {
                    MinX = b.min.x - padding,
                    MaxX = b.max.x + padding,
                    MinZ = b.min.z - padding,
                    MaxZ = b.max.z + padding,
                    Name = col.gameObject.name
                });
            }
        }

        public Vector3 PushOut(Vector3 point)
        {
            for (int i = 0; i < _rects.Count; i++)
                point = PushOutOfRect(point, _rects[i]);
            return point;
        }

        public bool IsOnContour(Vector3 point, float eps = 0.04f)
        {
            for (int i = 0; i < _rects.Count; i++)
            {
                CableRectXZ r = _rects[i];
                bool onX = Mathf.Abs(point.x - r.MinX) < eps || Mathf.Abs(point.x - r.MaxX) < eps;
                bool onZ = Mathf.Abs(point.z - r.MinZ) < eps || Mathf.Abs(point.z - r.MaxZ) < eps;
                bool inX = point.x >= r.MinX - eps && point.x <= r.MaxX + eps;
                bool inZ = point.z >= r.MinZ - eps && point.z <= r.MaxZ + eps;
                if ((onX && inZ) || (onZ && inX))
                    return true;
            }

            return false;
        }

        static Vector3 PushOutOfRect(Vector3 p, CableRectXZ r)
        {
            if (p.x <= r.MinX || p.x >= r.MaxX || p.z <= r.MinZ || p.z >= r.MaxZ)
                return p;

            float toMinX = p.x - r.MinX;
            float toMaxX = r.MaxX - p.x;
            float toMinZ = p.z - r.MinZ;
            float toMaxZ = r.MaxZ - p.z;

            float best = toMinX;
            int side = 0;
            if (toMaxX < best) { best = toMaxX; side = 1; }
            if (toMinZ < best) { best = toMinZ; side = 2; }
            if (toMaxZ < best) { side = 3; }

            if (side == 0) p.x = r.MinX;
            else if (side == 1) p.x = r.MaxX;
            else if (side == 2) p.z = r.MinZ;
            else p.z = r.MaxZ;

            return p;
        }
    }
}
