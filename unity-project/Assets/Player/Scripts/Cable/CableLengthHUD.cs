using UnityEngine;
using UnityEngine.UI;

namespace LastLivingSouls.Cable
{
    /// <summary>Cable length bar. Builds a Canvas at runtime.</summary>
    public class CableLengthHUD : MonoBehaviour
    {
        [SerializeField] RobotCable cable;
        [SerializeField] Vector2 barSize = new Vector2(320f, 18f);
        [SerializeField] Vector2 barOffset = new Vector2(0f, 36f);
        [SerializeField] Color fillColor = new Color(0.75f, 0.82f, 0.35f, 0.95f);
        [SerializeField] Color backColor = new Color(0f, 0f, 0f, 0.55f);
        [SerializeField] Color tautColor = new Color(0.9f, 0.25f, 0.2f, 0.95f);

        ICableReadout _readout;
        Image _fill;
        Text _label;
        Canvas _canvas;

        void Awake()
        {
            if (cable == null)
                cable = GetComponent<RobotCable>();
            _readout = cable;

            BuildUi();
        }

        void Update()
        {
            if (_readout == null || _fill == null)
                return;

            float used = _readout.UsedNormalized;
            _fill.color = used > 0.98f ? tautColor : fillColor;

            if (_label != null)
                _label.text = $"Cable  {_readout.UsedLength:0.0} / {_readout.MaxLength:0.0} m   [{_readout.StatusHint}]";

            var fillRt = _fill.rectTransform;
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(used, 1f);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
        }

        void BuildUi()
        {
            var canvasGo = new GameObject("CableLengthHUD_Canvas");
            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.AddComponent<GraphicRaycaster>();

            var root = CreateUiObject("BarRoot", canvasGo.transform);
            var rootRt = root.GetComponent<RectTransform>();
            rootRt.anchorMin = rootRt.anchorMax = new Vector2(0.5f, 0f);
            rootRt.pivot = new Vector2(0.5f, 0f);
            rootRt.anchoredPosition = barOffset;
            rootRt.sizeDelta = barSize + new Vector2(0f, 28f);

            var back = CreateUiObject("Background", root.transform);
            var backRt = back.GetComponent<RectTransform>();
            backRt.anchorMin = new Vector2(0f, 0f);
            backRt.anchorMax = new Vector2(1f, 0f);
            backRt.pivot = new Vector2(0.5f, 0f);
            backRt.anchoredPosition = Vector2.zero;
            backRt.sizeDelta = new Vector2(0f, barSize.y);
            back.AddComponent<Image>().color = backColor;

            var fillGo = CreateUiObject("Fill", back.transform);
            var fillRt = fillGo.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
            _fill = fillGo.AddComponent<Image>();
            _fill.color = fillColor;

            var labelGo = CreateUiObject("Label", root.transform);
            var labelRt = labelGo.GetComponent<RectTransform>();
            labelRt.anchorMin = new Vector2(0f, 0f);
            labelRt.anchorMax = new Vector2(1f, 0f);
            labelRt.pivot = new Vector2(0.5f, 0f);
            labelRt.anchoredPosition = new Vector2(0f, barSize.y + 4f);
            labelRt.sizeDelta = new Vector2(0f, 22f);
            _label = labelGo.AddComponent<Text>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")
                          ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            _label.fontSize = 14;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.color = Color.white;
            _label.text = "Cable";
        }

        static GameObject CreateUiObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return go;
        }

        void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }
    }
}
