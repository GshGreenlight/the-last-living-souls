using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace LastLivingSouls.Cable
{
    /// <summary>Owns LineRenderer + runtime hose material.</summary>
    public sealed class CableHoseVisual
    {
        readonly LineRenderer _line;
        Material _material;

        public CableHoseVisual(GameObject host)
        {
            _line = host.GetComponent<LineRenderer>();
            if (_line == null)
                _line = host.AddComponent<LineRenderer>();

            _line.useWorldSpace = true;
            _line.alignment = LineAlignment.View;
            _line.textureMode = LineTextureMode.Stretch;
            _line.numCapVertices = 6;
            _line.numCornerVertices = 4;
            _line.shadowCastingMode = ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.widthMultiplier = 1f;
        }

        public void ApplyStyle(float width, Color color)
        {
            if (_material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");

                _material = new Material(shader);
            }

            if (_material.HasProperty("_BaseColor"))
                _material.SetColor("_BaseColor", color);
            else if (_material.HasProperty("_Color"))
                _material.SetColor("_Color", color);

            _line.sharedMaterial = _material;
            _line.startColor = color;
            _line.endColor = color;
            _line.startWidth = width;
            _line.endWidth = width;
        }

        public void Refresh(IReadOnlyList<Vector3> points, Vector3 tip, float width)
        {
            if (_line == null || points == null)
                return;

            _line.startWidth = width;
            _line.endWidth = width;

            int count = points.Count + 1;
            _line.positionCount = count;
            for (int i = 0; i < points.Count; i++)
                _line.SetPosition(i, points[i]);
            _line.SetPosition(count - 1, tip);
        }

        public void Dispose()
        {
            if (_material != null)
            {
                Object.Destroy(_material);
                _material = null;
            }
        }
    }
}
