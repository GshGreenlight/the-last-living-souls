using System.Collections.Generic;
using UnityEngine;

namespace LastLivingSouls.Cable
{
    /// <summary>Rubber-band shrink of a floor polyline around obstacles.</summary>
    public sealed class CableRubberBand
    {
        readonly List<Vector3> _work = new List<Vector3>(512);
        readonly List<Vector3> _next = new List<Vector3>(512);

        public List<Vector3> Work => _work;

        public void Begin(
            IReadOnlyList<Vector3> path,
            Vector3 start,
            Vector3 tip,
            float floorY,
            float densifySpacing = 0.12f)
        {
            CableMath.Densify(path, _work, densifySpacing, floorY);
            if (_work.Count == 0)
                _work.Add(CableMath.Floor(start, floorY));
            else
                _work[0] = CableMath.Floor(start, floorY);

            _work.Add(CableMath.Floor(tip, floorY));
        }

        public void Step(
            Vector3 start,
            Vector3 tip,
            float floorY,
            float pull,
            int iterations,
            CableObstacleField obstacles)
        {
            if (_work.Count < 2)
                return;

            _work[0] = CableMath.Floor(start, floorY);
            _work[_work.Count - 1] = CableMath.Floor(tip, floorY);

            pull = Mathf.Clamp01(pull);
            iterations = Mathf.Max(1, iterations);
            for (int s = 0; s < iterations; s++)
                RunPass(start, tip, floorY, pull, obstacles);
        }

        public void Finish(float floorY, CableObstacleField obstacles)
        {
            if (_work.Count >= 3)
            {
                CableMath.CollapseCollinear(
                    _work,
                    floorY,
                    p => obstacles != null && obstacles.IsOnContour(p));
            }
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

        void RunPass(
            Vector3 start,
            Vector3 tip,
            float floorY,
            float pull,
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
                if (CableMath.HorizontalDistance(_next[i], _next[i - 1]) < 0.04f)
                    _next.RemoveAt(i);
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
