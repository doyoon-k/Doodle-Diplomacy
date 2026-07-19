using System;
using System.Collections.Generic;
using DoodleDiplomacy.Localization;
using UnityEngine;

namespace DoodleDiplomacy.Narrative
{
    [CreateAssetMenu(
        fileName = "NarrativeScenario",
        menuName = "DoodleDiplomacy/Narrative/Scenario")]
    public sealed class NarrativeScenarioAsset : ScriptableObject
    {
        [SerializeField] private int schemaVersion = 1;
        [SerializeField] private string scenarioId = string.Empty;
        [SerializeField] private string title = string.Empty;
        [SerializeField] private string sourceLocale = "en-US";
        [SerializeField] private List<string> locales = new();
        [SerializeField] private List<NarrativeSection> sections = new();
        [SerializeField] private List<NarrativeBeat> beats = new();
        [SerializeField] private List<NarrativeLocalizationEntry> localizationEntries = new();

        public int SchemaVersion => schemaVersion;
        public string ScenarioId => scenarioId;
        public string Title => title;
        public string SourceLocale => sourceLocale;
        public IReadOnlyList<string> Locales => locales;
        public IReadOnlyList<NarrativeSection> Sections => sections;
        public IReadOnlyList<NarrativeBeat> Beats => beats;
        public IReadOnlyList<NarrativeLocalizationEntry> LocalizationEntries => localizationEntries;

        public void ApplyDocument(NarrativeScenarioDocument document)
        {
            if (document == null)
            {
                return;
            }

            schemaVersion = document.schemaVersion;
            scenarioId = document.scenarioId ?? string.Empty;
            title = document.title ?? string.Empty;
            sourceLocale = string.IsNullOrWhiteSpace(document.sourceLocale)
                ? "en-US"
                : document.sourceLocale;
            locales = document.locales ?? new List<string>();
            sections = document.sections ?? new List<NarrativeSection>();
            beats = document.beats ?? new List<NarrativeBeat>();
            localizationEntries = document.localizationEntries ?? new List<NarrativeLocalizationEntry>();
        }

        public bool TryGetBeat(string beatId, out NarrativeBeat beat)
        {
            beat = null;
            if (string.IsNullOrWhiteSpace(beatId) || beats == null)
            {
                return false;
            }

            for (int i = 0; i < beats.Count; i++)
            {
                NarrativeBeat candidate = beats[i];
                if (candidate != null &&
                    string.Equals(candidate.id, beatId, StringComparison.OrdinalIgnoreCase))
                {
                    beat = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool TryGetBeatByRuntimeCue(string runtimeCue, out NarrativeBeat beat)
        {
            beat = null;
            if (string.IsNullOrWhiteSpace(runtimeCue) || beats == null)
            {
                return false;
            }

            for (int i = 0; i < beats.Count; i++)
            {
                NarrativeBeat candidate = beats[i];
                if (candidate != null && candidate.enabled &&
                    string.Equals(candidate.runtimeCue, runtimeCue, StringComparison.OrdinalIgnoreCase))
                {
                    beat = candidate;
                    return true;
                }
            }

            return false;
        }
    }

    [Serializable]
    public sealed class NarrativeSection
    {
        public string id = string.Empty;
        public string title = string.Empty;
        public int order;
        [TextArea(1, 4)] public string summary = string.Empty;
    }

    [Serializable]
    public sealed class NarrativeBeat
    {
        public string id = string.Empty;
        public string sectionId = string.Empty;
        public int order;
        public bool enabled = true;
        public string type = "dialogue";
        public string status = "draft";
        public string runtimeCue = string.Empty;
        public string triggerEvent = string.Empty;
        public string condition = string.Empty;
        public string repeat = "once";
        public string speakerId = string.Empty;
        public string speakerLocalizationKey = string.Empty;
        public string speakerFallback = string.Empty;
        public string localizationKey = string.Empty;
        [TextArea(2, 8)] public string sourceText = string.Empty;
        public string advance = "player";
        [Min(0f)] public float minimumSeconds = 0.3f;
        [TextArea(1, 4)] public string situation = string.Empty;
        [TextArea(1, 4)] public string beforeAction = string.Empty;
        [TextArea(1, 4)] public string afterAction = string.Empty;
        [TextArea(1, 4)] public string stageDirection = string.Empty;
        public List<string> tags = new();
        public List<NarrativeLocalizedText> localizedTexts = new();

        public bool WaitForAdvance =>
            !string.Equals(advance, "automatic", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(advance, "auto", StringComparison.OrdinalIgnoreCase);

        public string ResolveSpeaker(params L10nArg[] args)
        {
            return string.IsNullOrWhiteSpace(speakerLocalizationKey)
                ? speakerFallback ?? string.Empty
                : L10n.T(speakerLocalizationKey, speakerFallback ?? string.Empty, args);
        }

        public string ResolveText(params L10nArg[] args)
        {
            return string.IsNullOrWhiteSpace(localizationKey)
                ? FormatFallback(sourceText, args)
                : L10n.T(localizationKey, sourceText ?? string.Empty, args);
        }

        private static string FormatFallback(string template, IReadOnlyList<L10nArg> args)
        {
            string result = template ?? string.Empty;
            if (args == null)
            {
                return result;
            }

            for (int i = 0; i < args.Count; i++)
            {
                L10nArg arg = args[i];
                if (!string.IsNullOrWhiteSpace(arg.Key))
                {
                    result = result.Replace("{" + arg.Key + "}", arg.Value);
                }
            }

            return result;
        }
    }

    [Serializable]
    public sealed class NarrativeLocalizedText
    {
        public string locale = string.Empty;
        [TextArea(2, 8)] public string text = string.Empty;
        public string status = "draft";
    }

    [Serializable]
    public sealed class NarrativeLocalizationEntry
    {
        public string key = string.Empty;
        [TextArea(1, 8)] public string sourceText = string.Empty;
        public string group = "narrative";
        public string beatId = string.Empty;
        public List<NarrativeLocalizedText> localizedTexts = new();
    }

    [Serializable]
    public sealed class NarrativeScenarioDocument
    {
        public string documentType = "doodle-diplomacy-narrative";
        public int schemaVersion = 1;
        public string scenarioId = string.Empty;
        public string title = string.Empty;
        public string sourceLocale = "en-US";
        public List<string> locales = new();
        public List<NarrativeSection> sections = new();
        public List<NarrativeBeat> beats = new();
        public List<NarrativeLocalizationEntry> localizationEntries = new();
    }

    public static class NarrativeScenarioJson
    {
        public static NarrativeScenarioDocument Parse(string json)
        {
            return string.IsNullOrWhiteSpace(json)
                ? null
                : JsonUtility.FromJson<NarrativeScenarioDocument>(json);
        }
    }
}
