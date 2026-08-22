using System.Collections.Generic;
using UnityEngine;

namespace LastLivingSouls.Cable
{
    /// <summary>Rubber-band shrink of a floor polyline around obstacles.</summary>
    public sealed class CableRubberBand
    {
        readonly List<Vector3> _work = new List<Vector3>(512);
        readonly List<Vector3> _next = new List<Vector3>(512);
        readonly List<int> _simplifyStack = new List<int>(512);

        bool[] _simplifyKeep = new bool[512];

        float _tipSampleSpacing;

        public List<Vector3> Work => _work;

        public void Begin(
            IReadOnlyList<Vector3> path,
            Vector3 start,
            Vector3 tip,
            float floorY,
            float solverPointSpacing)
        {
            _tipSampleSpacing = Mathf.Max(0.02f, solverPointSpacing);
            CableMath.Densify(path, _work, _tipSampleSpacing, floorY);
            if (_work.Count == 0)
                _work.Add(CableMath.Floor(start, floorY));
            else
                _work[0] = CableMath.Floor(start, floorY);

            Vector3 pinnedTip = CableMath.Floor(tip, floorY);
            if (_work.Count == 1 ||
                CableMath.HorizontalDistance(
                    _work[_work.Count - 1],
                    pinnedTip) > 0.001f)
            {
                _work.Add(pinnedTip);
            }
            else
            {
                _work[_work.Count - 1] = pinnedTip;
            }
        }

        public void Step(
            Vector3 start,
            Vector3 tip,
            float floorY,
            float pull,
            int iterations,
            float pointMergeDistance,
            CableObstacleField obstacles)
        {
            if (_work.Count < 2)
                return;

            TrackMovingTip(tip, floorY);
            _work[0] = CableMath.Floor(start, floorY);
            _work[_work.Count - 1] = CableMath.Floor(tip, floorY);

            pull = Mathf.Clamp01(pull);
            iterations = Mathf.Max(1, iterations);
            pointMergeDistance = Mathf.Max(0f, pointMergeDistance);
            for (int s = 0; s < iterations; s++)
            {
                RunPass(
                    start,
                    floorY,
                    pull,
                    pointMergeDistance,
                    obstacles);
            }
        }

        public void Finish(
            CableObstacleField obstacles,
            float simplificationTolerance = 0.002f)
        {
            if (_work.Count < 3)
                return;

            int pointCount = _work.Count;
            if (_simplifyKeep.Length < pointCount)
                System.Array.Resize(ref _simplifyKeep, pointCount * 2);

            System.Array.Clear(_simplifyKeep, 0, pointCount);
            _simplifyStack.Clear();

            _simplifyKeep[0] = true;
            _simplifyKeep[pointCount - 1] = true;
            PushSimplifyRange(0, pointCount - 1);

            float tolerance = Mathf.Max(0f, simplificationTolerance);
            float toleranceSq = tolerance * tolerance;

            while (_simplifyStack.Count >= 2)
            {
                int last = PopSimplifyIndex();
                int first = PopSimplifyIndex();
                if (last <= first + 1)
                    continue;

                int split = FindFarthestPoint(first, last, out float errorSq);

                if (errorSq <= toleranceSq &&
                    (obstacles == null ||
                     obstacles.IsSegmentClear(_work[first], _work[last])))
                {
                    continue;
                }

                // If geometry is already within tolerance but the shortcut is
                // blocked, split conservatively until collision-free ranges remain.
                if (errorSq <= toleranceSq)
                    split = first + (last - first) / 2;

                _simplifyKeep[split] = true;
                PushSimplifyRange(first, split);
                PushSimplifyRange(split, last);
            }

            _next.Clear();
            for (int i = 0; i < pointCount; i++)
            {
                if (_simplifyKeep[i])
                    _next.Add(_work[i]);
            }

            _work.Clear();
            _work.AddRange(_next);
        }

