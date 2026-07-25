using System;
using System.Collections.Generic;
using System.Text;
using DoodleDiplomacy.Localization;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public readonly struct FirstContactProbeLabelFeedback
    {
        public FirstContactProbeLabelFeedback(
            string statusKey,
            string statusFallback,
            string guidanceLineKey)
        {
            StatusKey = statusKey ?? string.Empty;
            StatusFallback = statusFallback ?? string.Empty;
            GuidanceLineKey = guidanceLineKey ?? string.Empty;
        }

        public string StatusKey { get; }
        public string StatusFallback { get; }
        public string GuidanceLineKey { get; }
    }

    public static class FirstContactProbeFeedback
    {
        public static string ResolveCategoryGuidanceLine(
            FirstContactBootstrapCategoryFitResult _)
        {
            return "first_contact.doctor_hwang.bootstrap_category_mismatch";
        }

        public static FirstContactProbeLabelFeedback ResolveLabelIssue(
            FirstContactProbeLabelIssue issue)
        {
            return issue switch
            {
                FirstContactProbeLabelIssue.LabelMismatch => new FirstContactProbeLabelFeedback(
                    "first_contact.terminal.status.label_mismatch",
                    "LABEL MISMATCH",
                    "first_contact.doctor_hwang.probe_label_mismatch"),
                FirstContactProbeLabelIssue.ActionOrAbstract => new FirstContactProbeLabelFeedback(
                    "first_contact.terminal.status.label_action_or_abstract",
                    "LABEL: ACTION OR ABSTRACT",
                    "first_contact.doctor_hwang.probe_label_action_or_abstract"),
                FirstContactProbeLabelIssue.BroadCategory => new FirstContactProbeLabelFeedback(
                    "first_contact.terminal.status.label_broad_category",
                    "LABEL: CATEGORY TOO BROAD",
                    "first_contact.doctor_hwang.probe_label_broad_category"),
                FirstContactProbeLabelIssue.MultipleSubjects => new FirstContactProbeLabelFeedback(
                    "first_contact.terminal.status.label_multiple_subjects",
                    "LABEL: MULTIPLE SUBJECTS",
                    "first_contact.doctor_hwang.probe_label_multiple_subjects"),
                FirstContactProbeLabelIssue.ClassificationClaim => new FirstContactProbeLabelFeedback(
                    "first_contact.terminal.status.label_classification_claim",
                    "LABEL: REMOVE CLAIM",
                    "first_contact.doctor_hwang.probe_label_classification_claim"),
                _ => new FirstContactProbeLabelFeedback(
                    "first_contact.terminal.status.label_action_or_abstract",
                    "LABEL: ACTION OR ABSTRACT",
                    "first_contact.doctor_hwang.probe_label_action_or_abstract")
            };
        }

        public static bool TryGetContentRedrawPrompt(
            FirstContactProbeValidationResult result,
            FirstContactVlmSettings settings,
            out string prompt,
            out string guidanceLineKey)
        {
            prompt = string.Empty;
            guidanceLineKey = string.Empty;
            if (result == null)
            {
                prompt = "OBJECT NOT CLEAR";
                guidanceLineKey = "first_contact.doctor_hwang.probe_unresolved_object";
                return true;
            }

            IReadOnlyList<FirstContactProbeVisualIssue> issues =
                result.CollectRejectedVisualIssues(settings);
            if (issues.Count == 0)
            {
                return false;
            }

            var promptBuilder = new StringBuilder();
            for (int i = 0; i < issues.Count; i++)
            {
                ResolveVisualIssue(
                    issues[i],
                    out string issuePrompt,
                    out string issueGuidanceLineKey);
                if (promptBuilder.Length > 0)
                {
                    promptBuilder.Append('\n');
                }

                promptBuilder.Append(issuePrompt);
                if (string.IsNullOrWhiteSpace(guidanceLineKey))
                {
                    guidanceLineKey = issueGuidanceLineKey;
                }
            }

            prompt = promptBuilder.ToString();
            return prompt.Length > 0;
        }

        public static bool TryGetValidationErrorRedrawPrompt(
            FirstContactProbeValidationResult result,
            out string prompt,
            out string guidanceLineKey)
        {
            prompt = string.Empty;
            guidanceLineKey = string.Empty;
            if (result == null || result.IsSuccess || string.IsNullOrWhiteSpace(result.Error))
            {
                return false;
            }

            return TryGetBlankDrawingRedrawPrompt(
                result.Error,
                out prompt,
                out guidanceLineKey);
        }

        public static bool TryGetBlankDrawingRedrawPrompt(
            string error,
            out string prompt,
            out string guidanceLineKey)
        {
            prompt = string.Empty;
            guidanceLineKey = string.Empty;
            if (string.IsNullOrWhiteSpace(error) ||
                !error.Trim().Equals("Drawing is blank.", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            prompt = "DRAW SOMETHING";
            guidanceLineKey = "first_contact.doctor_hwang.probe_blank";
            return true;
        }

        public static bool IsFatalValidationFailure(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return false;
            }

            string normalized = error.Trim().ToLowerInvariant();
            return normalized.Contains("validator pipeline is not assigned") ||
                   normalized.Contains("probe validation pipeline is not assigned") ||
                   normalized.Contains("gamepipelinerunner is missing") ||
                   normalized.Contains("drawing texture is unavailable");
        }

        public static string LocalizeRedrawPrompt(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return L10n.T("first_contact.terminal.reason.draw_one_object", "DRAW ONE OBJECT");
            }

            string normalizedPrompt = prompt.Replace("\r\n", "\n").Replace('\r', '\n');
            if (normalizedPrompt.Contains('\n'))
            {
                string[] lines = normalizedPrompt.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    lines[i] = LocalizeRedrawPrompt(lines[i]);
                }

                return string.Join("\n", lines);
            }

            string normalized = normalizedPrompt.Trim().ToUpperInvariant();
            return normalized switch
            {
                "DRAW SOMETHING" => L10n.T(
                    "first_contact.terminal.reason.draw_something",
                    "DRAW SOMETHING"),
                "DRAW ONE OBJECT" => L10n.T(
                    "first_contact.terminal.reason.draw_one_object",
                    "DRAW ONE OBJECT"),
                "DRAW ONE OBJECT ONLY" => L10n.T(
                    "first_contact.terminal.reason.draw_one_object_only",
                    "DRAW ONE OBJECT ONLY"),
                "TEXT OR SYMBOL DETECTED" => L10n.T(
                    "first_contact.terminal.reason.text_or_symbol_detected",
                    "TEXT OR SYMBOL DETECTED"),
                "SCENE OR ACTION DETECTED" => L10n.T(
                    "first_contact.terminal.reason.scene_or_action_detected",
                    "SCENE OR ACTION DETECTED"),
                "OBJECT NOT CLEAR" => L10n.T(
                    "first_contact.terminal.reason.object_not_clear",
                    "OBJECT NOT CLEAR"),
                _ => prompt.Trim()
            };
        }

        public static string GetRedrawPromptLocalizationKey(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return "first_contact.terminal.reason.draw_one_object";
            }

            string[] lines = prompt.Replace("\r\n", "\n").Replace('\r', '\n')
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                return string.Empty;
            }

            string firstLine = lines[0].Trim().ToUpperInvariant();
            return firstLine switch
            {
                "DRAW SOMETHING" => "first_contact.terminal.reason.draw_something",
                "DRAW ONE OBJECT" => "first_contact.terminal.reason.draw_one_object",
                "DRAW ONE OBJECT ONLY" => "first_contact.terminal.reason.draw_one_object_only",
                "TEXT OR SYMBOL DETECTED" => "first_contact.terminal.reason.text_or_symbol_detected",
                "SCENE OR ACTION DETECTED" => "first_contact.terminal.reason.scene_or_action_detected",
                "OBJECT NOT CLEAR" => "first_contact.terminal.reason.object_not_clear",
                _ => string.Empty
            };
        }

        private static void ResolveVisualIssue(
            FirstContactProbeVisualIssue issue,
            out string prompt,
            out string guidanceLineKey)
        {
            switch (issue)
            {
                case FirstContactProbeVisualIssue.Blank:
                    prompt = "DRAW SOMETHING";
                    guidanceLineKey = "first_contact.doctor_hwang.probe_blank";
                    break;
                case FirstContactProbeVisualIssue.TextOrSymbol:
                    prompt = "TEXT OR SYMBOL DETECTED";
                    guidanceLineKey = "first_contact.doctor_hwang.probe_text_or_symbol";
                    break;
                case FirstContactProbeVisualIssue.SceneOrAction:
                    prompt = "SCENE OR ACTION DETECTED";
                    guidanceLineKey = "first_contact.doctor_hwang.probe_scene_or_action";
                    break;
                case FirstContactProbeVisualIssue.MultipleObjects:
                    prompt = "DRAW ONE OBJECT ONLY";
                    guidanceLineKey = "first_contact.doctor_hwang.probe_multiple_objects";
                    break;
                default:
                    prompt = "OBJECT NOT CLEAR";
                    guidanceLineKey = "first_contact.doctor_hwang.probe_unresolved_object";
                    break;
            }
        }
    }
}
