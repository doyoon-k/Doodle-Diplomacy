using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public readonly struct FirstContactBootstrapProbeFit
    {
        public readonly float CategoryDescriptorFit;
        public readonly bool HasUsableSignal;
        public readonly bool HasCategoryDescriptor;

        public FirstContactBootstrapProbeFit(
            float categoryDescriptorFit,
            bool hasUsableSignal,
            bool hasCategoryDescriptor)
        {
            CategoryDescriptorFit = categoryDescriptorFit;
            HasUsableSignal = hasUsableSignal;
            HasCategoryDescriptor = hasCategoryDescriptor;
        }
    }

    public sealed class FirstContactBootstrapSession
    {
        private readonly List<FirstContactBootstrapCategoryState> _categories = new();
        private int _activeCategoryIndex;

        public FirstContactBootstrapSession(
            IReadOnlyList<FirstContactBootstrapCategoryDefinition> definitions,
            int defaultRequiredTraceCount)
        {
            if (definitions == null || definitions.Count == 0)
            {
                throw new ArgumentException(
                    "At least one bootstrap CATEGORY definition is required.",
                    nameof(definitions));
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                FirstContactBootstrapCategoryDefinition definition = definitions[i];
                if (definition == null)
                {
                    throw new ArgumentException(
                        $"Bootstrap CATEGORY definition at index {i} is missing.",
                        nameof(definitions));
                }

                _categories.Add(new FirstContactBootstrapCategoryState(
                    definition,
                    defaultRequiredTraceCount));
            }
        }

        public IReadOnlyList<FirstContactBootstrapCategoryState> Categories => _categories;

        public FirstContactBootstrapCategoryState ActiveCategory =>
            _activeCategoryIndex >= 0 && _activeCategoryIndex < _categories.Count
                ? _categories[_activeCategoryIndex]
                : null;

        public bool IsComplete => ActiveCategory == null;

        public void AdvanceCategory()
        {
            if (!IsComplete)
            {
                _activeCategoryIndex++;
            }
        }
    }

    public sealed class FirstContactBootstrapCategoryState
    {
        private readonly float[] _emptyEmbedding = Array.Empty<float>();
        private float[] _descriptorEmbedding;

        public FirstContactBootstrapCategoryState(
            FirstContactBootstrapCategoryDefinition definition,
            int defaultRequiredTraceCount)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            Id = definition.Id;
            DisplayName = definition.CategoryDisplayName.ToUpperInvariant();
            Meaning = definition.MeaningDisplayName.ToUpperInvariant();
            DescriptorText = definition.DescriptorText;
            RequiredTraceCount = definition.ResolveRequiredTraceCount(defaultRequiredTraceCount);
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Meaning { get; }
        public string DescriptorText { get; }
        public string LocalizedDisplayName => FirstContactTerminalLocalization
            .LocalizeBootstrapCategory(Id, DisplayName);
        public string LocalizedMeaning => FirstContactTerminalLocalization
            .LocalizeMeaning(Id, Meaning);
        public string LocalizedDescriptorText => FirstContactTerminalLocalization
            .LocalizeCategoryDescriptor(Id, DescriptorText);
        public int RequiredTraceCount { get; }
        public List<SemanticCardRecord> Cards { get; } = new();
        public List<SemanticCardRecord> DetachedCards { get; } = new();
        public bool IsStable { get; private set; }
        public int TraceCount => Cards.Count;
        public bool HasDescriptorEmbedding =>
            _descriptorEmbedding != null && _descriptorEmbedding.Length > 0;

        public void SetDescriptorEmbedding(float[] embedding)
        {
            _descriptorEmbedding = embedding ?? _emptyEmbedding;
        }

        public bool TryFindRecordedCardByLabel(string label, out SemanticCardRecord card)
        {
            card = null;
            string normalized = NormalizeCardLabel(label);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return TryFindRecordedCardByLabel(Cards, normalized, out card) ||
                   TryFindRecordedCardByLabel(DetachedCards, normalized, out card);
        }

        public bool TryBuildCentroid(
            FirstContactEmbeddingService embeddingService,
            out float[] centroid)
        {
            centroid = null;
            if (embeddingService == null)
            {
                return false;
            }

            var vectors = new List<float[]>();
            for (int i = 0; i < Cards.Count; i++)
            {
                float[] embedding = Cards[i]?.Embedding;
                if (embedding != null && embedding.Length > 0)
                {
                    vectors.Add(embedding);
                }
            }

            return embeddingService.TryBuildCentroid(vectors, out centroid);
        }

        public FirstContactBootstrapProbeFit EvaluateCandidate(
            SemanticCardRecord card,
            FirstContactEmbeddingService embeddingService)
        {
            bool hasUsableSignal = card?.Embedding != null && card.Embedding.Length > 0;
            if (!hasUsableSignal)
            {
                return new FirstContactBootstrapProbeFit(0f, false, HasDescriptorEmbedding);
            }

            float categoryFit = 1f;
            bool hasCategoryDescriptor = false;
            if (embeddingService != null && HasDescriptorEmbedding)
            {
                categoryFit = embeddingService.Similarity(card.Embedding, _descriptorEmbedding);
                hasCategoryDescriptor = true;
            }

            return new FirstContactBootstrapProbeFit(
                categoryFit,
                true,
                hasCategoryDescriptor);
        }

        public bool RecordProbe(
            SemanticCardRecord card,
            FirstContactBootstrapProbeFit fit,
            bool categoryAccepted)
        {
            bool accepted = fit.HasUsableSignal && categoryAccepted;
            if (accepted)
            {
                Cards.Add(card);
            }
            else
            {
                DetachedCards.Add(card);
            }

            if (card != null)
            {
                card.BootstrapCategoryEvaluated = true;
                card.BootstrapCategoryAccepted = accepted;
            }

            IsStable = TraceCount >= Mathf.Max(2, RequiredTraceCount);
            return accepted;
        }

        private static bool TryFindRecordedCardByLabel(
            IReadOnlyList<SemanticCardRecord> cards,
            string normalizedLabel,
            out SemanticCardRecord card)
        {
            card = null;
            if (cards == null || string.IsNullOrWhiteSpace(normalizedLabel))
            {
                return false;
            }

            for (int i = 0; i < cards.Count; i++)
            {
                SemanticCardRecord candidate = cards[i];
                if (candidate != null &&
                    string.Equals(
                        ResolveNormalizedLabel(candidate),
                        normalizedLabel,
                        StringComparison.Ordinal))
                {
                    card = candidate;
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeCardLabel(string label)
        {
            return string.IsNullOrWhiteSpace(label)
                ? string.Empty
                : label.Trim().ToLowerInvariant();
        }

        private static string ResolveNormalizedLabel(SemanticCardRecord card)
        {
            return !string.IsNullOrWhiteSpace(card?.NormalizedLabel)
                ? FirstContactEmbeddingService.NormalizeText(card.NormalizedLabel)
                : NormalizeCardLabel(card?.OriginalLabel);
        }
    }
}
