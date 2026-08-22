using System.Collections.Generic;
using UnityEngine;

namespace LastLivingSouls.Cable
{
    public static class CableMath
    {
        public static float MeasureLength(IReadOnlyList<Vector3> points)
        {
            if (points == null || points.Count < 2)
                return 0f;

            float len = 0f;
            for (int i = 1; i < points.Count; i++)
                len += Vector3.Distance(points[i - 1], points[i]);
            return len;
        }

        public static float MeasureLengthToTip(
            IReadOnlyList<Vector3> points,
            Vector3 tip)
        {
            if (points == null || points.Count == 0)
                return 0f;

            if (points.Count == 1)
                return Vector3.Distance(points[0], tip);

            float len = 0f;
            for (int i = 1; i < points.Count - 1; i++)
                len += Vector3.Distance(points[i - 1], points[i]);

            len += Vector3.Distance(points[points.Count - 2], tip);
            return len;
        }

        public static void Densify(
            IReadOnlyList<Vector3> src,
            List<Vector3> dst,
            float spacing)
        {
            dst.Clear();
            if (src == null || src.Count == 0)
                return;

            spacing = Mathf.Max(0.001f, spacing);
            dst.Add(src[0]);
            for (int i = 1; i < src.Count; i++)
            {
                Vector3 a = src[i - 1];
                Vector3 b = src[i];
                float dist = Vector3.Distance(a, b);
                int segs = Mathf.Max(1, Mathf.CeilToInt(dist / spacing));
                for (int s = 1; s <= segs; s++)
                    dst.Add(Vector3.Lerp(a, b, s / (float)segs));
            }
        }
    }
}
