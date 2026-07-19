#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact.Editor
{
    public sealed class FirstContactMultilingualEmbeddingBenchmarkWindow : EditorWindow
    {
        private enum Expectation
        {
            Same,
            Different,
            Diagnostic
        }

        private readonly struct BenchmarkCase
        {
            public BenchmarkCase(string group, string left, string right, Expectation expectation)
            {
                Group = group;
                Left = left;
                Right = right;
                Expected = expectation;
            }

            public string Group { get; }
            public string Left { get; }
            public string Right { get; }
            public Expectation Expected { get; }
        }

        private readonly struct BenchmarkResult
        {
            public BenchmarkResult(BenchmarkCase testCase, float similarity, string error)
            {
                TestCase = testCase;
                Similarity = similarity;
                Error = error ?? string.Empty;
            }

            public BenchmarkCase TestCase { get; }
            public float Similarity { get; }
            public string Error { get; }
            public bool IsValid => string.IsNullOrWhiteSpace(Error);
        }

        private static readonly BenchmarkCase[] DefaultCases =
        {
            new("apple translations", "apple", "사과", Expectation.Same),
            new("apple translations", "apple", "りんご", Expectation.Same),
            new("apple translations", "apple", "苹果", Expectation.Same),
            new("apple translations", "apple", "manzana", Expectation.Same),
            new("apple translations", "apple", "pomme", Expectation.Same),
            new("apple translations", "apple", "Apfel", Expectation.Same),
            new("apple translations", "apple", "تفاحة", Expectation.Same),
            new("object translations", "knife", "칼", Expectation.Same),
            new("object translations", "knife", "ナイフ", Expectation.Same),
            new("object translations", "shield", "방패", Expectation.Same),
            new("object translations", "shield", "盾", Expectation.Same),
            new("object translations", "bread", "빵", Expectation.Same),
            new("object translations", "bread", "パン", Expectation.Same),
            new("near concepts", "apple", "pear", Expectation.Different),
            new("near concepts", "사과", "배", Expectation.Different),
            new("near concepts", "knife", "sword", Expectation.Different),
            new("near concepts", "칼", "검", Expectation.Different),
            new("near concepts", "bread", "cake", Expectation.Different),
            new("same category", "apple", "bread", Expectation.Different),
            new("same category", "knife", "hammer", Expectation.Different),
            new("same category", "shield", "armor", Expectation.Different),
            new("unrelated", "apple", "shield", Expectation.Different),
            new("unrelated", "bread", "knife", Expectation.Different),
            new("polysemy diagnostic", "배", "pear", Expectation.Diagnostic),
            new("polysemy diagnostic", "배", "ship", Expectation.Diagnostic),
            new("polysemy diagnostic", "bat", "박쥐", Expectation.Diagnostic),
            new("polysemy diagnostic", "bat", "baseball bat", Expectation.Diagnostic)
        };

        private readonly List<BenchmarkResult> _results = new();
        private FirstContactSemanticSettings _settings;
        private RoutingEditorLlmService _service;
        private Vector2 _scroll;
        private bool _running;
        private string _status = "Ready.";
        private float _suggestedThreshold;

        [MenuItem("Tools/First Contact/Multilingual Embedding Benchmark")]
        private static void Open()
        {
            GetWindow<FirstContactMultilingualEmbeddingBenchmarkWindow>(
                "First Contact Embeddings");
        }

        private void OnEnable()
        {
            _settings = FindSemanticSettings();
        }

        private void OnDisable()
        {
            _service?.Dispose();
            _service = null;
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Multilingual Label Embedding Benchmark", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Runs the configured EmbeddingGemma GGUF against direct translations, related concepts, " +
                "and ambiguous labels. Diagnostic polysemy rows are not used to suggest a threshold.",
                MessageType.Info);

            _settings = (FirstContactSemanticSettings)EditorGUILayout.ObjectField(
                "Semantic Settings",
                _settings,
                typeof(FirstContactSemanticSettings),
                false);

            using (new EditorGUI.DisabledScope(_running || _settings == null))
            {
                if (GUILayout.Button("Run Benchmark", GUILayout.Height(28f)))
                {
                    StartBenchmark();
                }
            }

            using (new EditorGUI.DisabledScope(_running || _results.Count == 0))
            {
                if (GUILayout.Button("Export CSV"))
                {
                    ExportCsv();
                }
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Status", _status);
            if (_results.Count > 0)
            {
                DrawSummary();
                DrawResults();
            }
        }

        private void DrawSummary()
        {
            float[] same = _results
                .Where(result => result.IsValid && result.TestCase.Expected == Expectation.Same)
                .Select(result => result.Similarity)
                .ToArray();
            float[] different = _results
                .Where(result => result.IsValid && result.TestCase.Expected == Expectation.Different)
                .Select(result => result.Similarity)
                .ToArray();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Same concept", FormatDistribution(same));
            EditorGUILayout.LabelField("Different concept", FormatDistribution(different));
            EditorGUILayout.LabelField(
                "Suggested split (sample only)",
                _suggestedThreshold.ToString("0.000", CultureInfo.InvariantCulture));
            EditorGUILayout.HelpBox(
                "Use the result distribution to tune the review and certain-duplicate thresholds. " +
                "Do not copy the suggested split blindly into release settings.",
                MessageType.Warning);
        }

        private void DrawResults()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Pairs", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (BenchmarkResult result in _results)
            {
                string score = result.IsValid
                    ? result.Similarity.ToString("0.0000", CultureInfo.InvariantCulture)
                    : "ERROR";
                EditorGUILayout.LabelField(
                    $"{score}  [{result.TestCase.Expected}]  {result.TestCase.Left}  ↔  {result.TestCase.Right}");
                if (!result.IsValid)
                {
                    EditorGUILayout.LabelField(result.Error, EditorStyles.miniLabel);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void StartBenchmark()
        {
            _running = true;
            _results.Clear();
            _status = "Loading embedding model and evaluating labels...";
            _service?.Dispose();
            _service = new RoutingEditorLlmService(logTraffic: false);
            EditorCoroutineRunner.Start(RunBenchmarkRoutine());
        }

        private IEnumerator RunBenchmarkRoutine()
        {
            var embeddingService = new FirstContactEmbeddingService(_service, _settings);
            string[] labels = DefaultCases
                .SelectMany(testCase => new[] { testCase.Left, testCase.Right })
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            IReadOnlyList<EmbeddingResult> embeddings = null;
            yield return embeddingService.EmbedLabels(labels, result => embeddings = result);

            var vectors = new Dictionary<string, EmbeddingResult>(StringComparer.Ordinal);
            if (embeddings != null)
            {
                for (int i = 0; i < labels.Length && i < embeddings.Count; i++)
                {
                    vectors[labels[i]] = embeddings[i];
                }
            }

            foreach (BenchmarkCase testCase in DefaultCases)
            {
                if (!vectors.TryGetValue(testCase.Left, out EmbeddingResult left) || !left.IsValid)
                {
                    _results.Add(new BenchmarkResult(testCase, 0f, left.Error ?? "Left embedding missing."));
                    continue;
                }

                if (!vectors.TryGetValue(testCase.Right, out EmbeddingResult right) || !right.IsValid)
                {
                    _results.Add(new BenchmarkResult(testCase, 0f, right.Error ?? "Right embedding missing."));
                    continue;
                }

                _results.Add(new BenchmarkResult(
                    testCase,
                    embeddingService.Similarity(left.Vector, right.Vector),
                    string.Empty));
            }

            _suggestedThreshold = FindBestThreshold(_results);
            _running = false;
            _status = _results.Any(result => !result.IsValid)
                ? "Completed with embedding errors."
                : $"Completed {_results.Count} comparisons.";
            _service?.Dispose();
            _service = null;
            Repaint();
        }

        private static float FindBestThreshold(IReadOnlyList<BenchmarkResult> results)
        {
            float[] scores = results
                .Where(result => result.IsValid && result.TestCase.Expected != Expectation.Diagnostic)
                .Select(result => result.Similarity)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
            if (scores.Length == 0)
            {
                return 0f;
            }

            float bestThreshold = scores[0];
            float bestBalancedAccuracy = -1f;
            for (int i = 0; i <= scores.Length; i++)
            {
                float threshold = i == 0
                    ? scores[0] - 0.0001f
                    : i == scores.Length
                        ? scores[^1] + 0.0001f
                        : (scores[i - 1] + scores[i]) * 0.5f;
                int sameTotal = 0;
                int sameCorrect = 0;
                int differentTotal = 0;
                int differentCorrect = 0;
                foreach (BenchmarkResult result in results)
                {
                    if (!result.IsValid || result.TestCase.Expected == Expectation.Diagnostic)
                    {
                        continue;
                    }

                    if (result.TestCase.Expected == Expectation.Same)
                    {
                        sameTotal++;
                        sameCorrect += result.Similarity >= threshold ? 1 : 0;
                    }
                    else
                    {
                        differentTotal++;
                        differentCorrect += result.Similarity < threshold ? 1 : 0;
                    }
                }

                float truePositiveRate = sameTotal > 0 ? (float)sameCorrect / sameTotal : 0f;
                float trueNegativeRate = differentTotal > 0 ? (float)differentCorrect / differentTotal : 0f;
                float balancedAccuracy = (truePositiveRate + trueNegativeRate) * 0.5f;
                if (balancedAccuracy > bestBalancedAccuracy)
                {
                    bestBalancedAccuracy = balancedAccuracy;
                    bestThreshold = threshold;
                }
            }

            return bestThreshold;
        }

        private void ExportCsv()
        {
            string path = EditorUtility.SaveFilePanel(
                "Export multilingual embedding benchmark",
                Application.dataPath,
                "first_contact_multilingual_embedding_benchmark.csv",
                "csv");
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var csv = new StringBuilder("group,expectation,left,right,similarity,error\n");
            foreach (BenchmarkResult result in _results)
            {
                csv.Append(EscapeCsv(result.TestCase.Group)).Append(',')
                    .Append(result.TestCase.Expected).Append(',')
                    .Append(EscapeCsv(result.TestCase.Left)).Append(',')
                    .Append(EscapeCsv(result.TestCase.Right)).Append(',')
                    .Append(result.IsValid
                        ? result.Similarity.ToString("0.000000", CultureInfo.InvariantCulture)
                        : string.Empty)
                    .Append(',').Append(EscapeCsv(result.Error)).Append('\n');
            }

            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            EditorUtility.RevealInFinder(path);
        }

        private static string FormatDistribution(IReadOnlyList<float> values)
        {
            return values == null || values.Count == 0
                ? "No valid values"
                : $"min {values.Min():0.000} / avg {values.Average():0.000} / max {values.Max():0.000}";
        }

        private static string EscapeCsv(string value)
        {
            value ??= string.Empty;
            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        private static FirstContactSemanticSettings FindSemanticSettings()
        {
            string guid = AssetDatabase.FindAssets("t:FirstContactSemanticSettings").FirstOrDefault();
            if (string.IsNullOrWhiteSpace(guid))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<FirstContactSemanticSettings>(
                AssetDatabase.GUIDToAssetPath(guid));
        }
    }
}
#endif
