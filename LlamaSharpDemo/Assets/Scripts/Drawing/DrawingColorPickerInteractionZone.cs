using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Forwards pointer input from a UI rect to the drawing color picker.
/// </summary>
public class DrawingColorPickerInteractionZone : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    public enum ZoneKind
    {
        ColorField,
        ValueSlider
    }

    [SerializeField] private DrawingColorPickerController picker;
    [SerializeField] private ZoneKind zoneKind;

    public void Configure(DrawingColorPickerController targetPicker, ZoneKind targetZoneKind)
    {
        picker = targetPicker;
        zoneKind = targetZoneKind;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        Forward(eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        Forward(eventData);
    }

    private void Forward(PointerEventData eventData)
    {
        if (picker == null)
        {
            picker = GetComponentInParent<DrawingColorPickerController>();
            if (picker == null)
            {
                return;
            }
        }

        var rectTransform = transform as RectTransform;
        if (rectTransform == null)
        {
            return;
        }

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform,
                eventData.position,
                eventData.pressEventCamera,
                out Vector2 localPoint))
        {
            return;
        }

        Rect rect = rectTransform.rect;
        float normalizedX = Mathf.InverseLerp(rect.xMin, rect.xMax, localPoint.x);
        float normalizedY = Mathf.InverseLerp(rect.yMin, rect.yMax, localPoint.y);

        switch (zoneKind)
        {
            case ZoneKind.ColorField:
                picker.SetHueAndSaturation(normalizedX, normalizedY);
                break;
            case ZoneKind.ValueSlider:
                picker.SetValue(normalizedY);
                break;
        }
    }
}
