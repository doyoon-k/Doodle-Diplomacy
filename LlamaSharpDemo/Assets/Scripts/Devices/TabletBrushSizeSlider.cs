using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DoodleDiplomacy.Devices
{
    [DisallowMultipleComponent]
    public sealed class TabletBrushSizeSlider : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        [System.Serializable]
        public sealed class ValueChangedEvent : UnityEvent<float> { }

        [SerializeField] private RectTransform fillRect;
        [SerializeField] private RectTransform handleRect;
        [SerializeField] private RectTransform interactionRect;
        [SerializeField] private float minValue = 1f;
        [SerializeField] private float maxValue = 24f;
        [SerializeField] private bool wholeNumbers = true;
        [SerializeField] private float value = 6f;
        [SerializeField] private ValueChangedEvent onValueChanged = new();

        public ValueChangedEvent OnValueChanged => onValueChanged;

        private bool _layoutRangesCaptured;
        private float _fillAnchorMinX;
        private float _fillAnchorMaxX = 1f;
        private float _handleAnchorMinX;
        private float _handleAnchorMaxX = 1f;

        private void Awake()
        {
            RefreshReferences();
            DisableLegacySlider();
            ApplyVisuals();
        }

        private void OnEnable()
        {
            RefreshReferences();
            DisableLegacySlider();
            ApplyVisuals();
        }

        public void Configure(RectTransform targetFillRect, RectTransform targetHandleRect, float targetMinValue, float targetMaxValue, bool useWholeNumbers)
        {
            fillRect = targetFillRect;
            handleRect = targetHandleRect;
            interactionRect = handleRect != null ? handleRect.parent as RectTransform : null;
            minValue = targetMinValue;
            maxValue = targetMaxValue;
            wholeNumbers = useWholeNumbers;
            _layoutRangesCaptured = false;
            RefreshReferences();
            DisableLegacySlider();
            ApplyVisuals();
        }

        public void SetValueWithoutNotify(float newValue)
        {
            SetValueInternal(newValue, false);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            UpdateFromPointer(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            UpdateFromPointer(eventData);
        }

        private void UpdateFromPointer(PointerEventData eventData)
        {
            RefreshReferences();
            RectTransform targetRect = interactionRect != null ? interactionRect : transform as RectTransform;
            if (targetRect == null)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    targetRect,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 localPoint))
            {
                return;
            }

            Rect rect = targetRect.rect;
            float normalized = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
            float newValue = Mathf.Lerp(minValue, maxValue, normalized);
            SetValueInternal(newValue, true);
        }

        private void SetValueInternal(float newValue, bool notify)
        {
            float clamped = Mathf.Clamp(newValue, minValue, maxValue);
            if (wholeNumbers)
            {
                clamped = Mathf.Round(clamped);
            }

            if (Mathf.Approximately(value, clamped))
            {
                value = clamped;
                ApplyVisuals();
                return;
            }

            value = clamped;
            ApplyVisuals();
            if (notify)
            {
                onValueChanged?.Invoke(value);
            }
        }

        private void ApplyVisuals()
        {
            RefreshReferences();
            CaptureLayoutRanges();
            float normalized = Mathf.Approximately(minValue, maxValue)
                ? 0f
                : Mathf.InverseLerp(minValue, maxValue, value);

            if (fillRect != null)
            {
                Vector2 fillAnchorMin = fillRect.anchorMin;
                Vector2 fillAnchorMax = fillRect.anchorMax;
                fillAnchorMin.x = _fillAnchorMinX;
                fillAnchorMax.x = Mathf.Lerp(_fillAnchorMinX, _fillAnchorMaxX, normalized);
                fillRect.anchorMin = fillAnchorMin;
                fillRect.anchorMax = fillAnchorMax;
            }

            if (handleRect != null)
            {
                float handleAnchorX = Mathf.Lerp(_handleAnchorMinX, _handleAnchorMaxX, normalized);
                Vector2 handleAnchorMin = handleRect.anchorMin;
                Vector2 handleAnchorMax = handleRect.anchorMax;
                handleAnchorMin.x = handleAnchorX;
                handleAnchorMax.x = handleAnchorX;
                handleRect.anchorMin = handleAnchorMin;
                handleRect.anchorMax = handleAnchorMax;
            }
        }

        private void RefreshReferences()
        {
            if (fillRect == null)
            {
                fillRect = transform.Find("Fill Area/Fill") as RectTransform;
            }

            if (handleRect == null)
            {
                handleRect = transform.Find("Handle Slide Area/Handle") as RectTransform;
            }

            if (interactionRect == null)
            {
                interactionRect = handleRect != null ? handleRect.parent as RectTransform : transform as RectTransform;
            }
        }

        private void CaptureLayoutRanges()
        {
            if (_layoutRangesCaptured)
            {
                return;
            }

            if (fillRect != null)
            {
                _fillAnchorMinX = Mathf.Min(fillRect.anchorMin.x, fillRect.anchorMax.x);
                _fillAnchorMaxX = Mathf.Max(fillRect.anchorMin.x, fillRect.anchorMax.x);
            }

            if (handleRect != null)
            {
                _handleAnchorMinX = Mathf.Min(handleRect.anchorMin.x, handleRect.anchorMax.x);
                _handleAnchorMaxX = Mathf.Max(handleRect.anchorMin.x, handleRect.anchorMax.x);
            }

            if (Mathf.Approximately(_fillAnchorMinX, _fillAnchorMaxX))
            {
                _fillAnchorMinX = 0f;
                _fillAnchorMaxX = 1f;
            }

            if (Mathf.Approximately(_handleAnchorMinX, _handleAnchorMaxX))
            {
                _handleAnchorMinX = 0f;
                _handleAnchorMaxX = 1f;
            }

            _layoutRangesCaptured = true;
        }

        private void DisableLegacySlider()
        {
            Slider legacySlider = GetComponent<Slider>();
            if (legacySlider != null)
            {
                legacySlider.enabled = false;
            }
        }
    }
}
