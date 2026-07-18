using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [CreateAssetMenu(
        fileName = "FirstContactModeConfig",
        menuName = "DoodleDiplomacy/First Contact/Mode Config")]
    public sealed class FirstContactModeConfig : ScriptableObject
    {
        [Tooltip("터미널 표시, 카드 공개, 분석 대기 등 UX 연출 시간 설정입니다.")]
        public FirstContactPresentationSettings presentationSettings;
        [Tooltip("임베딩, 군집, 파형 생성 설정입니다.")]
        public FirstContactSemanticSettings semanticSettings;
        [Tooltip("그림 라벨링 파이프라인과 라벨 거절 규칙 설정입니다.")]
        public FirstContactVlmSettings vlmSettings;
        [Tooltip("First Contact 모드의 디버그 로그와 터미널 디버그 표시 설정입니다.")]
        public FirstContactDebugSettings debugSettings;

        [Header("Bootstrap Categories")]
        [Tooltip("The ordered CATEGORY targets collected during First Contact bootstrap. Reorder this list to change the probe sequence.")]
        public List<FirstContactBootstrapCategoryDefinition> bootstrapCategories = new();

        public bool TryGetBootstrapCategories(
            out IReadOnlyList<FirstContactBootstrapCategoryDefinition> categories,
            out string error)
        {
            categories = Array.Empty<FirstContactBootstrapCategoryDefinition>();
            error = string.Empty;
            if (bootstrapCategories == null || bootstrapCategories.Count == 0)
            {
                error = "Configure at least one bootstrap CATEGORY.";
                return false;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < bootstrapCategories.Count; i++)
            {
                FirstContactBootstrapCategoryDefinition category = bootstrapCategories[i];
                if (category == null)
                {
                    error = $"Bootstrap CATEGORY {i + 1} is missing.";
                    return false;
                }

                if (!category.TryValidate(out string categoryError))
                {
                    error = $"Bootstrap CATEGORY {i + 1}: {categoryError}";
                    return false;
                }

                if (!ids.Add(category.Id))
                {
                    error = $"Bootstrap CATEGORY IDs must be unique. Duplicate ID: '{category.Id}'.";
                    return false;
                }
            }

            categories = bootstrapCategories;
            return true;
        }
    }

    [Serializable]
    public sealed class FirstContactBootstrapCategoryDefinition
    {
        [Tooltip("Stable lowercase ID used by the semantic map and generated terminal localization keys. Use lowercase letters, numbers, and underscores only.")]
        public string id;
        [Tooltip("English source/fallback shown after CATEGORY:. Its localization key is generated as first_contact.terminal.category.{id}.")]
        public string categoryDisplayName;
        [Tooltip("English source/fallback shown after MEANING: once this CATEGORY is stable. Its localization key is generated as first_contact.terminal.meaning.{id}.")]
        public string meaningDisplayName;
        [TextArea(2, 5)]
        [Tooltip("English source/fallback for embedding and CATEGORY-fit analysis. Its localization key is generated as first_contact.terminal.category.{id}.descriptor.")]
        public string descriptorText;
        [Tooltip("Optional label keywords used to name a stable semantic GROUP with this CATEGORY's MEANING on the map.")]
        public List<string> clusterLabelKeywords = new();
        [Min(0)]
        [Tooltip("Accepted TRACE count for this CATEGORY. Set to 0 to use Semantic Settings > Bootstrap Min Trace Count.")]
        public int requiredTraceCount;

        public string Id => id?.Trim() ?? string.Empty;
        public string CategoryDisplayName => categoryDisplayName?.Trim() ?? string.Empty;
        public string MeaningDisplayName => meaningDisplayName?.Trim() ?? string.Empty;
        public string DescriptorText => descriptorText?.Trim() ?? string.Empty;

        public bool MatchesClusterLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label) || clusterLabelKeywords == null)
            {
                return false;
            }

            for (int i = 0; i < clusterLabelKeywords.Count; i++)
            {
                string keyword = clusterLabelKeywords[i];
                if (!string.IsNullOrWhiteSpace(keyword) &&
                    label.IndexOf(keyword.Trim(), StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        public int ResolveRequiredTraceCount(int defaultTraceCount)
        {
            int traceCount = requiredTraceCount > 0 ? requiredTraceCount : defaultTraceCount;
            return Math.Max(2, traceCount);
        }

        public bool TryValidate(out string error)
        {
            error = string.Empty;
            if (!IsValidId(Id))
            {
                error = "ID is required and may contain only lowercase letters, numbers, and underscores.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(CategoryDisplayName))
            {
                error = "CATEGORY display name is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(MeaningDisplayName))
            {
                error = "MEANING display name is required.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(DescriptorText))
            {
                error = "Descriptor text is required for CATEGORY-fit analysis.";
                return false;
            }

            return true;
        }

        private static bool IsValidId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                bool isLowercaseLetter = character >= 'a' && character <= 'z';
                bool isNumber = character >= '0' && character <= '9';
                if (!isLowercaseLetter && !isNumber && character != '_')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
