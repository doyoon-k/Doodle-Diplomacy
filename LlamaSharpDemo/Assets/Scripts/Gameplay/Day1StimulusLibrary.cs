using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Data;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class Day1StimulusLibrary : MonoBehaviour
    {
        private const string DefaultRelativeFolder = "first_contact/day1";
        private const string StimuliFileName = "day1_stimuli.json";
        private const string ProfilePayloadFileName = "day1_profile_payload.json";

        [Tooltip("Persistent-data subfolder where approved Day1 drawings and calibration manifests are written.")]
        [SerializeField] private string relativeFolder = DefaultRelativeFolder;

        private readonly List<Day1StimulusRecord> _approvedRecords = new();
        private string _rootPathOverride;

        public IReadOnlyList<Day1StimulusRecord> ApprovedRecords => _approvedRecords;
        public string RootPath => !string.IsNullOrWhiteSpace(_rootPathOverride)
            ? _rootPathOverride
            : Path.Combine(Application.persistentDataPath, NormalizeRelativeFolder(relativeFolder));
        public string StimuliManifestPath => Path.Combine(RootPath, StimuliFileName);
        public string ProfilePayloadPath => Path.Combine(RootPath, ProfilePayloadFileName);

        public void SetRootPathForTests(string rootPath)
        {
            _rootPathOverride = rootPath;
        }

        public void BeginSession(bool clearExisting = true)
        {
            _approvedRecords.Clear();
            if (clearExisting && Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, true);
            }

            Directory.CreateDirectory(RootPath);
            WriteStimuliManifest();
        }

        public Day1StimulusRecord SaveApprovedStimulus(
            int slot,
            string label,
            ReactionTier reactionTier,
            byte[] pngBytes)
        {
            if (pngBytes == null || pngBytes.Length == 0)
            {
                throw new ArgumentException("Approved Day1 stimulus requires PNG bytes.", nameof(pngBytes));
            }

            string normalizedLabel = Day1ReactionTierEvaluator.NormalizeLabel(label);
            if (string.IsNullOrWhiteSpace(normalizedLabel))
            {
                throw new ArgumentException("Approved Day1 stimulus requires a label.", nameof(label));
            }

            Directory.CreateDirectory(RootPath);

            string slug = BuildSlug(normalizedLabel);
            string fileName = $"stimulus_{Mathf.Max(1, slot):00}_{slug}.png";
            string imagePath = Path.Combine(RootPath, fileName);
            File.WriteAllBytes(imagePath, pngBytes);

            var record = new Day1StimulusRecord
            {
                slot = slot,
                label = normalizedLabel,
                imagePath = imagePath,
                stickerKey = BuildStickerKey(slot, normalizedLabel),
                reactionTier = reactionTier
            };

            _approvedRecords.Add(record);
            WriteStimuliManifest();
            return record;
        }

        public Day1ProfilePayload BuildProfilePayload()
        {
            var payload = new Day1ProfilePayload();
            foreach (Day1StimulusRecord record in _approvedRecords)
            {
                payload.stimuli.Add(new Day1ProfileStimulus
                {
                    slot = record.slot,
                    label = record.label,
                    reactionTier = record.reactionTier
                });
            }

            return payload;
        }

        public string WriteProfilePayload()
        {
            Directory.CreateDirectory(RootPath);
            string json = ToJson(BuildProfilePayload());
            File.WriteAllText(ProfilePayloadPath, json, Encoding.UTF8);
            return ProfilePayloadPath;
        }

        private void WriteStimuliManifest()
        {
            Directory.CreateDirectory(RootPath);
            string json = ToJson(new Day1StimulusManifest { stimuli = new List<Day1StimulusRecord>(_approvedRecords) });
            File.WriteAllText(StimuliManifestPath, json, Encoding.UTF8);
        }

        private static string BuildStickerKey(int slot, string label)
        {
            return $"day1_slot_{Mathf.Max(1, slot):00}_{BuildSlug(label)}";
        }

        private static string BuildSlug(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return "stimulus";
            }

            var builder = new StringBuilder(label.Length);
            bool lastWasSeparator = false;
            foreach (char c in label.ToLowerInvariant())
            {
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9')
                {
                    builder.Append(c);
                    lastWasSeparator = false;
                    continue;
                }

                if (!lastWasSeparator)
                {
                    builder.Append('_');
                    lastWasSeparator = true;
                }
            }

            string slug = builder.ToString().Trim('_');
            return string.IsNullOrWhiteSpace(slug) ? "stimulus" : slug;
        }

        private static string NormalizeRelativeFolder(string folder)
        {
            return string.IsNullOrWhiteSpace(folder)
                ? DefaultRelativeFolder
                : folder.Replace('\\', '/').Trim('/');
        }

        private static string ToJson<T>(T value)
        {
            return JsonSerializer.Serialize(value, new JsonSerializerOptions
            {
                IncludeFields = true,
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            });
        }

        [Serializable]
        private sealed class Day1StimulusManifest
        {
            public List<Day1StimulusRecord> stimuli = new();
        }
    }
}
