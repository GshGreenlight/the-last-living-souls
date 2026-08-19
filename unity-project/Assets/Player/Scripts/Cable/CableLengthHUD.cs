using UnityEngine;
using UnityEngine.UI;

namespace LastLivingSouls.Cable
{
    public sealed class CableLengthHUD : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] RobotCable cable;
        [SerializeField] Image fill;
        [SerializeField] Text label;

        [Header("Colors")]
        [SerializeField]
        Color normalColor =
            new Color(0.75f, 0.82f, 0.35f, 0.95f);

        [SerializeField]
        Color tautColor =
            new Color(0.9f, 0.25f, 0.2f, 0.95f);

        void Awake()
        {
            if (cable == null || fill == null || label == null)
            {
                Debug.LogError(
                    $"{nameof(CableLengthHUD)}: assign Cable, Fill and Label.",
                    this);

                enabled = false;
            }
        }

        void Update()
        {
            float used = cable.UsedNormalized;

            fill.fillAmount = used;
            fill.color = used > 0.98f
                ? tautColor
                : normalColor;

            label.text =
                $"Cable  {cable.UsedLength:0.0} / " +
                $"{cable.MaxLength:0.0} m  " +
                $"[{cable.StatusHint}]";
        }
    }
}