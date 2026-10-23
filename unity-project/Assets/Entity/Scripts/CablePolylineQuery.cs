using UnityEngine;

/// <summary>
/// Read-only helpers for distance / nearest point on PhysicalCable active polyline.
/// </summary>
public static class CablePolylineQuery
{
    public struct NearestResult
    {
        public float Distance;
        public Vector3 Point;
        public int SegmentIndex;
        public float SegmentT;
        public bool Valid;
    }

    /// <summary>
    /// Fills <paramref name="buffer"/> via cable.GetActivePositions. Returns point count.
    /// </summary>
    public static int Sample(PhysicalCable cable, Vector3[] buffer)
    {
        if (cable == null || buffer == null || buffer.Length == 0)
            return 0;

        return cable.GetActivePositions(buffer);
    }

    /// <summary>
    /// Nearest point on the polyline in XZ (Y ignored for distance). Height check optional via maxYDelta.
    /// </summary>
    public static NearestResult FindNearestXZ(
        Vector3 origin,
        Vector3[] points,
        int pointCount,
        float maxYDelta = float.PositiveInfinity)
    {
        var result = new NearestResult { Distance = float.PositiveInfinity, Valid = false };

        if (points == null || pointCount < 2)
            return result;

        Vector3 o = origin;
        o.y = 0f;

        for (int i = 0; i < pointCount - 1; i++)
        {
            Vector3 a = points[i];
            Vector3 b = points[i + 1];

            float midY = (a.y + b.y) * 0.5f;
            if (Mathf.Abs(origin.y - midY) > maxYDelta)
                continue;

            Vector3 aXZ = a; aXZ.y = 0f;
            Vector3 bXZ = b; bXZ.y = 0f;
            Vector3 ab = bXZ - aXZ;
            float abSqr = ab.sqrMagnitude;
            float t = abSqr > 1e-8f ? Mathf.Clamp01(Vector3.Dot(o - aXZ, ab) / abSqr) : 0f;

            Vector3 closestXZ = aXZ + ab * t;
            float dist = Vector3.Distance(o, closestXZ);

            if (dist < result.Distance)
            {
                Vector3 closest = Vector3.Lerp(a, b, t);
                result.Distance = dist;
                result.Point = closest;
                result.SegmentIndex = i;
                result.SegmentT = t;
                result.Valid = true;
            }
        }

        return result;
    }

    /// <summary>
    /// Advance along polyline toward the end (player attach). Returns next world target.
    /// progress01 is 0..1 along total length from start; returns false if path invalid.
    /// </summary>
    public static bool GetPointAlongPath(
        Vector3[] points,
        int pointCount,
        float distanceFromStart,
        out Vector3 worldPoint,
        out int segmentIndex,
        out bool reachedEnd)
    {
        worldPoint = Vector3.zero;
        segmentIndex = 0;
        reachedEnd = false;

        if (points == null || pointCount < 2)
            return false;

        float remaining = Mathf.Max(0f, distanceFromStart);

        for (int i = 0; i < pointCount - 1; i++)
        {
            float segLen = Vector3.Distance(points[i], points[i + 1]);
            if (remaining <= segLen || i == pointCount - 2)
            {
                float t = segLen > 1e-6f ? Mathf.Clamp01(remaining / segLen) : 1f;
                worldPoint = Vector3.Lerp(points[i], points[i + 1], t);
                segmentIndex = i;
                reachedEnd = (i == pointCount - 2 && t >= 0.999f);
                return true;
            }

            remaining -= segLen;
        }

        worldPoint = points[pointCount - 1];
        segmentIndex = pointCount - 2;
        reachedEnd = true;
        return true;
    }

    public static float PathLength(Vector3[] points, int pointCount)
    {
        if (points == null || pointCount < 2)
            return 0f;

        float len = 0f;
        for (int i = 0; i < pointCount - 1; i++)
            len += Vector3.Distance(points[i], points[i + 1]);
        return len;
    }

    public static float DistanceAlongPathToNearest(
        Vector3[] points,
        int pointCount,
        int segmentIndex,
        float segmentT)
    {
        if (points == null || pointCount < 2 || segmentIndex < 0)
            return 0f;

        float dist = 0f;
        for (int i = 0; i < segmentIndex && i < pointCount - 1; i++)
            dist += Vector3.Distance(points[i], points[i + 1]);

        if (segmentIndex < pointCount - 1)
            dist += Vector3.Distance(points[segmentIndex], points[segmentIndex + 1]) * Mathf.Clamp01(segmentT);

        return dist;
    }
}
