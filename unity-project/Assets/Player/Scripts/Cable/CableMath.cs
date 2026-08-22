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
    }
}
