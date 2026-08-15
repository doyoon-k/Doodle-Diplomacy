#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEditor;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact.Editor
{
    /// <summary>
    /// Executes the production First Contact semantic pipelines in Edit Mode.
    /// A request placed in Temp/FirstContactSemanticEvaluation/request.json is
    /// consumed automatically and removed after result.json has been written.
    /// </summary>
    [InitializeOnLoad]
    public static class FirstContactSemanticEvaluationRunner
    {
        internal const string BootstrapKind = "bootstrap";
        internal const string GroupSeedKind = "group_seed";
        internal const string GroupMembershipKind = "group_membership";
        internal const string NonEmptyExpectation = "non_empty";
        internal const string EmptyExpectation = "empty";

        private const string BootstrapPipelinePath =
            "Assets/ScriptableObjects/Pipeline/FirstContactBootstrapCategoryFitPipeline.asset";
        private const string GroupSeedPipelinePath =
            "Assets/ScriptableObjects/Pipeline/FirstContactSemanticGroupSeedPipeline.asset";
        private const string GroupMembershipPipelinePath =
            "Assets/ScriptableObjects/Pipeline/FirstContactSemanticGroupFitPipeline.asset";
        private const double PollIntervalSeconds = 0.5d;
        private const double RequestSettleSeconds = 0.25d;

        private static double _nextPollAt;
        private static EvaluationSession _session;

        static FirstContactSemanticEvaluationRunner()
        {
            EditorApplication.update += PollForRequest;
            EditorApplication.quitting += CancelActiveEvaluation;
            AssemblyReloadEvents.beforeAssemblyReload += CancelActiveEvaluation;
        }

        public static string EvaluationDirectory => Path.Combine(
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath,
            "Temp",
            "FirstContactSemanticEvaluation");

        public static string RequestPath => Path.Combine(EvaluationDirectory, "request.json");
        public static string ResultPath => Path.Combine(EvaluationDirectory, "result.json");

        [MenuItem("Tools/First Contact/Run Pending Semantic Evaluation")]
        public static void RunPendingRequest()
        {
            TryStartPendingRequest(ignoreSettleTime: true);
        }

        [MenuItem("Tools/First Contact/Open Semantic Evaluation Folder")]
        private static void OpenEvaluationFolder()
        {
            Directory.CreateDirectory(EvaluationDirectory);
            EditorUtility.RevealInFinder(EvaluationDirectory);
        }

        private static void PollForRequest()
        {
            if (EditorApplication.timeSinceStartup < _nextPollAt)
            {
                return;
            }

            _nextPollAt = EditorApplication.timeSinceStartup + PollIntervalSeconds;
            TryStartPendingRequest(ignoreSettleTime: false);
        }

        private static void TryStartPendingRequest(bool ignoreSettleTime)
        {
            if (_session != null ||
                EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating ||
                !File.Exists(RequestPath))
            {
                return;
            }

            if (!ignoreSettleTime &&
                (DateTime.UtcNow - File.GetLastWriteTimeUtc(RequestPath)).TotalSeconds <
                RequestSettleSeconds)
            {
                return;
            }

            string json;
            try
            {
                json = File.ReadAllText(RequestPath, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(
                    $"[FirstContactSemanticEvaluation] Could not read request: {ex.Message}");
                return;
            }

            if (!TryDeserializeRequest(json, out FirstContactSemanticEvaluationRequest request, out string error))
            {
                WriteRejectedRequestResult(error);
                DeleteRequestFile();
                return;
            }

            PromptPipelineAsset bootstrap =
                AssetDatabase.LoadAssetAtPath<PromptPipelineAsset>(BootstrapPipelinePath);
            PromptPipelineAsset groupSeed =
                AssetDatabase.LoadAssetAtPath<PromptPipelineAsset>(GroupSeedPipelinePath);
            PromptPipelineAsset groupMembership =
                AssetDatabase.LoadAssetAtPath<PromptPipelineAsset>(GroupMembershipPipelinePath);
            if (bootstrap == null || groupSeed == null || groupMembership == null)
            {
                WriteRejectedRequestResult("One or more production semantic pipeline assets are missing.");
                DeleteRequestFile();
                return;
            }

            _session = new EvaluationSession(
                request,
                BuildWorkItems(request, bootstrap, groupSeed, groupMembership),
                new RoutingEditorLlmService(logTraffic: false));
            UnityEngine.Debug.Log(
                $"[FirstContactSemanticEvaluation] Started run='{request.runId}' " +
                $"cases={_session.Items.Count}. No Play Mode or drawing input is used.");
            RunNextCase();
        }

        internal static bool TryDeserializeRequest(
            string json,
            out FirstContactSemanticEvaluationRequest request,
            out string error)
        {
            request = null;
            error = string.Empty;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Evaluation request is empty.";
                return false;
            }

            try
            {
                request = JsonUtility.FromJson<FirstContactSemanticEvaluationRequest>(json);
            }
            catch (Exception ex)
            {
                error = $"Evaluation request JSON is invalid: {ex.Message}";
                return false;
            }

            if (request == null)
            {
                error = "Evaluation request JSON produced no request object.";
                return false;
            }

            int caseCount = (request.bootstrapCases?.Length ?? 0) +
                            (request.groupSeedCases?.Length ?? 0) +
                            (request.groupMembershipCases?.Length ?? 0);
            if (caseCount == 0)
            {
                error = "Evaluation request contains no cases.";
                return false;
            }

            request.runId = string.IsNullOrWhiteSpace(request.runId)
                ? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
                : request.runId.Trim();
            return true;
        }

        private static List<EvaluationWorkItem> BuildWorkItems(
            FirstContactSemanticEvaluationRequest request,
            PromptPipelineAsset bootstrap,
            PromptPipelineAsset groupSeed,
            PromptPipelineAsset groupMembership)
        {
            var items = new List<EvaluationWorkItem>();

            foreach (FirstContactBootstrapEvaluationCase testCase in
                     request.bootstrapCases ?? Array.Empty<FirstContactBootstrapEvaluationCase>())
            {
                if (testCase == null)
                {
                    continue;
                }

                var state = new PipelineState();
                state.SetString("category_definition", testCase.categoryDefinition ?? string.Empty);
                state.SetString("probe_display_label_json", SerializeJson(testCase.subject));
                items.Add(new EvaluationWorkItem(
                    ResolveCaseId(testCase.id, BootstrapKind, items.Count),
                    BootstrapKind,
                    bootstrap,
                    state,
                    testCase.categoryDefinition,
                    testCase.subject,
                    null,
                    null,
                    null,
                    testCase.expectedDecision));
            }

            foreach (FirstContactGroupSeedEvaluationCase testCase in
                     request.groupSeedCases ?? Array.Empty<FirstContactGroupSeedEvaluationCase>())
            {
                if (testCase == null)
                {
                    continue;
                }

                string[] members = testCase.existingMembers ?? Array.Empty<string>();
                var state = new PipelineState();
                state.SetString("new_meaning_json", SerializeJson(testCase.newMeaning));
                state.SetString("existing_members_json", JsonSerializer.Serialize(members));
                items.Add(new EvaluationWorkItem(
                    ResolveCaseId(testCase.id, GroupSeedKind, items.Count),
                    GroupSeedKind,
                    groupSeed,
                    state,
                    null,
                    null,
                    testCase.newMeaning,
                    members,
                    null,
                    testCase.expectedCategoryPresence));
            }

            foreach (FirstContactGroupMembershipEvaluationCase testCase in
                     request.groupMembershipCases ?? Array.Empty<FirstContactGroupMembershipEvaluationCase>())
            {
                if (testCase == null)
                {
                    continue;
                }

                var state = new PipelineState();
                state.SetString("new_meaning_json", SerializeJson(testCase.newMeaning));
                state.SetString("existing_category_json", SerializeJson(testCase.existingCategory));
                items.Add(new EvaluationWorkItem(
                    ResolveCaseId(testCase.id, GroupMembershipKind, items.Count),
                    GroupMembershipKind,
                    groupMembership,
                    state,
                    null,
                    null,
                    testCase.newMeaning,
                    null,
                    testCase.existingCategory,
                    testCase.expectedDecision));
            }

            return items;
        }

        private static void RunNextCase()
        {
            EvaluationSession session = _session;
            if (session == null)
            {
                return;
            }

            if (session.NextIndex >= session.Items.Count)
            {
                CompleteEvaluation();
                return;
            }

            EvaluationWorkItem item = session.Items[session.NextIndex++];
            var stopwatch = Stopwatch.StartNew();
            PromptPipelineSimulator.Run(
                item.Pipeline,
                item.State,
                state =>
                {
                    stopwatch.Stop();
                    session.Results.Add(BuildResult(item, state, string.Empty, stopwatch.Elapsed.TotalSeconds));
                    RunNextCase();
                },
                error =>
                {
                    stopwatch.Stop();
                    session.Results.Add(BuildResult(item, null, error, stopwatch.Elapsed.TotalSeconds));
                    RunNextCase();
                },
                message => UnityEngine.Debug.Log(
                    $"[FirstContactSemanticEvaluation:{item.Id}] {message}"),
                session.Service);
        }

        private static FirstContactSemanticEvaluationCaseResult BuildResult(
            EvaluationWorkItem item,
            PipelineState state,
            string executionError,
            double durationSeconds)
        {
            string pipelineError = state?.GetString(PromptPipelineConstants.ErrorKey) ?? string.Empty;
            string error = string.IsNullOrWhiteSpace(executionError)
                ? pipelineError
                : executionError;
            string decision = string.Empty;
            string category = string.Empty;
            if (state != null)
            {
                if (string.Equals(item.Kind, GroupSeedKind, StringComparison.Ordinal))
                {
                    category = FirstContactSemanticCategory.Normalize(state.GetString("category"));
                }
                else
                {
                    decision = state.GetString("decision").Trim();
                }
            }

            bool hasExpectation = !string.IsNullOrWhiteSpace(item.Expected);
            bool expectationPassed = hasExpectation && EvaluateExpectation(
                item.Kind,
                item.Expected,
                decision,
                category);
            string status = !string.IsNullOrWhiteSpace(error)
                ? "error"
                : !hasExpectation
                    ? "unscored"
                    : expectationPassed
                        ? "passed"
                        : "failed";

            return new FirstContactSemanticEvaluationCaseResult
            {
                id = item.Id,
                kind = item.Kind,
                categoryDefinition = item.CategoryDefinition ?? string.Empty,
                subject = item.Subject ?? string.Empty,
                newMeaning = item.NewMeaning ?? string.Empty,
                existingMembers = item.ExistingMembers ?? Array.Empty<string>(),
                existingCategory = item.ExistingCategory ?? string.Empty,
                expected = item.Expected ?? string.Empty,
                decision = decision,
                category = category,
                status = status,
                error = error ?? string.Empty,
                durationSeconds = durationSeconds
            };
        }

        internal static bool EvaluateExpectation(
            string kind,
            string expected,
            string decision,
            string category)
        {
            expected = expected?.Trim() ?? string.Empty;
            if (string.Equals(kind, GroupSeedKind, StringComparison.Ordinal))
            {
                if (string.Equals(expected, NonEmptyExpectation, StringComparison.OrdinalIgnoreCase))
                {
                    return !string.IsNullOrWhiteSpace(category);
                }

                if (string.Equals(expected, EmptyExpectation, StringComparison.OrdinalIgnoreCase))
                {
                    return string.IsNullOrWhiteSpace(category);
                }

                return string.Equals(
                    FirstContactSemanticCategory.Normalize(category),
                    FirstContactSemanticCategory.Normalize(expected),
                    StringComparison.OrdinalIgnoreCase);
            }

            return string.Equals(decision, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static void CompleteEvaluation()
        {
            EvaluationSession session = _session;
            if (session == null)
            {
                return;
            }

            var report = new FirstContactSemanticEvaluationReport
            {
                runId = session.Request.runId,
                startedUtc = session.StartedUtc.ToString("O"),
                completedUtc = DateTime.UtcNow.ToString("O"),
                total = session.Results.Count,
                passed = session.Results.Count(result => result.status == "passed"),
                failed = session.Results.Count(result => result.status == "failed"),
                errors = session.Results.Count(result => result.status == "error"),
                unscored = session.Results.Count(result => result.status == "unscored"),
                results = session.Results.ToArray()
            };

            try
            {
                Directory.CreateDirectory(EvaluationDirectory);
                File.WriteAllText(
                    ResultPath,
                    JsonUtility.ToJson(report, prettyPrint: true),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                UnityEngine.Debug.Log(
                    $"[FirstContactSemanticEvaluation] Completed run='{report.runId}' " +
                    $"passed={report.passed} failed={report.failed} errors={report.errors} " +
                    $"unscored={report.unscored} result='{ResultPath}'.");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(
                    $"[FirstContactSemanticEvaluation] Could not write result: {ex}");
            }
            finally
            {
                DeleteRequestFile();
                session.Service.Dispose();
                _session = null;
            }
        }

        private static void WriteRejectedRequestResult(string error)
        {
            try
            {
                Directory.CreateDirectory(EvaluationDirectory);
                var report = new FirstContactSemanticEvaluationReport
                {
                    startedUtc = DateTime.UtcNow.ToString("O"),
                    completedUtc = DateTime.UtcNow.ToString("O"),
                    errors = 1,
                    fatalError = error ?? "Evaluation request was rejected.",
                    results = Array.Empty<FirstContactSemanticEvaluationCaseResult>()
                };
                File.WriteAllText(
                    ResultPath,
                    JsonUtility.ToJson(report, prettyPrint: true),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                UnityEngine.Debug.LogError(
                    $"[FirstContactSemanticEvaluation] {report.fatalError} Result='{ResultPath}'.");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(
                    $"[FirstContactSemanticEvaluation] Could not record rejected request: {ex}");
            }
        }

        private static void CancelActiveEvaluation()
        {
            PromptPipelineSimulator.CancelActiveSimulation();
            _session?.Service.Dispose();
            _session = null;
        }

        private static void DeleteRequestFile()
        {
            try
            {
                if (File.Exists(RequestPath))
                {
                    File.Delete(RequestPath);
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogWarning(
                    $"[FirstContactSemanticEvaluation] Could not delete consumed request: {ex.Message}");
            }
        }

        private static string ResolveCaseId(string id, string kind, int index)
        {
            return string.IsNullOrWhiteSpace(id)
                ? $"{kind}-{index + 1:000}"
                : id.Trim();
        }

        private static string SerializeJson(string value)
        {
            return JsonSerializer.Serialize(value ?? string.Empty);
        }

        private sealed class EvaluationSession
        {
            public EvaluationSession(
                FirstContactSemanticEvaluationRequest request,
                List<EvaluationWorkItem> items,
                RoutingEditorLlmService service)
            {
                Request = request;
                Items = items;
                Service = service;
                StartedUtc = DateTime.UtcNow;
            }

            public FirstContactSemanticEvaluationRequest Request { get; }
            public List<EvaluationWorkItem> Items { get; }
            public RoutingEditorLlmService Service { get; }
            public DateTime StartedUtc { get; }
            public List<FirstContactSemanticEvaluationCaseResult> Results { get; } = new();
            public int NextIndex { get; set; }
        }

        private sealed class EvaluationWorkItem
        {
            public EvaluationWorkItem(
                string id,
                string kind,
                PromptPipelineAsset pipeline,
                PipelineState state,
                string categoryDefinition,
                string subject,
                string newMeaning,
                string[] existingMembers,
                string existingCategory,
                string expected)
            {
                Id = id;
                Kind = kind;
                Pipeline = pipeline;
                State = state;
                CategoryDefinition = categoryDefinition;
                Subject = subject;
                NewMeaning = newMeaning;
                ExistingMembers = existingMembers;
                ExistingCategory = existingCategory;
                Expected = expected;
            }

            public string Id { get; }
            public string Kind { get; }
            public PromptPipelineAsset Pipeline { get; }
            public PipelineState State { get; }
            public string CategoryDefinition { get; }
            public string Subject { get; }
            public string NewMeaning { get; }
            public string[] ExistingMembers { get; }
            public string ExistingCategory { get; }
            public string Expected { get; }
        }
    }

    [Serializable]
    public sealed class FirstContactSemanticEvaluationRequest
    {
        public string runId = string.Empty;
        public FirstContactBootstrapEvaluationCase[] bootstrapCases =
            Array.Empty<FirstContactBootstrapEvaluationCase>();
        public FirstContactGroupSeedEvaluationCase[] groupSeedCases =
            Array.Empty<FirstContactGroupSeedEvaluationCase>();
        public FirstContactGroupMembershipEvaluationCase[] groupMembershipCases =
            Array.Empty<FirstContactGroupMembershipEvaluationCase>();
    }

    [Serializable]
    public sealed class FirstContactBootstrapEvaluationCase
    {
        public string id = string.Empty;
        public string categoryDefinition = string.Empty;
        public string subject = string.Empty;
        public string expectedDecision = string.Empty;
    }

    [Serializable]
    public sealed class FirstContactGroupSeedEvaluationCase
    {
        public string id = string.Empty;
        public string newMeaning = string.Empty;
        public string[] existingMembers = Array.Empty<string>();
        public string expectedCategoryPresence = string.Empty;
    }

    [Serializable]
    public sealed class FirstContactGroupMembershipEvaluationCase
    {
        public string id = string.Empty;
        public string newMeaning = string.Empty;
        public string existingCategory = string.Empty;
        public string expectedDecision = string.Empty;
    }

    [Serializable]
    public sealed class FirstContactSemanticEvaluationCaseResult
    {
        public string id = string.Empty;
        public string kind = string.Empty;
        public string categoryDefinition = string.Empty;
        public string subject = string.Empty;
        public string newMeaning = string.Empty;
        public string[] existingMembers = Array.Empty<string>();
        public string existingCategory = string.Empty;
        public string expected = string.Empty;
        public string decision = string.Empty;
        public string category = string.Empty;
        public string status = string.Empty;
        public string error = string.Empty;
        public double durationSeconds;
    }

    [Serializable]
    public sealed class FirstContactSemanticEvaluationReport
    {
        public string runId = string.Empty;
        public string startedUtc = string.Empty;
        public string completedUtc = string.Empty;
        public int total;
        public int passed;
        public int failed;
        public int errors;
        public int unscored;
        public string fatalError = string.Empty;
        public FirstContactSemanticEvaluationCaseResult[] results =
            Array.Empty<FirstContactSemanticEvaluationCaseResult>();
    }
}
#endif
