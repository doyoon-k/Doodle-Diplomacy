#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
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
        private const string DiverseDatasetRelativePath =
            "TestData/FirstContactSemanticEvaluation/semantic_diverse_v1.json";
        private const string WildDatasetRelativePath =
            "TestData/FirstContactSemanticEvaluation/semantic_wild_v1.json";
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

        public static string ProjectDirectory =>
            Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;

        public static string EvaluationDirectory => Path.Combine(
            ProjectDirectory,
            "Temp",
            "FirstContactSemanticEvaluation");

        public static string RequestPath => Path.Combine(EvaluationDirectory, "request.json");
        public static string ResultPath => Path.Combine(EvaluationDirectory, "result.json");
        public static string DiverseDatasetPath => Path.Combine(
            ProjectDirectory,
            DiverseDatasetRelativePath.Replace('/', Path.DirectorySeparatorChar));
        public static string WildDatasetPath => Path.Combine(
            ProjectDirectory,
            WildDatasetRelativePath.Replace('/', Path.DirectorySeparatorChar));

        [MenuItem("Tools/First Contact/Run Diverse Semantic Dataset")]
        public static void RunDiverseDataset()
        {
            QueueDataset(DiverseDatasetPath, "diverse");
        }

        [MenuItem("Tools/First Contact/Run Wild Semantic Dataset")]
        public static void RunWildDataset()
        {
            QueueDataset(WildDatasetPath, "wild");
        }

        private static void QueueDataset(string datasetPath, string datasetName)
        {
            if (_session != null)
            {
                UnityEngine.Debug.LogWarning(
                    "[FirstContactSemanticEvaluation] An evaluation is already running.");
                return;
            }

            if (!File.Exists(datasetPath))
            {
                UnityEngine.Debug.LogError(
                    $"[FirstContactSemanticEvaluation] Dataset not found: {datasetPath}");
                return;
            }

            if (File.Exists(RequestPath))
            {
                UnityEngine.Debug.LogWarning(
                    $"[FirstContactSemanticEvaluation] A request is already pending: {RequestPath}");
                return;
            }

            try
            {
                Directory.CreateDirectory(EvaluationDirectory);
                File.Copy(datasetPath, RequestPath, overwrite: false);
                TryStartPendingRequest(ignoreSettleTime: true);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError(
                    $"[FirstContactSemanticEvaluation] Could not queue {datasetName} dataset: {ex.Message}");
            }
        }

        [MenuItem("Tools/First Contact/Run Pending Semantic Evaluation")]
        public static void RunPendingRequest()
        {
            TryStartPendingRequest(ignoreSettleTime: true);
        }

        [MenuItem("Tools/First Contact/Cancel Semantic Evaluation")]
        public static void CancelEvaluation()
        {
            if (_session == null)
            {
                return;
            }

            UnityEngine.Debug.LogWarning(
                $"[FirstContactSemanticEvaluation] Cancelled run='{_session.Request.runId}' " +
                $"after {_session.Results.Count}/{_session.Items.Count} cases.");
            CancelActiveEvaluation();
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
                new RoutingEditorLlmService(logTraffic: false),
                groupMembership);
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

            int caseCount = CountCases(request);
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

        internal static int CountCases(FirstContactSemanticEvaluationRequest request)
        {
            if (request == null)
            {
                return 0;
            }

            int count = 0;
            if (IncludesKind(request, BootstrapKind))
            {
                count += request.bootstrapCases?.Length ?? 0;
                foreach (FirstContactBootstrapEvaluationSet set in
                         request.bootstrapSets ?? Array.Empty<FirstContactBootstrapEvaluationSet>())
                {
                    if (set == null)
                    {
                        continue;
                    }

                    count += (set.ordinaryMatches?.Length ?? 0) +
                             (set.mismatches?.Length ?? 0) +
                             (set.uncertainSubjects?.Length ?? 0);
                }
            }

            if (IncludesKind(request, GroupSeedKind))
            {
                count += request.groupSeedCases?.Length ?? 0;
            }

            if (IncludesKind(request, GroupMembershipKind))
            {
                count += request.groupMembershipCases?.Length ?? 0;
                foreach (FirstContactGroupMembershipEvaluationSet set in
                         request.groupMembershipSets ??
                         Array.Empty<FirstContactGroupMembershipEvaluationSet>())
                {
                    if (set == null)
                    {
                        continue;
                    }

                    count += (set.joins?.Length ?? 0) +
                             (set.rejects?.Length ?? 0) +
                             (set.uncertainMeanings?.Length ?? 0);
                }
            }

            return count;
        }

        internal static bool IncludesKind(
            FirstContactSemanticEvaluationRequest request,
            string kind)
        {
            string[] includedKinds = request?.includeKinds;
            if (includedKinds == null || includedKinds.Length == 0)
            {
                return true;
            }

            return includedKinds.Any(candidate =>
                string.Equals(candidate?.Trim(), kind, StringComparison.OrdinalIgnoreCase));
        }

        private static List<EvaluationWorkItem> BuildWorkItems(
            FirstContactSemanticEvaluationRequest request,
            PromptPipelineAsset bootstrap,
            PromptPipelineAsset groupSeed,
            PromptPipelineAsset groupMembership)
        {
            var items = new List<EvaluationWorkItem>();

            foreach (FirstContactBootstrapEvaluationSet set in
                     request.bootstrapSets ?? Array.Empty<FirstContactBootstrapEvaluationSet>())
            {
                if (set == null)
                {
                    continue;
                }

                AddBootstrapSetItems(items, bootstrap, set, set.ordinaryMatches, "ordinary_match", "match");
                AddBootstrapSetItems(items, bootstrap, set, set.mismatches, "category_mismatch", "mismatch");
                AddBootstrapSetItems(items, bootstrap, set, set.uncertainSubjects, "uncertain", "uncertain");
            }

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
                string[] seedMembers = members
                    .Concat(new[] { testCase.newMeaning ?? string.Empty })
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .ToArray();
                var state = new PipelineState();
                state.SetString(
                    "seed_members_json",
                    FirstContactPromptJson.SerializeStringArray(seedMembers));
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

            foreach (FirstContactGroupMembershipEvaluationSet set in
                     request.groupMembershipSets ??
                     Array.Empty<FirstContactGroupMembershipEvaluationSet>())
            {
                if (set == null)
                {
                    continue;
                }

                AddMembershipSetItems(items, groupMembership, set, set.joins, "join", "join");
                AddMembershipSetItems(items, groupMembership, set, set.rejects, "reject", "reject");
                AddMembershipSetItems(
                    items,
                    groupMembership,
                    set,
                    set.uncertainMeanings,
                    "uncertain",
                    "uncertain");
            }

            items.RemoveAll(item => !IncludesKind(request, item.Kind));
            return items;
        }

        private static void AddBootstrapSetItems(
            List<EvaluationWorkItem> items,
            PromptPipelineAsset pipeline,
            FirstContactBootstrapEvaluationSet set,
            string[] subjects,
            string expected,
            string idSegment)
        {
            string prefix = string.IsNullOrWhiteSpace(set.idPrefix)
                ? "bootstrap-set"
                : set.idPrefix.Trim();
            int index = 0;
            foreach (string subject in subjects ?? Array.Empty<string>())
            {
                index++;
                var state = new PipelineState();
                state.SetString("category_definition", set.categoryDefinition ?? string.Empty);
                state.SetString("probe_display_label_json", SerializeJson(subject));
                items.Add(new EvaluationWorkItem(
                    $"{prefix}-{idSegment}-{index:000}",
                    BootstrapKind,
                    pipeline,
                    state,
                    set.categoryDefinition,
                    subject,
                    null,
                    null,
                    null,
                    expected));
            }
        }

        private static void AddMembershipSetItems(
            List<EvaluationWorkItem> items,
            PromptPipelineAsset pipeline,
            FirstContactGroupMembershipEvaluationSet set,
            string[] meanings,
            string expected,
            string idSegment)
        {
            string prefix = string.IsNullOrWhiteSpace(set.idPrefix)
                ? "membership-set"
                : set.idPrefix.Trim();
            int index = 0;
            foreach (string meaning in meanings ?? Array.Empty<string>())
            {
                index++;
                var state = new PipelineState();
                state.SetString("new_meaning_json", SerializeJson(meaning));
                state.SetString("existing_category_json", SerializeJson(set.existingCategory));
                items.Add(new EvaluationWorkItem(
                    $"{prefix}-{idSegment}-{index:000}",
                    GroupMembershipKind,
                    pipeline,
                    state,
                    null,
                    null,
                    meaning,
                    null,
                    set.existingCategory,
                    expected));
            }
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
            if (string.Equals(item.Kind, GroupSeedKind, StringComparison.Ordinal))
            {
                RunSeedCase(session, item, stopwatch);
                return;
            }

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

        private static void RunSeedCase(
            EvaluationSession session,
            EvaluationWorkItem item,
            Stopwatch stopwatch)
        {
            PromptPipelineSimulator.Run(
                item.Pipeline,
                item.State,
                seedState =>
                {
                    string category = FirstContactSemanticCategory.Normalize(
                        seedState.GetString("category"));
                    if (category.Length == 0)
                    {
                        seedState.SetString(
                            "decision",
                            FirstContactSemanticGroupFitResult.RejectDecision);
                        FinishCase(session, item, seedState, string.Empty, stopwatch);
                        return;
                    }

                    string[] labels = (item.ExistingMembers ?? Array.Empty<string>())
                        .Concat(new[] { item.NewMeaning ?? string.Empty })
                        .Where(label => !string.IsNullOrWhiteSpace(label))
                        .ToArray();
                    VerifySeedMember(
                        session,
                        item,
                        seedState,
                        category,
                        labels,
                        0,
                        new List<FirstContactSemanticGroupFitResult>(labels.Length),
                        stopwatch);
                },
                error => FinishCase(session, item, null, error, stopwatch),
                message => UnityEngine.Debug.Log(
                    $"[FirstContactSemanticEvaluation:{item.Id}] {message}"),
                session.Service);
        }

        private static void VerifySeedMember(
            EvaluationSession session,
            EvaluationWorkItem item,
            PipelineState seedState,
            string category,
            IReadOnlyList<string> labels,
            int index,
            List<FirstContactSemanticGroupFitResult> memberResults,
            Stopwatch stopwatch)
        {
            if (index >= labels.Count)
            {
                FirstContactSemanticGroupFitResult aggregate =
                    FirstContactSemanticGroupFitResult.ResolveSeedMemberVerifications(
                        category,
                        memberResults);
                seedState.SetString("decision", aggregate.Decision);
                FinishCase(
                    session,
                    item,
                    seedState,
                    aggregate.IsSuccess ? string.Empty : aggregate.Error,
                    stopwatch);
                return;
            }

            var membershipState = new PipelineState();
            membershipState.SetString("new_meaning_json", SerializeJson(labels[index]));
            membershipState.SetString("existing_category_json", SerializeJson(category));
            PromptPipelineSimulator.Run(
                session.GroupMembershipPipeline,
                membershipState,
                finalState =>
                {
                    if (!FirstContactSemanticGroupFitResult.TryFromMembershipPipelineState(
                            finalState,
                            category,
                            out FirstContactSemanticGroupFitResult memberResult))
                    {
                        FinishCase(
                            session,
                            item,
                            seedState,
                            memberResult?.Error ?? "Seed member verification failed.",
                            stopwatch);
                        return;
                    }

                    memberResults.Add(memberResult);
                    if (!memberResult.JoinsGroup)
                    {
                        seedState.SetString(
                            "decision",
                            FirstContactSemanticGroupFitResult.RejectDecision);
                        FinishCase(session, item, seedState, string.Empty, stopwatch);
                        return;
                    }

                    VerifySeedMember(
                        session,
                        item,
                        seedState,
                        category,
                        labels,
                        index + 1,
                        memberResults,
                        stopwatch);
                },
                error => FinishCase(session, item, seedState, error, stopwatch),
                message => UnityEngine.Debug.Log(
                    $"[FirstContactSemanticEvaluation:{item.Id}/member-{index + 1}] {message}"),
                session.Service);
        }

        private static void FinishCase(
            EvaluationSession session,
            EvaluationWorkItem item,
            PipelineState state,
            string error,
            Stopwatch stopwatch)
        {
            stopwatch.Stop();
            session.Results.Add(BuildResult(
                item,
                state,
                error,
                stopwatch.Elapsed.TotalSeconds));
            RunNextCase();
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
                    decision = state.GetString("decision").Trim();
                }
                else if (string.Equals(item.Kind, GroupMembershipKind, StringComparison.Ordinal))
                {
                    if (FirstContactSemanticGroupFitResult.TryFromMembershipPipelineState(
                            state,
                            item.ExistingCategory,
                            out FirstContactSemanticGroupFitResult membershipResult))
                    {
                        decision = membershipResult.Decision;
                        category = membershipResult.Category;
                    }
                    else if (string.IsNullOrWhiteSpace(error))
                    {
                        error = membershipResult?.Error ??
                                "Semantic group membership result could not be parsed.";
                    }
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
                    return string.Equals(
                               decision,
                               FirstContactSemanticGroupFitResult.JoinDecision,
                               StringComparison.OrdinalIgnoreCase) &&
                           !string.IsNullOrWhiteSpace(category);
                }

                if (string.Equals(expected, EmptyExpectation, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Equals(
                        decision,
                        FirstContactSemanticGroupFitResult.RejectDecision,
                        StringComparison.OrdinalIgnoreCase);
                }

                return string.Equals(
                           decision,
                           FirstContactSemanticGroupFitResult.JoinDecision,
                           StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(
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

        internal static string SerializeJson(string value)
        {
            return FirstContactPromptJson.SerializeString(value);
        }

        private sealed class EvaluationSession
        {
            public EvaluationSession(
                FirstContactSemanticEvaluationRequest request,
                List<EvaluationWorkItem> items,
                RoutingEditorLlmService service,
                PromptPipelineAsset groupMembershipPipeline)
            {
                Request = request;
                Items = items;
                Service = service;
                GroupMembershipPipeline = groupMembershipPipeline;
                StartedUtc = DateTime.UtcNow;
            }

            public FirstContactSemanticEvaluationRequest Request { get; }
            public List<EvaluationWorkItem> Items { get; }
            public RoutingEditorLlmService Service { get; }
            public PromptPipelineAsset GroupMembershipPipeline { get; }
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
        public string[] includeKinds = Array.Empty<string>();
        public FirstContactBootstrapEvaluationSet[] bootstrapSets =
            Array.Empty<FirstContactBootstrapEvaluationSet>();
        public FirstContactBootstrapEvaluationCase[] bootstrapCases =
            Array.Empty<FirstContactBootstrapEvaluationCase>();
        public FirstContactGroupSeedEvaluationCase[] groupSeedCases =
            Array.Empty<FirstContactGroupSeedEvaluationCase>();
        public FirstContactGroupMembershipEvaluationSet[] groupMembershipSets =
            Array.Empty<FirstContactGroupMembershipEvaluationSet>();
        public FirstContactGroupMembershipEvaluationCase[] groupMembershipCases =
            Array.Empty<FirstContactGroupMembershipEvaluationCase>();
    }

    [Serializable]
    public sealed class FirstContactBootstrapEvaluationSet
    {
        public string idPrefix = string.Empty;
        public string categoryDefinition = string.Empty;
        public string[] ordinaryMatches = Array.Empty<string>();
        public string[] mismatches = Array.Empty<string>();
        public string[] uncertainSubjects = Array.Empty<string>();
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
    public sealed class FirstContactGroupMembershipEvaluationSet
    {
        public string idPrefix = string.Empty;
        public string existingCategory = string.Empty;
        public string[] joins = Array.Empty<string>();
        public string[] rejects = Array.Empty<string>();
        public string[] uncertainMeanings = Array.Empty<string>();
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
