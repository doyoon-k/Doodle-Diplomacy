using System;
using System.Collections.Generic;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Localization;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactTerminalPresenter
    {
        public static string ProbeLabelInputPrefix => T("line.probe_label_input_prefix", "PROBE LABEL: ");

        private readonly TerminalDisplay _terminalDisplay;
        private readonly TerminalBrainwaveDisplay _brainwaveDisplay;
        private readonly FirstContactSemanticMapDisplay _semanticMapDisplay;
        private readonly FirstContactDebugSettings _debugSettings;
        private readonly FirstContactPresentationSettings _presentationSettings;
        private readonly FirstContactProbePreviewDisplay _probePreviewDisplay;

        public FirstContactTerminalPresenter(
            TerminalDisplay terminalDisplay,
            FirstContactDebugSettings debugSettings,
            FirstContactPresentationSettings presentationSettings = null)
        {
            _terminalDisplay = terminalDisplay;
            _brainwaveDisplay = terminalDisplay != null
                ? terminalDisplay.GetComponent<TerminalBrainwaveDisplay>() ??
                  terminalDisplay.GetComponentInChildren<TerminalBrainwaveDisplay>(true)
                : null;
            _debugSettings = debugSettings;
            _presentationSettings = presentationSettings != null
                ? presentationSettings
                : ScriptableObject.CreateInstance<FirstContactPresentationSettings>();
            _semanticMapDisplay = ResolveSemanticMapDisplay(terminalDisplay);
            _semanticMapDisplay?.SetStyle(_presentationSettings.semanticMapStyle);
            _probePreviewDisplay = ResolveProbePreviewDisplay(terminalDisplay, _presentationSettings);
        }

        public void Clear()
        {
            ClearVisualOverlays();
            _terminalDisplay?.Clear();
        }

        public void ShowBootstrapProbeSequence(
            string categoryId,
            string category,
            int traceCount,
            int requiredTraceCount,
            bool stable,
            int selectedIndex,
            bool instant = true)
        {
            ClearVisualOverlays();
            string text =
                Header("probe_sequence", "[PROBE SEQUENCE]") + "\n\n" +
                T("line.category", "CATEGORY: {category}", L10n.Arg("category", LocalizeCategory(categoryId, category))) + "\n" +
                T("line.group", "GROUP: {group}", L10n.Arg("group", BuildGroupState(traceCount, requiredTraceCount, stable))) + "\n" +
                T("line.trace_count", "TRACE: {count}/{required}",
                    L10n.Arg("count", Mathf.Max(0, traceCount).ToString("00")),
                    L10n.Arg("required", Mathf.Max(1, requiredTraceCount).ToString("00"))) + "\n\n" +
                BuildChoiceLine(0, selectedIndex, T("choice.draw_related_object", "DRAW RELATED OBJECT")) +
                BuildSelectPrompt();
            _terminalDisplay?.ShowText(text, instant);
        }

        public void ShowBootstrapProbeChannelOpen(
            string categoryId,
            string category,
            int traceCount,
            int requiredTraceCount,
            bool instant = false)
        {
            ClearVisualOverlays();
            string text =
                Header("probe_channel_open", "[PROBE CHANNEL OPEN]") + "\n\n" +
                T("line.category", "CATEGORY: {category}", L10n.Arg("category", LocalizeCategory(categoryId, category))) + "\n" +
                T("line.trace_count", "TRACE: {count}/{required}",
                    L10n.Arg("count", Mathf.Max(0, traceCount).ToString("00")),
                    L10n.Arg("required", Mathf.Max(1, requiredTraceCount).ToString("00"))) + "\n\n" +
                T("line.draw_related_object", "DRAW RELATED OBJECT");
            _terminalDisplay?.ShowText(text, instant);
        }

        public void ShowBootstrapSignalCapture(
            SemanticCardRecord card,
            string categoryId,
            string category,
            int previousTraceCount,
            int traceCount,
            int requiredTraceCount,
            FirstContactSemanticMapSnapshot beforeMapSnapshot,
            FirstContactSemanticMapSnapshot mapSnapshot,
            FirstContactSemanticSettings semanticSettings,
            bool accepted,
            bool becameStable,
            bool stable,
            FirstContactClusterFormationEvent clusterFormation = default,
            bool duplicate = false,
            bool instant = false)
        {
            _brainwaveDisplay?.Clear();
            ShowBootstrapResultMap(
                beforeMapSnapshot,
                mapSnapshot,
                semanticSettings,
                FirstContactSemanticMapLayout.BuildCardNodeId(card),
                BuildBootstrapCategoryNodeId(card?.BootstrapCategoryId),
                accepted,
                becameStable,
                clusterFormation);
            string label = card != null ? GetDisplayLabel(card) : string.Empty;
            string safeLabel = NormalizeTerminalLine(label, T("fallback.unknown", "UNKNOWN")).ToUpperInvariant();
            string safeCategory = LocalizeCategory(categoryId, category);
            bool rejected = !accepted && !duplicate;
            bool semanticClusterTrace = clusterFormation.BecameStable && clusterFormation.IsStable;
            string text;
            if (semanticClusterTrace)
            {
                text =
                    Header("cluster_trace", "[CLUSTER TRACE]") + "\n\n" +
                    T("line.probe_label", "PROBE LABEL: {label}", L10n.Arg("label", safeLabel)) + "\n" +
                    T("line.category", "CATEGORY: {category}", L10n.Arg("category", safeCategory)) + "\n" +
                    T("line.group", "GROUP: {group}", L10n.Arg("group", T("cluster.stable", "STABLE"))) + "\n" +
                    T("line.meaning", "MEANING: {meaning}", L10n.Arg("meaning", LocalizeMeaning(clusterFormation.Meaning))) +
                    BuildContinuePrompt();
            }
            else
            {
                text =
                    Header("signal_capture", "[SIGNAL CAPTURE]") + "\n\n" +
                    T("line.probe_label", "PROBE LABEL: {label}", L10n.Arg("label", safeLabel)) + "\n" +
                    T("line.category", "CATEGORY: {category}", L10n.Arg("category", safeCategory)) + "\n" +
                    T("line.group", "GROUP: {group}", L10n.Arg("group", rejected || duplicate ? T("cluster.unchanged", "UNCHANGED") : BuildGroupState(traceCount, requiredTraceCount, stable))) + "\n";
                if (rejected)
                {
                }
                else
                {
                    text += BuildTraceLine(previousTraceCount, traceCount, requiredTraceCount, accepted, duplicate);
                }
                if (!rejected && clusterFormation.IsIsolated)
                {
                    text += "\n" + T("line.trace_isolated", "TRACE: ISOLATED");
                }

                text += BuildContinuePrompt();
            }

            _terminalDisplay?.ShowText(text, instant);
        }

        public void ShowBootstrapClusterTrace(
            string categoryId,
            string category,
            int traceCount,
            int requiredTraceCount,
            string meaning,
            FirstContactSemanticMapSnapshot mapSnapshot,
            FirstContactSemanticSettings semanticSettings,
            bool instant = false)
        {
            _brainwaveDisplay?.Clear();
            ShowFullMap(mapSnapshot, semanticSettings);
            string text =
                Header("cluster_trace", "[CLUSTER TRACE]") + "\n\n" +
                T("line.category", "CATEGORY: {category}", L10n.Arg("category", LocalizeCategory(categoryId, category))) + "\n" +
                T("line.trace_count", "TRACE: {count}/{required}",
                    L10n.Arg("count", Mathf.Max(0, traceCount).ToString("00")),
                    L10n.Arg("required", Mathf.Max(1, requiredTraceCount).ToString("00"))) + "\n" +
                T("line.group", "GROUP: {group}", L10n.Arg("group", T("cluster.stable", "STABLE"))) + "\n" +
                T("line.meaning", "MEANING: {meaning}", L10n.Arg("meaning", LocalizeMeaning(categoryId, meaning))) +
                BuildContinuePrompt();
            _terminalDisplay?.ShowText(text, instant);
        }

        public void ShowBootstrapComplete(bool instant = false)
        {
            ClearVisualOverlays();
            string text =
                Header("bootstrap_complete", "[BOOTSTRAP COMPLETE]") + "\n\n" +
                T("line.translator_ready", "TRANSLATOR READY") + "\n" +
                T("line.meaning_map_seeded", "MEANING MAP SEEDED") +
                BuildContinuePrompt();
            _terminalDisplay?.ShowText(text, instant);
        }

        public void ShowProbeDispatching(
            FirstContactCardSource source,
            string unknownId,
            string label,
            string category,
            Texture probeTexture,
            BrainwaveSemanticProfile dispatchSignalProfile,
            int streamSeed,
            bool instant = false)
        {
            ClearVisualOverlays();
            ShowProbePreview(probeTexture, ProbePreviewLayout.Dispatch, scanActive: true);
            PlayProbeDispatchSignal(dispatchSignalProfile, streamSeed, completeLoop: false);
            _terminalDisplay?.ShowText(BuildProbeDispatchText(source, unknownId, label, category, accepted: false), instant);
        }

        public void ShowProbeDispatchAccepted(
            FirstContactCardSource source,
            string unknownId,
            string label,
            string category,
            Texture probeTexture,
            BrainwaveSemanticProfile dispatchSignalProfile,
            int streamSeed,
            bool instant = false)
        {
            ClearVisualOverlays();
            ShowProbePreview(probeTexture, ProbePreviewLayout.Dispatch, scanActive: false);
            PlayProbeDispatchSignal(dispatchSignalProfile, streamSeed, completeLoop: true);
            _terminalDisplay?.ShowText(BuildProbeDispatchText(source, unknownId, label, category, accepted: true), instant);
        }

        public void ShowProbeLabelEntry(
            FirstContactCardSource source,
            string unknownId,
            Texture probeTexture,
            string labelInput,
            string status,
            bool instant = false)
        {
            ClearVisualOverlays();
            ShowProbePreview(probeTexture);

            string renderedLabelInput = (labelInput ?? string.Empty) + TerminalDisplay.CursorMarker;
            string text =
                Header("probe_review", "[PROBE REVIEW]") + "\n\n" +
                T("line.image_captured", "IMAGE CAPTURED") + "\n" +
                T("line.probe_label", "PROBE LABEL: {label}", L10n.Arg("label", renderedLabelInput)) + "\n" +
                T("line.channel", "CHANNEL: {channel}", L10n.Arg("channel", BuildChannelLabel()));
            if (!string.IsNullOrWhiteSpace(status))
            {
                text += "\n" + T("line.input_status", "STATUS: {status}", L10n.Arg("status", status.Trim().ToUpperInvariant()));
            }

            text += "\n\n" +
                T("line.submit_enter", "SUBMIT: ENTER") + "\n" +
                T("line.redraw_escape", "REDRAW: ESC");
            _terminalDisplay?.ShowText(text, instant);
        }

        public void ShowInputRejected(string reason, int selectedIndex, bool instant = true)
        {
            ClearVisualOverlays();
            string text =
                Header("input_rejected", "[INPUT REJECTED]") + "\n\n" +
                $"{NormalizeTerminalLine(reason, T("reason.draw_one_object", "DRAW ONE OBJECT"))}\n\n" +
                BuildChoiceLine(0, selectedIndex, T("choice.redraw", "REDRAW")) +
                BuildSelectPrompt();
            _terminalDisplay?.ShowText(text, instant);
        }

        public void ShowAnalysisError(string status, int selectedIndex, bool instant = true)
        {
            ClearVisualOverlays();
            string safeStatus = string.IsNullOrWhiteSpace(status)
                ? T("status.analysis_unavailable", "ANALYSIS UNAVAILABLE")
                : status.Trim();
            string text =
                Header("analysis_error", "[ANALYSIS ERROR]") + "\n\n" +
                T("line.input_status", "STATUS: {status}", L10n.Arg("status", safeStatus)) + "\n\n" +
                BuildChoiceLine(0, selectedIndex, T("choice.redraw", "REDRAW")) +
                BuildChoiceLine(1, selectedIndex, T("choice.retry", "RETRY")) +
                BuildSelectPrompt();
            _terminalDisplay?.ShowText(text, instant);
        }

        private static string BuildChoiceLine(int choiceIndex, int selectedIndex, string label)
        {
            string marker = choiceIndex == selectedIndex ? ">" : " ";
            return $"{marker} {label}\n";
        }

        private static string BuildContinuePrompt()
        {
            return "\n\n" + T("prompt.continue", "PRESS ENTER TO CONTINUE") + TerminalDisplay.CursorMarker;
        }

        private static string BuildSelectPrompt()
        {
            return "\n" + T("prompt.select", "PRESS ENTER TO SELECT") + TerminalDisplay.CursorMarker;
        }

        private static string LocalizeCategory(string category)
        {
            return LocalizeCategory(category, category);
        }

        private static string LocalizeCategory(string categoryId, string category)
        {
            return FirstContactTerminalLocalization
                .LocalizeBootstrapCategory(categoryId, NormalizeTerminalLine(category, T("fallback.unknown", "UNKNOWN")))
                .ToUpperInvariant();
        }

        private static string LocalizeMeaning(string meaning)
        {
            return FirstContactTerminalLocalization
                .LocalizeMeaning(NormalizeTerminalLine(meaning, "[MEANING?]"))
                .ToUpperInvariant();
        }

        private static string LocalizeMeaning(string meaningId, string meaning)
        {
            return FirstContactTerminalLocalization
                .LocalizeMeaning(meaningId, NormalizeTerminalLine(meaning, "[MEANING?]"))
                .ToUpperInvariant();
        }

        private static string BuildChannelLabel()
        {
            return T("channel.probe_sequence", "PROBE SEQUENCE");
        }

        private static string BuildProbeDispatchText(
            FirstContactCardSource source,
            string unknownId,
            string label,
            string category,
            bool accepted)
        {
            string safeLabel = NormalizeTerminalLine(label, T("fallback.unknown", "UNKNOWN")).ToUpperInvariant();
            string text =
                Header("probe_dispatch", "[PROBE DISPATCH]") + "\n\n" +
                T("line.probe_label", "PROBE LABEL: {label}", L10n.Arg("label", safeLabel)) + "\n";

            if (!string.IsNullOrWhiteSpace(category))
            {
                text += T("line.category", "CATEGORY: {category}", L10n.Arg("category", LocalizeCategory(category))) + "\n";
            }
            else
            {
                text += T("line.channel", "CHANNEL: {channel}", L10n.Arg("channel", BuildChannelLabel())) + "\n";
            }

            text += accepted
                ? T("line.probe_check_passed", "PROBE CHECK: PASSED") + "\n" +
                  T("line.response_channel_open", "RESPONSE CHANNEL: OPEN")
                : T("line.probe_check_in_progress", "PROBE CHECK: IN PROGRESS") + "\n" +
                  T("line.response_channel_waiting", "RESPONSE CHANNEL: WAITING");
            return text;
        }

        private static string NormalizeTerminalLine(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }

        private static string GetDisplayLabel(SemanticCardRecord card)
        {
            if (card == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(card.LocalizedLabel)
                ? ResolveDynamicLabelFallback(card.Label).ToUpperInvariant()
                : card.LocalizedLabel.Trim().ToUpperInvariant();
        }

        private static string BuildMemberLine(SemanticClusterRecord cluster)
        {
            if (cluster == null || cluster.Members.Count == 0)
            {
                return string.Empty;
            }

            int start = Mathf.Max(0, cluster.Members.Count - 4);
            string line = string.Empty;
            for (int i = start; i < cluster.Members.Count; i++)
            {
                SemanticCardRecord member = cluster.Members[i];
                if (member == null)
                {
                    continue;
                }

                string label = !string.IsNullOrWhiteSpace(member.LocalizedLabel)
                    ? member.LocalizedLabel.Trim()
                    : ResolveDynamicLabelFallback(member.Label);
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                if (line.Length > 0)
                {
                    line += " / ";
                }

                line += label.ToUpperInvariant();
            }

            return line;
        }

        private static string BuildGroupState(int traceCount, int requiredTraceCount, bool stable)
        {
            if (stable)
            {
                return T("cluster.stable", "STABLE");
            }

            if (traceCount >= Mathf.Max(1, requiredTraceCount))
            {
                return T("cluster.unstable", "UNSTABLE");
            }

            return traceCount > 0
                ? T("cluster.forming", "FORMING")
                : T("cluster.unstable", "UNSTABLE");
        }

        private void ShowMiniMap(
            FirstContactSemanticMapSnapshot mapSnapshot,
            FirstContactSemanticSettings semanticSettings)
        {
            ClearProbePreview();
            if (semanticSettings != null && !semanticSettings.showSemanticMapFeedback)
            {
                _semanticMapDisplay?.Clear();
                return;
            }

            if (mapSnapshot == null || mapSnapshot.Nodes.Count == 0)
            {
                _semanticMapDisplay?.Clear();
                return;
            }

            _semanticMapDisplay?.ShowMiniMap(mapSnapshot);
        }

        private void ShowFullMap(
            FirstContactSemanticMapSnapshot mapSnapshot,
            FirstContactSemanticSettings semanticSettings)
        {
            ClearProbePreview();
            if (semanticSettings != null && !semanticSettings.showSemanticMapFeedback)
            {
                _semanticMapDisplay?.Clear();
                return;
            }

            if (mapSnapshot == null || mapSnapshot.Nodes.Count == 0)
            {
                _semanticMapDisplay?.Clear();
                return;
            }

            _semanticMapDisplay?.ShowFullMap(mapSnapshot);
        }

        private void ShowBootstrapResultMap(
            FirstContactSemanticMapSnapshot beforeMapSnapshot,
            FirstContactSemanticMapSnapshot mapSnapshot,
            FirstContactSemanticSettings semanticSettings,
            string activeCardNodeId,
            string categoryNodeId,
            bool accepted,
            bool becameStable,
            FirstContactClusterFormationEvent clusterFormation)
        {
            ClearProbePreview();
            if (semanticSettings != null && !semanticSettings.showSemanticMapFeedback)
            {
                _semanticMapDisplay?.Clear();
                return;
            }

            if (mapSnapshot == null || mapSnapshot.Nodes.Count == 0)
            {
                _semanticMapDisplay?.Clear();
                return;
            }

            if (clusterFormation.ShouldAnimate)
            {
                _semanticMapDisplay?.ShowClusterFormationTransition(
                    beforeMapSnapshot,
                    mapSnapshot,
                    clusterFormation);
                return;
            }

            _semanticMapDisplay?.ShowBootstrapResultTransition(
                beforeMapSnapshot,
                mapSnapshot,
                activeCardNodeId,
                categoryNodeId,
                accepted,
                becameStable);
        }

        private void ShowSemanticAnalysisMap(
            FirstContactSemanticMapSnapshot beforeMapSnapshot,
            FirstContactSemanticMapSnapshot mapSnapshot,
            FirstContactSemanticSettings semanticSettings,
            FirstContactClusterFormationEvent clusterFormation)
        {
            ClearProbePreview();
            if (semanticSettings != null && !semanticSettings.showSemanticMapFeedback)
            {
                _semanticMapDisplay?.Clear();
                return;
            }

            if (mapSnapshot == null || mapSnapshot.Nodes.Count == 0)
            {
                _semanticMapDisplay?.Clear();
                return;
            }

            if (clusterFormation.ShouldAnimate)
            {
                _semanticMapDisplay?.ShowClusterFormationTransition(
                    beforeMapSnapshot,
                    mapSnapshot,
                    clusterFormation);
                return;
            }

            _semanticMapDisplay?.ShowFullMap(mapSnapshot);
        }

        private static string BuildTraceLine(
            int previousTraceCount,
            int traceCount,
            int requiredTraceCount,
            bool accepted,
            bool duplicate = false)
        {
            if (duplicate)
            {
                return T("line.trace_duplicate", "TRACE: DUPLICATE");
            }

            string required = Mathf.Max(1, requiredTraceCount).ToString("00");
            string current = Mathf.Max(0, traceCount).ToString("00");
            if (accepted && previousTraceCount != traceCount)
            {
                return T(
                    "line.trace_count_transition",
                    "TRACE: {from}/{required} -> {to}/{required}",
                    L10n.Arg("from", Mathf.Max(0, previousTraceCount).ToString("00")),
                    L10n.Arg("to", current),
                    L10n.Arg("required", required));
            }

            return T(
                "line.trace_count",
                "TRACE: {count}/{required}",
                L10n.Arg("count", current),
                L10n.Arg("required", required));
        }

        private static string BuildBootstrapCategoryNodeId(string categoryId)
        {
            return string.IsNullOrWhiteSpace(categoryId)
                ? string.Empty
                : $"B:{categoryId.Trim()}";
        }

        private static string T(string key, string fallback, params L10nArg[] args)
        {
            return L10n.T($"first_contact.terminal.{key}", fallback, args);
        }

        private static string Header(string key, string fallback)
        {
            return T($"header.{key}", fallback);
        }

        private static string SignalColor(string key, string fallback)
        {
            return T($"color.{key}", fallback);
        }

        private static string ResolveDynamicLabelFallback(string fallbackLabel)
        {
            if (LlmLocalizationSettings.IsEnglishLocale(L10n.CurrentLocale))
            {
                return string.IsNullOrWhiteSpace(fallbackLabel) ? "UNKNOWN" : fallbackLabel.Trim();
            }

            return T("fallback.unknown", "UNKNOWN");
        }

        private void ClearVisualOverlays()
        {
            ClearProbePreview();
            _brainwaveDisplay?.Clear();
            _semanticMapDisplay?.Clear();
        }

        private void ShowProbePreview(Texture probeTexture)
        {
            ShowProbePreview(probeTexture, ProbePreviewLayout.Review, scanActive: false);
        }

        private void ShowProbePreview(Texture probeTexture, ProbePreviewLayout layout, bool scanActive)
        {
            if (_terminalDisplay == null ||
                probeTexture == null ||
                _probePreviewDisplay == null ||
                !_probePreviewDisplay.Show(
                    probeTexture,
                    layout == ProbePreviewLayout.Dispatch,
                    scanActive))
            {
                return;
            }

            _terminalDisplay.SetContentTopInsetNormalized(GetProbePreviewTextTopInset(layout));
        }

        private void ClearProbePreview()
        {
            _probePreviewDisplay?.Clear();
        }

        private float GetProbePreviewTextTopInset(ProbePreviewLayout layout)
        {
            if (_presentationSettings == null)
            {
                return layout == ProbePreviewLayout.Dispatch ? 0f : 0.52f;
            }

            return layout == ProbePreviewLayout.Dispatch
                ? Mathf.Clamp01(_presentationSettings.probeDispatchTextTopInset)
                : Mathf.Clamp01(_presentationSettings.probeReviewTextTopInset);
        }

        private void PlayProbeDispatchSignal(
            BrainwaveSemanticProfile dispatchSignalProfile,
            int streamSeed,
            bool completeLoop)
        {
            if (_brainwaveDisplay == null)
            {
                return;
            }

            int safeSeed = streamSeed != 0
                ? streamSeed
                : (dispatchSignalProfile.IsValid ? dispatchSignalProfile.TextureSeed : 1);
            _brainwaveDisplay.BeginReceiverStream(safeSeed);
            if (dispatchSignalProfile.IsValid)
            {
                _brainwaveDisplay.InjectReceiverSignal(
                    dispatchSignalProfile,
                    BrainwaveSignalRole.Drawing,
                    completeLoop ? 0.45f : 0.95f,
                    completeLoop ? 0.6f : 0.9f);
            }

            if (completeLoop)
            {
                _brainwaveDisplay.CompleteReceiverSequenceLoop();
            }
        }

        private static FirstContactSemanticMapDisplay ResolveSemanticMapDisplay(TerminalDisplay terminalDisplay)
        {
            if (terminalDisplay == null)
            {
                return null;
            }

            FirstContactSemanticMapDisplay display =
                terminalDisplay.GetComponent<FirstContactSemanticMapDisplay>() ??
                terminalDisplay.GetComponentInChildren<FirstContactSemanticMapDisplay>(true);
            return display != null
                ? display
                : terminalDisplay.gameObject.AddComponent<FirstContactSemanticMapDisplay>();
        }

        private static FirstContactProbePreviewDisplay ResolveProbePreviewDisplay(
            TerminalDisplay terminalDisplay,
            FirstContactPresentationSettings presentationSettings)
        {
            if (terminalDisplay == null)
            {
                return null;
            }

            FirstContactProbePreviewDisplay display =
                terminalDisplay.GetComponent<FirstContactProbePreviewDisplay>() ??
                terminalDisplay.GetComponentInChildren<FirstContactProbePreviewDisplay>(true);
            return display != null
                ? display
                : FirstContactProbePreviewDisplay.CreateRuntime(
                    terminalDisplay.ScreenRectTransform,
                    presentationSettings);
        }

        private enum ProbePreviewLayout
        {
            Review,
            Dispatch
        }
    }

}
