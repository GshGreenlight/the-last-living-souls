using System.Collections.Generic;
using UnityEngine;

namespace LastLivingSouls.Cable
{
    public static class CableMath
    {
        public static Vector3 Floor(Vector3 world, float floorY)
        {
            world.y = floorY;
            return world;
        }

        public static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }

        public static float MeasureLength(IReadOnlyList<Vector3> points, Vector3 tip)
        {
            if (points == null || points.Count == 0)
                return 0f;

            float len = 0f;
            for (int i = 1; i < points.Count; i++)
                len += HorizontalDistance(points[i - 1], points[i]);
            len += HorizontalDistance(points[points.Count - 1], tip);
            return len;
        }

        public static void Densify(
            IReadOnlyList<Vector3> src,
            List<Vector3> dst,
            float spacing,
            float floorY)
        {
            dst.Clear();
            if (src == null || src.Count == 0)
                return;

            dst.Add(Floor(src[0], floorY));
            for (int i = 1; i < src.Count; i++)
            {
                Vector3 a = Floor(src[i - 1], floorY);
                Vector3 b = Floor(src[i], floorY);
                float dist = HorizontalDistance(a, b);
                int segs = Mathf.Max(1, Mathf.CeilToInt(dist / spacing));
                for (int s = 1; s <= segs; s++)
                    dst.Add(Floor(Vector3.Lerp(a, b, s / (float)segs), floorY));
            }
        }

        public static void CollapseCollinear(
            List<Vector3> path,
            float floorY,
            System.Func<Vector3, bool> keepPoint)
        {
            if (path == null || path.Count < 3)
                return;

            bool removed;
            int guard = 0;
            do
            {
                removed = false;
                for (int i = 1; i < path.Count - 1; i++)
                {
                    Vector3 a = path[i - 1];
                    Vector3 b = path[i];
                    Vector3 c = path[i + 1];

                    Vector3 ab = c - a;
                    ab.y = 0f;
                    if (ab.sqrMagnitude < 0.0001f)
                    {
                        path.RemoveAt(i);
                        removed = true;
                        break;
                    }

                    if (keepPoint != null && keepPoint(b))
                        continue;

                    Vector3 ap = b - a;
                    ap.y = 0f;
                    float t = Vector3.Dot(ap, ab) / ab.sqrMagnitude;
                    Vector3 proj = a + ab * t;
                    proj.y = floorY;
                    if (HorizontalDistance(b, proj) < 0.06f)
                    {
                        path.RemoveAt(i);
                        removed = true;
                        break;
                    }
                }
            } while (removed && guard++ < 2048);
        }
    }
}
