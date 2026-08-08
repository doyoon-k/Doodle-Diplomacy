using System;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactBriefingProjector : MonoBehaviour
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        [SerializeField] private Renderer slideRenderer;
        [SerializeField] private FirstContactBriefingSlideDeck slideDeck;
        [SerializeField] private Color poweredColor = Color.white;
        [SerializeField] private Color poweredOffColor = new(0.025f, 0.025f, 0.025f, 1f);

        private MaterialPropertyBlock _propertyBlock;
        private FirstContactBriefingSlideId? _currentSlide;
        private bool _isPowered;

        public FirstContactBriefingSlideDeck SlideDeck => slideDeck;
        public FirstContactBriefingSlideId? CurrentSlide => _currentSlide;
        public bool IsPowered => _isPowered;

        private void Awake()
        {
            PowerOff();
        }

        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                PowerOff();
            }
        }

        public bool ShowSlide(FirstContactBriefingSlideId slideId)
        {
            if (slideRenderer == null || slideDeck == null)
            {
                return false;
            }

            Texture2D texture = slideDeck.GetSlide(slideId);
            if (texture == null)
            {
                Debug.LogWarning(
                    $"[BriefingProjector] Missing artwork for {slideId}.",
                    this);
                PowerOff();
                return false;
            }

            if (_isPowered && _currentSlide == slideId)
            {
                return true;
            }

            EnsurePropertyBlock();
            slideRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetTexture(BaseMapId, texture);
            _propertyBlock.SetTexture(MainTextureId, texture);
            _propertyBlock.SetColor(BaseColorId, poweredColor);
            _propertyBlock.SetColor(ColorId, poweredColor);
            slideRenderer.SetPropertyBlock(_propertyBlock);
            slideRenderer.enabled = true;
            _currentSlide = slideId;
            _isPowered = true;
            return true;
        }

        public void PowerOff()
        {
            if (slideRenderer == null)
            {
                _currentSlide = null;
                _isPowered = false;
                return;
            }

            EnsurePropertyBlock();
            slideRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetTexture(BaseMapId, Texture2D.blackTexture);
            _propertyBlock.SetTexture(MainTextureId, Texture2D.blackTexture);
            _propertyBlock.SetColor(BaseColorId, poweredOffColor);
            _propertyBlock.SetColor(ColorId, poweredOffColor);
            slideRenderer.SetPropertyBlock(_propertyBlock);
            slideRenderer.enabled = false;
            _currentSlide = null;
            _isPowered = false;
        }

        public static bool TryResolveSlide(
            string runtimeCue,
            out FirstContactBriefingSlideId slideId)
        {
            slideId = FirstContactBriefingSlideId.Technical;
            if (string.IsNullOrWhiteSpace(runtimeCue))
            {
                return false;
            }

            switch (runtimeCue)
            {
                case "BriefingProjectorTechnical":
                case "BriefingSlideTechnical":
                    slideId = FirstContactBriefingSlideId.Technical;
                    return true;
                case "BriefingSlideObjectSignal":
                    slideId = FirstContactBriefingSlideId.ObjectSignal;
                    return true;
                case "BriefingSlideBanana":
                    slideId = FirstContactBriefingSlideId.Banana;
                    return true;
                case "BriefingSlideDatabase":
                    slideId = FirstContactBriefingSlideId.Database;
                    return true;
                case "BriefingSlidePresidentTask":
                    slideId = FirstContactBriefingSlideId.PresidentTask;
                    return true;
                case "BriefingSlideFoodCluster":
                    slideId = FirstContactBriefingSlideId.FoodCluster;
                    return true;
                case "BriefingSlideUfoEvidence":
                    slideId = FirstContactBriefingSlideId.UfoEvidence;
                    return true;
                case "BriefingSlideCategories":
                    slideId = FirstContactBriefingSlideId.Categories;
                    return true;
                default:
                    return false;
            }
        }

        private void EnsurePropertyBlock()
        {
            _propertyBlock ??= new MaterialPropertyBlock();
        }
    }
}