        public void Clear() => _work.Clear();

        public void Translate(Vector3 delta, float floorY)
        {
            for (int i = 0; i < _work.Count; i++)
                _work[i] = CableMath.Floor(_work[i] + delta, floorY);
        }

        public void PinStart(Vector3 start, float floorY)
        {
            if (_work.Count > 0)
                _work[0] = CableMath.Floor(start, floorY);
        }

        void TrackMovingTip(Vector3 tip, float floorY)
        {
            if (_work.Count < 2)
                return;

            tip = CableMath.Floor(tip, floorY);
            int tipIndex = _work.Count - 1;
            Vector3 previousTip = _work[tipIndex];

            if (CableMath.HorizontalDistance(previousTip, tip) < 0.001f)
                return;

            Vector3 previousPoint = _work[tipIndex - 1];
            if (CableMath.HorizontalDistance(previousPoint, tip) >=
                _tipSampleSpacing)
            {
                // Keep the previous player position as an interior cable point.
                // This records which side of an obstacle the tip travelled around.
                _work.Add(tip);
            }
            else
            {
                _work[tipIndex] = tip;
            }
        }

        void PushSimplifyRange(int first, int last)
        {
            _simplifyStack.Add(first);
            _simplifyStack.Add(last);
        }

        int PopSimplifyIndex()
        {
            int index = _simplifyStack.Count - 1;
            int value = _simplifyStack[index];
            _simplifyStack.RemoveAt(index);
            return value;
        }

        int FindFarthestPoint(int first, int last, out float errorSq)
        {
            Vector3 a = _work[first];
            Vector3 b = _work[last];
            int farthest = first + 1;
            errorSq = -1f;

            for (int i = first + 1; i < last; i++)
            {
                float distanceSq = HorizontalDistanceToSegmentSq(
                    _work[i],
                    a,
                    b);

                if (distanceSq > errorSq)
                {
                    errorSq = distanceSq;
                    farthest = i;
                }
            }

            return farthest;
        }

        static float HorizontalDistanceToSegmentSq(
            Vector3 point,
            Vector3 a,
            Vector3 b)
        {
            Vector3 ab = b - a;
            Vector3 ap = point - a;
            ab.y = 0f;
            ap.y = 0f;

            float lengthSq = ab.sqrMagnitude;
            if (lengthSq < 0.000001f)
                return ap.sqrMagnitude;

            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / lengthSq);
            Vector3 offset = ap - ab * t;
            return offset.sqrMagnitude;
        }

        void RunPass(
            Vector3 start,
            float floorY,
            float pull,
            float pointMergeDistance,
            CableObstacleField obstacles)
        {
            if (_work.Count < 3)
                return;

            Vector3 pinnedStart = CableMath.Floor(start, floorY);
            Vector3 pinnedTip = _work[_work.Count - 1];

            _next.Clear();
            _next.Add(pinnedStart);

            for (int i = 1; i < _work.Count - 1; i++)
            {
                Vector3 prev = _work[i - 1];
                Vector3 curr = _work[i];
                Vector3 next = _work[i + 1];

                Vector3 mid = (prev + next) * 0.5f;
                Vector3 pulled = Vector3.Lerp(curr, mid, pull);
                if (obstacles != null)
                    pulled = obstacles.PushOut(pulled);
                _next.Add(CableMath.Floor(pulled, floorY));
            }

            _next.Add(pinnedTip);

            for (int i = _next.Count - 2; i >= 1; i--)
            {
                if (CableMath.HorizontalDistance(_next[i], _next[i - 1]) <
                    pointMergeDistance)
                {
                    _next.RemoveAt(i);
                }
            }

            _work.Clear();
            _work.AddRange(_next);
            if (_work.Count > 0)
                _work[0] = pinnedStart;
            if (_work.Count > 1)
                _work[_work.Count - 1] = pinnedTip;
        }
    }
}
