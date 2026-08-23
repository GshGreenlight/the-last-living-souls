using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class CableVisual : MonoBehaviour
{
    public PhysicalCable cable;

    [Header("Smoothing")]
    [Range(1, 20)]
    public int subdivisionsPerSegment = 8;

    private LineRenderer line;
    private Vector3[] controlPoints;
    private Vector3[] smoothedPoints;

    private void Awake()
    {
        line = GetComponent<LineRenderer>();
        line.useWorldSpace = true;
    }

    private void LateUpdate()
    {
        if (cable == null)
        {
            line.positionCount = 0;
            return;
        }

        int maxPoints = cable.MaxPossiblePoints;

        if (controlPoints == null || controlPoints.Length < maxPoints)
            controlPoints = new Vector3[maxPoints];

        int count = cable.GetActivePositions(controlPoints);

        if (count < 2)
        {
            line.positionCount = 0;
            return;
        }

        int maxSmoothed = (maxPoints - 1) * subdivisionsPerSegment + 1;

        if (smoothedPoints == null || smoothedPoints.Length < maxSmoothed)
            smoothedPoints = new Vector3[maxSmoothed];

        int idx = 0;
        int steps = subdivisionsPerSegment;

        for (int i = 0; i < count - 1; i++)
        {
            Vector3 p0 = GetPoint(controlPoints, count, i - 1);
            Vector3 p1 = GetPoint(controlPoints, count, i);
            Vector3 p2 = GetPoint(controlPoints, count, i + 1);
            Vector3 p3 = GetPoint(controlPoints, count, i + 2);

            int lastStep = (i == count - 2) ? steps : steps - 1;

            for (int s = 0; s <= lastStep; s++)
            {
                float t = (float)s / steps;
                smoothedPoints[idx++] = CatmullRom(p0, p1, p2, p3, t);
            }
        }

        line.positionCount = idx;
        line.SetPositions(smoothedPoints);
    }

    private Vector3 GetPoint(Vector3[] pts, int count, int index)
    {
        index = Mathf.Clamp(index, 0, count - 1);
        return pts[index];
    }

    private Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;

        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t3
        );
    }
}