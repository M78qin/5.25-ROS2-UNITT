using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DigitalTwin
{
    public enum CardState
    {
        Hidden,
        Collapsed,
        Expanded
    }

    [DisallowMultipleComponent]
    public sealed class TwinRuntimeUICard : MonoBehaviour
    {
        [SerializeField] private GameObject headerRoot;
        [SerializeField] private GameObject bodyRoot;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text tagText;
        [SerializeField] private TMP_Text chevronText;
        [SerializeField] private Image accentImage;
        [SerializeField] private Button toggleButton;
        [SerializeField] private CardState state = CardState.Expanded;

        public CardState State => state;
        public bool BodyRefreshAllowed => state == CardState.Expanded && isActiveAndEnabled;

        public void Initialize(
            GameObject header,
            GameObject body,
            TMP_Text title,
            TMP_Text tag,
            TMP_Text chevron,
            Image accent,
            Button toggle)
        {
            headerRoot = header;
            bodyRoot = body;
            titleText = title;
            tagText = tag;
            chevronText = chevron;
            accentImage = accent;
            toggleButton = toggle;

            if (toggleButton != null)
            {
                toggleButton.onClick.RemoveListener(ToggleCollapsed);
                toggleButton.onClick.AddListener(ToggleCollapsed);
            }

            SetState(state);
        }

        public void SetTitle(string value)
        {
            titleText.SetIfChanged(value);
        }

        public void SetTag(string value)
        {
            if (tagText == null)
            {
                return;
            }

            tagText.gameObject.SetActive(!string.IsNullOrWhiteSpace(value));
            tagText.SetIfChanged(value);
        }

        public void SetAccentColor(Color color)
        {
            if (accentImage != null)
            {
                accentImage.color = color;
            }
        }

        public void SetState(CardState nextState)
        {
            state = nextState;
            bool visible = state != CardState.Hidden;
            gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            if (headerRoot != null)
            {
                headerRoot.SetActive(true);
            }

            if (bodyRoot != null)
            {
                bodyRoot.SetActive(state == CardState.Expanded);
            }

            chevronText.SetIfChanged(state == CardState.Expanded ? "v" : ">");
        }

        public void ToggleCollapsed()
        {
            SetState(state == CardState.Expanded ? CardState.Collapsed : CardState.Expanded);
        }
    }
}
