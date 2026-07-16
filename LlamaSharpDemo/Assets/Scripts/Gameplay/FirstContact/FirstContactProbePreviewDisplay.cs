using System;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactProbePreviewDisplay : MonoBehaviour
    {
        [Serializable]
        private sealed class PreviewSlot
        {
            public RectTransform root;
            public RawImage image;
            public AspectRatioFitter aspectRatioFitter;
            public FirstContactProbePreviewScanline scanline;

            public bool IsConfigured => root != null && image != null;
        }

        [Header("Editable Layouts")]
        [Tooltip("Large preview layout used while reviewing a captured drawing.")]
        [SerializeField] private PreviewSlot review = new();
        [Tooltip("Compact preview layout used while dispatching the drawing beside terminal text.")]
        [SerializeField] private PreviewSlot dispatch = new();

        private void Awake()
        {
            Clear();
        }

        private void OnValidate()
        {
            ValidateSlot(review);
            ValidateSlot(dispatch);
        }

        public void ConfigureReview(
            RectTransform root,
            RawImage image,
            AspectRatioFitter aspectRatioFitter,
            FirstContactProbePreviewScanline scanline)
        {
            review.root = root;
            review.image = image;
            review.aspectRatioFitter = aspectRatioFitter;
            review.scanline = scanline;
            ValidateSlot(review);
        }

        public void ConfigureDispatch(
            RectTransform root,
            RawImage image,
            AspectRatioFitter aspectRatioFitter,
            FirstContactProbePreviewScanline scanline)
        {
            dispatch.root = root;
            dispatch.image = image;
            dispatch.aspectRatioFitter = aspectRatioFitter;
            dispatch.scanline = scanline;
            ValidateSlot(dispatch);
        }

        public bool Show(Texture texture, bool useDispatchLayout, bool scanActive)
        {
            PreviewSlot active = useDispatchLayout ? dispatch : review;
            PreviewSlot inactive = useDispatchLayout ? review : dispatch;
            HideSlot(inactive, clearTexture: true);

            if (texture == null || active == null || !active.IsConfigured)
            {
                HideSlot(active, clearTexture: true);
                return false;
            }

            active.image.texture = texture;
            active.image.color = Color.white;
            if (active.aspectRatioFitter != null)
            {
                active.aspectRatioFitter.aspectRatio =
                    Mathf.Max(0.01f, texture.width / (float)Mathf.Max(1, texture.height));
            }

            active.root.gameObject.SetActive(true);
            active.scanline?.SetScanning(scanActive);
            return true;
        }

        public void Clear()
        {
            HideSlot(review, clearTexture: true);
            HideSlot(dispatch, clearTexture: true);
        }

        public static FirstContactProbePreviewDisplay CreateRuntime(
            RectTransform parent,
            FirstContactPresentationSettings settings)
        {
            if (parent == null)
            {
                return null;
            }

            var host = new GameObject(
                nameof(FirstContactProbePreviewDisplay),
                typeof(FirstContactProbePreviewDisplay));
            host.transform.SetParent(parent, false);
            FirstContactProbePreviewDisplay display =
                host.GetComponent<FirstContactProbePreviewDisplay>();

            Vector2 reviewMin = settings != null
                ? settings.probeReviewAnchorMin
                : new Vector2(0.12f, 0.5f);
            Vector2 reviewMax = settings != null
                ? settings.probeReviewAnchorMax
                : new Vector2(0.88f, 0.93f);
            Vector2 dispatchMin = settings != null
                ? settings.probeDispatchAnchorMin
                : new Vector2(0.54f, 0.36f);
            Vector2 dispatchMax = settings != null
                ? settings.probeDispatchAnchorMax
                : new Vector2(0.93f, 0.76f);

            PreviewSlot runtimeReview = CreateRuntimeSlot(host.transform, "ProbePreview_Review", reviewMin, reviewMax);
            PreviewSlot runtimeDispatch =
                CreateRuntimeSlot(host.transform, "ProbePreview_Dispatch", dispatchMin, dispatchMax);
            display.review = runtimeReview;
            display.dispatch = runtimeDispatch;
            display.Clear();
            return display;
        }

        private static PreviewSlot CreateRuntimeSlot(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax)
        {
            GameObject rootObject = new(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(FirstContactProbePreviewScanline));
            rootObject.transform.SetParent(parent, false);
            RectTransform rootRect = rootObject.GetComponent<RectTransform>();
            rootRect.anchorMin = anchorMin;
            rootRect.anchorMax = anchorMax;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            Image background = rootObject.GetComponent<Image>();
            background.color = new Color(0.01f, 0.015f, 0.012f, 0.88f);
            background.raycastTarget = false;

            GameObject imageObject = new(
                "Image",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage),
                typeof(AspectRatioFitter));
            imageObject.transform.SetParent(rootObject.transform, false);
            RectTransform imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.anchorMin = new Vector2(0.04f, 0.06f);
            imageRect.anchorMax = new Vector2(0.96f, 0.94f);
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;

            RawImage image = imageObject.GetComponent<RawImage>();
            image.color = Color.white;
            image.raycastTarget = false;
            AspectRatioFitter aspect = imageObject.GetComponent<AspectRatioFitter>();
            aspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            aspect.aspectRatio = 1f;

            GameObject scanObject = new(
                "Scanline",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            scanObject.transform.SetParent(rootObject.transform, false);
            RectTransform scanRect = scanObject.GetComponent<RectTransform>();
            scanRect.anchorMin = new Vector2(0.04f, 0.5f);
            scanRect.anchorMax = new Vector2(0.96f, 0.5f);
            scanRect.offsetMin = new Vector2(0f, -1.5f);
            scanRect.offsetMax = new Vector2(0f, 1.5f);
            Image scanImage = scanObject.GetComponent<Image>();
            scanImage.color = new Color(0.35f, 1f, 0.5f, 0.58f);
            scanImage.raycastTarget = false;

            FirstContactProbePreviewScanline scanline =
                rootObject.GetComponent<FirstContactProbePreviewScanline>();
            scanline.Configure(scanRect);

            return new PreviewSlot
            {
                root = rootRect,
                image = image,
                aspectRatioFitter = aspect,
                scanline = scanline
            };
        }

        private static void ValidateSlot(PreviewSlot slot)
        {
            if (slot?.image != null)
            {
                slot.image.raycastTarget = false;
            }
        }

        private static void HideSlot(PreviewSlot slot, bool clearTexture)
        {
            if (slot == null)
            {
                return;
            }

            slot.scanline?.SetScanning(false);
            if (clearTexture && slot.image != null)
            {
                slot.image.texture = null;
            }

            if (slot.root != null)
            {
                slot.root.gameObject.SetActive(false);
            }
        }
    }
}
