using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DigitalTwin
{
    [DisallowMultipleComponent]
    public sealed class TwinRuntimeUIPanelInteractor : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        [SerializeField] private TwinRuntimeUIBindings bindings;
        [SerializeField] private RectTransform panelRect;
        [SerializeField] private RectTransform dragHandle;
        [SerializeField] private RectTransform resizeHandle;
        [SerializeField] private GameObject contentRoot;
        [SerializeField] private TMP_Text minimizeLabel;
        [SerializeField] private Vector2 minSize = new Vector2(TwinRuntimeUIFactory.MinPanelWidth, TwinRuntimeUIFactory.MinPanelHeight);
        [SerializeField] private Vector2 maxSize = new Vector2(TwinRuntimeUIFactory.MaxPanelWidth, TwinRuntimeUIFactory.MaxPanelHeight);

        private bool _dragging;
        private bool _resizing;
        private bool _minimized;
        private Vector2 _pointerStart;
        private Vector2 _panelStartPosition;
        private Vector2 _panelStartSize;
        private Vector2 _expandedSize;

        public void Initialize(
            TwinRuntimeUIBindings uiBindings,
            RectTransform panel,
            RectTransform drag,
            RectTransform resize,
            GameObject content,
            Button minimizeButton,
            TMP_Text minimizeText,
            Vector2 minPanelSize,
            Vector2 maxPanelSize)
        {
            bindings = uiBindings;
            panelRect = panel;
            dragHandle = drag;
            resizeHandle = resize;
            contentRoot = content;
            minimizeLabel = minimizeText;
            minSize = minPanelSize;
            maxSize = maxPanelSize;
            _expandedSize = panelRect != null ? ClampSize(panelRect.sizeDelta) : minSize;
            ApplyLayout(_expandedSize);

            if (minimizeButton != null)
            {
                minimizeButton.onClick.RemoveListener(ToggleMinimized);
                minimizeButton.onClick.AddListener(ToggleMinimized);
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (panelRect == null)
            {
                return;
            }

            _resizing = !_minimized && IsPointerInside(resizeHandle, eventData);
            _dragging = !_resizing && (dragHandle == null || IsPointerInside(dragHandle, eventData));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                panelRect.parent as RectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out _pointerStart);
            _panelStartPosition = panelRect.anchoredPosition;
            _panelStartSize = panelRect.sizeDelta;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (panelRect == null || (!_dragging && !_resizing))
            {
                return;
            }

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                panelRect.parent as RectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 pointer);
            Vector2 delta = pointer - _pointerStart;

            if (_resizing)
            {
                Vector2 size = ClampSize(new Vector2(_panelStartSize.x + delta.x, _panelStartSize.y - delta.y));
                ApplyLayout(size);
                if (!_minimized)
                {
                    _expandedSize = size;
                }
                return;
            }

            if (_dragging)
            {
                panelRect.anchoredPosition = _panelStartPosition + delta;
            }
        }

        public void ToggleMinimized()
        {
            if (panelRect == null)
            {
                return;
            }

            _minimized = !_minimized;
            if (_minimized)
            {
                _expandedSize = ClampSize(panelRect.sizeDelta);
                panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, 56f);
                if (contentRoot != null) contentRoot.SetActive(false);
                if (resizeHandle != null) resizeHandle.gameObject.SetActive(false);
                minimizeLabel.SetIfChanged("+");
            }
            else
            {
                Vector2 expandedSize = _expandedSize.sqrMagnitude > 1f ? ClampSize(_expandedSize) : minSize;
                ApplyLayout(expandedSize);
                if (contentRoot != null) contentRoot.SetActive(true);
                if (resizeHandle != null) resizeHandle.gameObject.SetActive(true);
                minimizeLabel.SetIfChanged("-");
            }
        }

        private void ApplyLayout(Vector2 size)
        {
            if (bindings != null)
            {
                TwinRuntimeUIFactory.ApplyPanelLayout(bindings, size);
                return;
            }

            if (panelRect != null)
            {
                panelRect.sizeDelta = ClampSize(size);
            }
        }

        private Vector2 ClampSize(Vector2 size)
        {
            return new Vector2(
                Mathf.Clamp(size.x, minSize.x, maxSize.x),
                Mathf.Clamp(size.y, minSize.y, maxSize.y));
        }

        private static bool IsPointerInside(RectTransform target, PointerEventData eventData)
        {
            return target != null &&
                   RectTransformUtility.RectangleContainsScreenPoint(target, eventData.position, eventData.pressEventCamera);
        }
    }
}
