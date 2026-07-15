using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactEmbeddingService
    {
        public const string SentenceSimilarityPrefix = "task: sentence similarity | query: ";

        private readonly IEmbeddingService _embeddingService;
        private readonly FirstContactSemanticSettings _settings;
        private readonly Dictionary<string, float[]> _cache = new(StringComparer.Ordinal);

        public FirstContactEmbeddingService(
            IEmbeddingService embeddingService,
            FirstContactSemanticSettings settings)
        {
            _embeddingService = embeddingService;
            _settings = settings;
        }

        public IEnumerator EmbedLabel(string label, Action<EmbeddingResult> onComplete)
        {
            IReadOnlyList<EmbeddingResult> results = null;
            yield return EmbedLabels(new[] { label }, value => results = value);
            onComplete?.Invoke(results != null && results.Count > 0
                ? results[0]
                : new EmbeddingResult(label, null, "Embedding failed."));
        }

        public IEnumerator EmbedLabels(
            IReadOnlyList<string> labels,
            Action<IReadOnlyList<EmbeddingResult>> onComplete)
        {
            var results = new EmbeddingResult[labels?.Count ?? 0];
            if (labels == null || labels.Count == 0)
            {
                onComplete?.Invoke(results);
                yield break;
            }

            if (_settings == null || _settings.embeddingProfile == null)
            {
                FillError(results, labels, "Embedding profile is missing.");
                onComplete?.Invoke(results);
                yield break;
            }

            if (_embeddingService == null)
            {
                FillError(results, labels, "Embedding service is missing.");
                onComplete?.Invoke(results);
                yield break;
            }

            var missingTexts = new List<string>();
            var missingKeys = new List<string>();
            var computedVectors = new Dictionary<string, float[]>(StringComparer.Ordinal);
            for (int i = 0; i < labels.Count; i++)
            {
                string normalized = NormalizeText(labels[i]);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    results[i] = new EmbeddingResult(labels[i], null, "Embedding input is empty.");
                    continue;
                }

                if (_settings.cacheEmbeddings && _cache.TryGetValue(normalized, out float[] cached))
                {
                    results[i] = new EmbeddingResult(normalized, cached);
                    continue;
                }

                if (!missingKeys.Contains(normalized))
                {
                    missingKeys.Add(normalized);
                    missingTexts.Add(BuildEmbeddingInput(normalized));
                }
            }

            int batchSize = Mathf.Max(1, _settings.maxBatchSize);
            for (int start = 0; start < missingTexts.Count; start += batchSize)
            {
                int count = Mathf.Min(batchSize, missingTexts.Count - start);
                string[] batch = new string[count];
                for (int i = 0; i < count; i++)
                {
                    batch[i] = missingTexts[start + i];
                }

                float[][] vectors = null;
                yield return _embeddingService.Embed(_settings.embeddingProfile, batch, value => vectors = value);
                for (int i = 0; i < count; i++)
                {
                    float[] vector = vectors != null && i < vectors.Length ? vectors[i] : null;
                    if (vector != null && vector.Length > 0)
                    {
                        if (_settings.normalizeVectorsInMemory)
                        {
                            NormalizeInPlace(vector);
                        }

                        computedVectors[missingKeys[start + i]] = vector;
                        if (_settings.cacheEmbeddings)
                        {
                            _cache[missingKeys[start + i]] = vector;
                        }
                    }
                }
            }

            for (int i = 0; i < labels.Count; i++)
            {
                if (results[i].IsValid || !string.IsNullOrWhiteSpace(results[i].Error))
                {
                    continue;
                }

                string normalized = NormalizeText(labels[i]);
                if (computedVectors.TryGetValue(normalized, out float[] computed))
                {
                    results[i] = new EmbeddingResult(normalized, computed);
                }
                else if (_settings.cacheEmbeddings && _cache.TryGetValue(normalized, out float[] cached))
                {
                    results[i] = new EmbeddingResult(normalized, cached);
                }
                else
                {
                    results[i] = new EmbeddingResult(normalized, null, $"Embedding failed for '{normalized}'.");
                }
            }

            onComplete?.Invoke(results);
        }

        public float Similarity(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length == 0 || a.Length != b.Length)
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                sum += a[i] * b[i];
            }

            return Mathf.Clamp(sum, -1f, 1f);
        }

        public bool TryBuildCentroid(IReadOnlyList<float[]> vectors, out float[] centroid)
        {
            centroid = null;
            if (vectors == null || vectors.Count == 0)
            {
                return false;
            }

            int dims = 0;
            for (int i = 0; i < vectors.Count; i++)
            {
                if (vectors[i] != null && vectors[i].Length > 0)
                {
                    dims = vectors[i].Length;
                    break;
                }
            }

            if (dims <= 0)
            {
                return false;
            }

            centroid = new float[dims];
            int used = 0;
            for (int i = 0; i < vectors.Count; i++)
            {
                float[] vector = vectors[i];
                if (vector == null || vector.Length != dims)
                {
                    continue;
                }

                used++;
                for (int d = 0; d < dims; d++)
                {
                    centroid[d] += vector[d];
                }
            }

            if (used <= 0)
            {
                centroid = null;
                return false;
            }

            float inv = 1f / used;
            for (int d = 0; d < dims; d++)
            {
                centroid[d] *= inv;
            }

            NormalizeInPlace(centroid);
            return true;
        }

        public static string NormalizeText(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            string trimmed = value.Trim().ToLowerInvariant();
            var chars = new char[trimmed.Length];
            int count = 0;
            bool previousWhitespace = false;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (char.IsWhiteSpace(c))
                {
                    if (!previousWhitespace)
                    {
                        chars[count++] = ' ';
                        previousWhitespace = true;
                    }

                    continue;
                }

                chars[count++] = c;
                previousWhitespace = false;
            }

            return new string(chars, 0, count).Trim();
        }

        public static string BuildEmbeddingInput(string value)
        {
            string normalized = NormalizeText(value);
            return string.IsNullOrWhiteSpace(normalized)
                ? string.Empty
                : SentenceSimilarityPrefix + normalized;
        }

        private static void FillError(EmbeddingResult[] results, IReadOnlyList<string> labels, string error)
        {
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = new EmbeddingResult(labels[i], null, error);
            }
        }

        private static void NormalizeInPlace(float[] vector)
        {
            if (vector == null || vector.Length == 0)
            {
                return;
            }

            double sum = 0d;
            for (int i = 0; i < vector.Length; i++)
            {
                sum += vector[i] * vector[i];
            }

            if (sum <= double.Epsilon)
            {
                return;
            }

            float inv = (float)(1d / Math.Sqrt(sum));
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] *= inv;
            }
        }
    }
}
