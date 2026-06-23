using System;
using System.Collections.Generic;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Localization;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactTerminalPresenter
    {
        public static string MeaningMapActionLabel => T("choice.meaning_map", "MEANING MAP");
        public static string AnswerActionLabel => T("choice.send_reply", "SEND REPLY");
        public static string ProbeLabelInputPrefix => T("line.probe_label_input_prefix", "PROBE LABEL: ");

        private readonly TerminalDisplay _terminalDisplay;
        private readonly TerminalBrainwaveDisplay _brainwaveDisplay;
        private readonly FirstContactSemanticMapDisplay _semanticMapDisplay;
        private readonly FirstContactDebugSettings _debugSettings;
        private readonly FirstContactPresentationSettings _presentationSettings;
        private GameObject _probePreviewRoot;
        private RawImage _probePreviewImage;
        private AspectRatioFitter _probePreviewAspect;
        private FirstContactProbePreviewScanline _probePreviewScanline;
        private bool _hasShownQuestionOnce;
        private int _lastIncomingTransmissionTextLength;

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
            _semanticMapDisplay = ResolveSemanticMapDisplay(terminalDisplay);
            _debugSettings = debugSettings;
            _presentationSettings = presentationSettings != null
                ? presentationSettings
                : ScriptableObject.CreateInstance<FirstContactPresentationSettings>();
        }

        public void Clear()
        {
            ClearVisualOverlays();
            _terminalDisplay?.Clear();
            _hasShownQuestionOnce = false;
        }

        public void ShowQuestion(AlienQuestion question, bool instant = false, string fallbackReason = null)
        {
            ClearVisualOverlays();
            _terminalDisplay?.ShowText(
                BuildQuestionText(question, fallbackReason),
                instant);
            _hasShownQuestionOnce = true;
        }

        public void BeginIncomingTransmissionStream(int streamSeed)
        {
            _semanticMapDisplay?.Clear(resetTerminalInset: false);
            _brainwaveDisplay?.BeginReceiverStream(streamSeed);
            _hasShownQuestionOnce = true;
            _lastIncomingTransmissionTextLength = 0;
        }

        public void CompleteIncomingTransmissionStream()
        {
            _brainwaveDisplay?.CompleteReceiverSequenceLoop();
        }

        public void ShowIncomingTransmissionToken(
            AlienQuestion question,
            int tokenIndex,
            bool isUnknownToken,
            BrainwaveSemanticProfile signalProfile,
            float signalDuration,
            float signalIntensity,
            bool instant = true)
        {
            _semanticMapDisplay?.Clear(resetTerminalInset: false);
            string[] displayTokens = question?.BuildDisplayTokens() ?? Array.Empty<string>();
            int tokenCount = displayTokens.Length;
            int activeIndex = tokenCount > 0
                ? Mathf.Clamp(tokenIndex, 0, tokenCount - 1)
                : -1;
            string text = BuildIncomingTransmissionBaseText(displayTokens, activeIndex);
            int visibleCharacterCount = BuildIncomingTransmissionVisibleCharacterCount(displayTokens, activeIndex);
            if (instant)
            {
                _terminalDisplay?.ShowText(text, instant: true);
            }
            else
            {
                _terminalDisplay?.ShowTextWithTypedSuffix(text, visibleCharacterCount);
            }

            _lastIncomingTransmissionTextLength = text.Length;
            if (_brainwaveDisplay != null && signalProfile.IsValid)
            {
                _brainwaveDisplay.InjectReceiverSignal(
                    signalProfile,
                    isUnknownToken ? BrainwaveSignalRole.UnknownToken : BrainwaveSignalRole.AlienToken,
                    signalDuration,
                    signalIntensity);
            }

            _hasShownQuestionOnce = true;
        }

        public void ShowIncomingTransmissionChoices(
            AlienQuestion question,
            int selectedIndex,
            string initialProbeUnknownId,
            bool instant = true)
        {
            _semanticMapDisplay?.Clear(resetTerminalInset: false);
            string[] displayTokens = question?.BuildDisplayTokens() ?? Array.Empty<string>();
            int finalTokenIndex = displayTokens.Length > 0 ? displayTokens.Length - 1 : -1;
            string text = BuildIncomingTransmissionBaseText(displayTokens, finalTokenIndex);

            string traceId = FirstContactUnknownSlotDefinition.NormalizeUnknownId(initialProbeUnknownId);
            if (!string.IsNullOrWhiteSpace(initialProbeUnknownId))
            {
                text += "\n\n";
                text += T("line.trace", "TRACE: {trace}", L10n.Arg("trace", traceId)) + "\n";
                text += T("line.status_signal_buffered", "STATUS: SIGNAL BUFFERED") + "\n";
                text += T("line.unknown_meaning_signal_color", "UNKNOWN MEANING SIGNAL: {color}", L10n.Arg("color", SignalColor("red", "RED")));
            }

            text += "\n\n";
            text += Header("response_channel_ready", "[RESPONSE CHANNEL READY]") + "\n\n";
            text += BuildQuestionChoiceBlock(question, selectedIndex, initialProbeUnknownId);

            if (instant || _lastIncomingTransmissionTextLength <= 0)
            {
                _terminalDisplay?.ShowText(text, instant);
            }
            else
            {
                _terminalDisplay?.ShowTextWithTypedSuffix(
                    text,
                    Mathf.Min(_lastIncomingTransmissionTextLength, text.Length));
            }

            _hasShownQuestionOnce = true;
        }

        public void ShowQuestionChoices(
            AlienQuestion question,
            int selectedIndex,
            FirstContactSemanticMapSnapshot mapSnapshot,
            FirstContactSemanticSettings semanticSettings,
            bool instant = true,
            string fallbackReason = null,
            string initialProbeUnknownId = null)
        {
            _brainwaveDisplay?.Clear();
            ShowMiniMap(mapSnapshot, semanticSettings);
            string text = BuildQuestionText(question, fallbackReason);
            text += "\n\n" + Header("response_channel_ready", "[RESPONSE CHANNEL READY]") + "\n\n";
            text += BuildQuestionChoiceBlock(question, selectedIndex, initialProbeUnknownId);

            _terminalDisplay?.ShowText(text, instant);
            _hasShownQuestionOnce = true;
        }

        public void ShowSemanticMapChoices(
            AlienQuestion question,
            int selectedIndex,
            FirstContactSemanticMapSnapshot mapSnapshot,
            FirstContactSemanticSettings semanticSettings,
            bool instant = true)
        {
            _brainwaveDisplay?.Clear();
            ShowFullMap(mapSnapshot, semanticSettings);

            string text =
                Header("meaning_map", "[MEANING MAP]") + "\n\n" +
                T("meaning_map.drawing_reference", "DRAWING REFERENCE") + "\n\n";

            int choiceIndex = 0;
            text += BuildChoiceLine(choiceIndex, selectedIndex, T("choice.back_to_request", "BACK TO REQUEST"));
            choiceIndex++;

            if (question != null)
            {
                for (int i = 0; i < question.UnknownSlots.Count; i++)
                {
                    string id = question.UnknownSlots[i].Id;
                    text += BuildChoiceLine(choiceIndex, selectedIndex, BuildProbeActionLabel(id));
                    choiceIndex++;
                }
            }

            text += BuildChoiceLine(choiceIndex, selectedIndex, AnswerActionLabel);
            text += BuildSelectPrompt();
            _terminalDisplay?.ShowText(text, instant);
            _hasShownQuestionOnce = true;
        }

        public void ShowQuestionChoiceEcho(AlienQuestion question, string choiceLabel, bool instant = false)
        {
            ClearVisualOverlays();
            string text = BuildQuestionText(question);
            text += "\n\n" + Header("input_accepted", "[INPUT ACCEPTED]") + "\n\n";
            text += $"> {choiceLabel ?? string.Empty}";
            _terminalDisplay?.ShowText(text, instant);
            _hasShownQuestionOnce = true;
        }

        public void ShowTabletLinkOpen(
            AlienQuestion question,
            FirstContactCardSource source,
            string unknownId,
            bool instant = false)
        {
            ClearVisualOverlays();
            string text = BuildQuestionText(question);
            text += "\n\n" + Header("tablet_link_open", "[TABLET LINK OPEN]") + "\n\n";
            text += T("line.channel", "CHANNEL: {channel}", L10n.Arg("channel", BuildChannelLabel(source, unknownId))) + "\n";
            text += T("line.draw_one_object", "DRAW ONE OBJECT") + "\n\n";
            text += T("line.submit_enter", "SUBMIT: ENTER") + TerminalDisplay.CursorMarker;
            _terminalDisplay?.ShowText(text, instant);
            _hasShownQuestionOnce = true;
        }

        public void ShowProbeChannelOpen(
            FirstContactCardSource source,
            string unknownId,
            bool instant = false)
        {
            ClearVisualOverlays();
            bool isAnswer = source == FirstContactCardSource.Answer;
            string text =
                (isAnswer ? Header("reply_channel_open", "[REPLY CHANNEL OPEN]") : Header("probe_channel_open", "[PROBE CHANNEL OPEN]")) + "\n\n" +
                T("line.channel", "CHANNEL: {channel}", L10n.Arg("channel", BuildChannelLabel(source, unknownId))) + "\n" +
                T("line.draw_one_simple_object", "DRAW ONE SIMPLE OBJECT");
            _terminalDisplay?.ShowText(text, instant);
            _hasShownQuestionOnce = true;
        }

        public void ShowBootstrapProbeSequence(
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
                T("line.category", "CATEGORY: {category}", L10n.Arg("category", LocalizeCategory(category))) + "\n" +
                T("line.group", "GROUP: {group}", L10n.Arg("group", BuildGroupState(traceCount, requiredTraceCount, stable))) + "\n" +
                T("line.trace_count", "TRACE: {count}/{required}",
                    L10n.Arg("count", Mathf.Max(0, traceCount).ToString("00")),
                    L10n.Arg("required", Mathf.Max(1, requiredTraceCount).ToString("00"))) + "\n\n" +
                BuildChoiceLine(0, selectedIndex, T("choice.draw_related_object", "DRAW RELATED OBJECT")) +
                BuildSelectPrompt();
            _terminalDisplay?.ShowText(text, instant);
            _hasShownQuestionOnce = true;
        }

        public void ShowBootstrapProbeChannelOpen(
            string category,
            int traceCount,
            int requiredTraceCount,
            bool instant = false)
        {
            ClearVisualOverlays();
            string text =
                Header("probe_channel_open", "[PROBE CHANNEL OPEN]") + "\n\n" +
                T("line.category", "CATEGORY: {category}", L10n.Arg("category", LocalizeCategory(category))) + "\n" +
                T("line.trace_count", "TRACE: {count}/{required}",
                    L10n.Arg("count", Mathf.Max(0, traceCount).ToString("00")),
                    L10n.Arg("required", Mathf.Max(1, requiredTraceCount).ToString("00"))) + "\n\n" +
                T("line.draw_related_object", "DRAW RELATED OBJECT");
            _terminalDisplay?.ShowText(text, instant);
            _hasShownQuestionOnce = true;
        }

        public void ShowBootstrapSignalCapture(
            SemanticCardRecord card,
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
            string safeCategory = LocalizeCategory(category);
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
                    T("line.group", "GROUP: {group}", L10n.Arg("group", duplicate ? T("cluster.unchanged", "UNCHANGED") : BuildGroupState(traceCount, requiredTraceCount, stable))) + "\n" +
                    BuildTraceLine(previousTraceCount, traceCount, requiredTraceCount, accepted, duplicate);
                if (clusterFormation.IsIsolated)
                {
                    text += "\n" + T("line.trace_isolated", "TRACE: ISOLATED");
                }

                text += BuildContinuePrompt();
            }

            _terminalDisplay?.ShowText(text, instant);
        }

        public void ShowBootstrapClusterTrace(
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
                T("line.category", "CATEGORY: {category}", L10n.Arg("category", LocalizeCategory(category))) + "\n" +
                T("line.trace_count", "TRACE: {count}/{required}",
                    L10n.Arg("count", Mathf.Max(0, traceCount).ToString("00")),
                    L10n.Arg("required", Mathf.Max(1, requiredTraceCount).ToString("00"))) + "\n" +
                T("line.group", "GROUP: {group}", L10n.Arg("group", T("cluster.stable", "STABLE"))) + "\n" +
                T("line.meaning", "MEANING: {meaning}", L10n.Arg("meaning", LocalizeMeaning(meaning))) +
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
                T("line.channel", "CHANNEL: {channel}", L10n.Arg("channel", BuildChannelLabel(source, unknownId)));
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

        public void ShowLabelReview(
            FirstContactCardSource source,
            string unknownId,
            string label,
            int selectedIndex,
            bool instant = true)
        {
            ClearVisualOverlays();
            string text =
                Header("probe_label", "[PROBE LABEL]") + "\n\n" +
                $"{NormalizeTerminalLine(label, T("fallback.unknown", "UNKNOWN")).ToUpperInvariant()}\n\n" +
                T("line.channel", "CHANNEL: {channel}", L10n.Arg("channel", BuildChannelLabel(source, unknownId))) + "\n\n" +
                BuildChoiceLine(0, selectedIndex, T("choice.submit", "SUBMIT")) +
                BuildChoiceLine(1, selectedIndex, T("choice.redraw", "REDRAW")) +
                BuildSelectPrompt();
            _terminalDisplay?.ShowText(text, instant);
        }

        public void ShowDecodeTarget(AlienQuestion question, string unknownId)
        {
            ClearVisualOverlays();
            string line = question?.BuildDisplayLine() ?? string.Empty;
            string id = FirstContactUnknownSlotDefinition.NormalizeUnknownId(unknownId);
            string text = $"{Header("probe_sample", "[PROBE SAMPLE]")}\n\n{line}\n\n<{id}>";
            _terminalDisplay?.ShowText(text);
        }

        public void ShowCard(SemanticCardRecord card, SemanticClusterRecord cluster, bool instant = false)
        {
            if (card == null)
            {
                return;
            }

            _semanticMapDisplay?.Clear();
            string clusterLine = cluster != null ? $"[{cluster.Id}]" : Header("no_cluster", "[NO CLUSTER]");
            string text =
                Header("signal_capture", "[SIGNAL CAPTURE]") + "\n\n" +
                T("line.visual_probe_signal_color", "VISUAL PROBE SIGNAL: {color}", L10n.Arg("color", SignalColor("cyan", "CYAN"))) + "\n\n" +
                T("line.probe_label", "PROBE LABEL: {label}", L10n.Arg("label", GetDisplayLabel(card))) + "\n\n" +
                $"{clusterLine}";

            if (_debugSettings != null && _debugSettings.showScoresOnTerminal && !string.IsNullOrWhiteSpace(card.TargetUnknownId))
            {
                text += "\n" + T("line.target", "TARGET: {target}", L10n.Arg("target", card.TargetUnknownId));
            }

            text += BuildContinuePrompt();
            _terminalDisplay?.ShowText(text, instant);
            if (_brainwaveDisplay != null && card.WaveformProfile.IsValid)
            {
                _brainwaveDisplay.PlaySignal(card.WaveformProfile);
            }
        }

        public void ShowCluster(SemanticClusterRecord cluster)
        {
            if (cluster == null)
            {
                return;
            }

            ClearVisualOverlays();
            string members = BuildMemberLine(cluster);
            string text =
                $"[{cluster.Id}]\n" +
                $"{members}\n\n" +
                $"{LocalizeMeaning(cluster.DisplayName)}";
            _terminalDisplay?.ShowText(text);
        }

        public void ShowSemanticAnalysis(
            SemanticCardRecord card,
            SemanticClusterRecord cluster,
            IReadOnlyList<FirstContactSlotScore> slotScores,
            FirstContactResolutionResult? activeResolution,
            FirstContactSemanticMapSnapshot mapSnapshot,
            FirstContactSemanticSettings semanticSettings,
            BrainwaveSemanticProfile unknownSignalProfile = default,
            FirstContactSemanticMapSnapshot beforeMapSnapshot = null,
            FirstContactClusterFormationEvent clusterFormation = default,
            bool instant = false)
        {
            ShowSemanticAnalysisMap(
                beforeMapSnapshot,
                mapSnapshot,
                semanticSettings,
                clusterFormation);
            string label = card != null ? GetDisplayLabel(card) : string.Empty;
            bool isProbeResult = activeResolution.HasValue && activeResolution.Value.Slot != null;
            bool isClusterTrace = clusterFormation.BecameStable && cluster != null && cluster.IsStable;
            string text = isClusterTrace
                ? Header("cluster_trace", "[CLUSTER TRACE]") + "\n\n"
                : isProbeResult
                ? Header("probe_result", "[PROBE RESULT]") + "\n\n"
                : Header("reply_signal", "[REPLY SIGNAL]") + "\n\n";
            if (!isClusterTrace && isProbeResult)
            {
                text += T("line.unknown_meaning_signal_color", "UNKNOWN MEANING SIGNAL: {color}", L10n.Arg("color", SignalColor("red", "RED"))) + "\n";
                text += T("line.visual_probe_signal_color", "VISUAL PROBE SIGNAL: {color}", L10n.Arg("color", SignalColor("cyan", "CYAN"))) + "\n\n";
            }

            text += T("line.probe_label", "PROBE LABEL: {label}", L10n.Arg("label", NormalizeTerminalLine(label, T("fallback.unknown", "UNKNOWN")).ToUpperInvariant())) + "\n";

            if (isClusterTrace)
            {
                text += T("line.group", "GROUP: {group}", L10n.Arg("group", T("cluster.stable", "STABLE"))) + "\n";
                text += T("line.meaning", "MEANING: {meaning}", L10n.Arg("meaning", LocalizeMeaning(cluster.DisplayName))) + "\n";
            }
            else if (isProbeResult)
            {
                FirstContactResolutionResult result = activeResolution.Value;
                text += T("line.translation_alignment", "TRANSLATION ALIGNMENT: {alignment}", L10n.Arg("alignment", BuildSignalMatchLabel(result.Score, semanticSettings))) + "\n";
                text += BuildTokenUpdateBlock(result);
            }
            else
            {
                text += T("line.translation_alignment", "TRANSLATION ALIGNMENT: {alignment}", L10n.Arg("alignment", T("alignment.stored", "STORED"))) + "\n";
            }

            if (!isClusterTrace && clusterFormation.IsIsolated)
            {
                text += T("line.group", "GROUP: {group}", L10n.Arg("group", T("cluster.forming", "FORMING"))) + "\n";
                text += T("line.trace_isolated", "TRACE: ISOLATED") + "\n";
            }
            else if (!isClusterTrace && clusterFormation.HasCluster && !clusterFormation.IsStable && clusterFormation.MemberCount > 1)
            {
                text += T("line.group", "GROUP: {group}", L10n.Arg("group", T("cluster.forming", "FORMING"))) + "\n";
            }

            if (!isClusterTrace && isProbeResult && cluster != null && cluster.IsStable)
            {
                text += "\n" + T("line.memory_map_updated", "MEMORY MAP: {name} UPDATED", L10n.Arg("name", LocalizeMeaning(cluster.DisplayName))) + "\n";
            }

            if (_debugSettings != null && _debugSettings.showScoresOnTerminal && cluster != null)
            {
                string stability = cluster.IsStable ? T("cluster.stable", "STABLE") : T("cluster.forming", "FORMING");
                text += T("line.cluster_status", "CLUSTER: {cluster} / {status}", L10n.Arg("cluster", cluster.Id), L10n.Arg("status", stability)) + "\n";
            }

            if (_debugSettings != null && _debugSettings.showScoresOnTerminal)
            {
                text += BuildSlotScoreBlock(slotScores, semanticSettings);
            }

            text += BuildContinuePrompt();

            _terminalDisplay?.ShowText(text, instant);
            if (_brainwaveDisplay != null && card != null && card.WaveformProfile.IsValid)
            {
                if (isProbeResult && unknownSignalProfile.IsValid)
                {
                    _brainwaveDisplay.PlayComparisonCapture(unknownSignalProfile, card.WaveformProfile);
                }
                else
                {
                    _brainwaveDisplay.PlaySignal(card.WaveformProfile);
                }
            }
        }

        public void ShowAnswerTransmitted(SemanticCardRecord card, bool instant = false)
        {
            ClearVisualOverlays();
            string label = card != null ? GetDisplayLabel(card) : string.Empty;
            string text =
                Header("reply_sent", "[REPLY SENT]") + "\n\n" +
                T("line.visual_response", "VISUAL RESPONSE: {label}", L10n.Arg("label", NormalizeTerminalLine(label, T("fallback.unknown", "UNKNOWN")).ToUpperInvariant())) + "\n" +
                T("line.transmission_status_delivered", "TRANSMISSION STATUS: DELIVERED");
            _terminalDisplay?.ShowText(text, instant);
        }

        private string BuildQuestionText(AlienQuestion question, string fallbackReason = null)
        {
            string text = $"{Header("translation_buffer", "[TRANSLATION BUFFER]")}\n\n{question?.BuildDisplayLine() ?? string.Empty}";
            if (_debugSettings != null && _debugSettings.showQuestionFallbackReason && !string.IsNullOrWhiteSpace(fallbackReason))
            {
                text += $"\n\n{Header("fallback", "[FALLBACK]")}\n{fallbackReason}";
            }

            return text + BuildSelectPrompt();
        }

        private static string BuildQuestionChoiceBlock(
            AlienQuestion question,
            int selectedIndex,
            string initialProbeUnknownId)
        {
            string text = string.Empty;
            int choiceIndex = 0;
            string initialProbeId = FirstContactUnknownSlotDefinition.NormalizeUnknownId(initialProbeUnknownId);
            if (!string.IsNullOrWhiteSpace(initialProbeUnknownId))
            {
                text += BuildChoiceLine(choiceIndex, selectedIndex, BuildProbeActionLabel(initialProbeId));
                choiceIndex++;
            }
            else
            {
                if (question != null)
                {
                    for (int i = 0; i < question.UnknownSlots.Count; i++)
                    {
                        string id = question.UnknownSlots[i].Id;
                        text += BuildChoiceLine(choiceIndex, selectedIndex, BuildProbeActionLabel(id));
                        choiceIndex++;
                    }
                }

                text += BuildChoiceLine(choiceIndex, selectedIndex, MeaningMapActionLabel);
                choiceIndex++;
                text += BuildChoiceLine(choiceIndex, selectedIndex, AnswerActionLabel);
                choiceIndex++;
            }

            return text;
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

        private static string BuildChannelLabel(FirstContactCardSource source, string unknownId)
        {
            if (source == FirstContactCardSource.Answer)
            {
                return AnswerActionLabel;
            }

            if (source == FirstContactCardSource.BootstrapProbe)
            {
                return T("channel.probe_sequence", "PROBE SEQUENCE");
            }

            return BuildProbeActionLabel(unknownId);
        }

        public static string BuildProbeActionLabel(string unknownId)
        {
            string id = FirstContactUnknownSlotDefinition.NormalizeUnknownId(unknownId);
            return string.IsNullOrWhiteSpace(id)
                ? T("choice.probe_unknown", "PROBE UNKNOWN")
                : T("choice.probe_id", "PROBE {id}", L10n.Arg("id", id));
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
                text += T("line.channel", "CHANNEL: {channel}", L10n.Arg("channel", BuildChannelLabel(source, unknownId))) + "\n";
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

        private static string BuildIncomingBufferLine(string[] displayTokens, int activeIndex)
        {
            if (displayTokens == null || displayTokens.Length == 0 || activeIndex < 0)
            {
                return "_";
            }

            int lastVisibleIndex = Mathf.Clamp(activeIndex, 0, displayTokens.Length - 1);
            string line = string.Empty;
            for (int i = 0; i <= lastVisibleIndex; i++)
            {
                if (line.Length > 0)
                {
                    line += " / ";
                }

                string token = NormalizeTerminalLine(displayTokens[i], "<?>");
                line += FirstContactTerminalLocalization.LocalizeToken(token);
            }

            return line;
        }

        private static string BuildIncomingTransmissionBaseText(string[] displayTokens, int activeIndex)
        {
            return
                Header("incoming_transmission", "[INCOMING TRANSMISSION]") + "\n\n" +
                T("line.status_receiving_signal", "STATUS: RECEIVING SIGNAL") + "\n" +
                T("line.translation_buffer_label", "TRANSLATION BUFFER:") + "\n" +
                BuildIncomingBufferLine(displayTokens, activeIndex);
        }

        private static int BuildIncomingTransmissionVisibleCharacterCount(string[] displayTokens, int activeIndex)
        {
            string prefix =
                Header("incoming_transmission", "[INCOMING TRANSMISSION]") + "\n\n" +
                T("line.status_receiving_signal", "STATUS: RECEIVING SIGNAL") + "\n" +
                T("line.translation_buffer_label", "TRANSLATION BUFFER:") + "\n";

            if (displayTokens == null || displayTokens.Length == 0 || activeIndex < 0)
            {
                return prefix.Length;
            }

            int lastVisibleIndex = Mathf.Clamp(activeIndex, 0, displayTokens.Length - 1);
            if (lastVisibleIndex <= 0)
            {
                return prefix.Length;
            }

            return prefix.Length + BuildIncomingBufferLine(displayTokens, lastVisibleIndex - 1).Length + " / ".Length;
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

        private static string BuildTokenUpdateBlock(FirstContactResolutionResult result)
        {
            if (result.Slot == null)
            {
                return string.Empty;
            }

            string text =
                "\n" + T("line.translation_update", "TRANSLATION UPDATE") + "\n" +
                T("line.meaning", "MEANING: {meaning}", L10n.Arg("meaning", result.Slot.Id)) + "\n";

            if (!result.Changed)
            {
                return text + T("line.no_change", "NO CHANGE") + "\n";
            }

            FirstContactStageTexts stageTexts = result.Slot.Definition?.stageTexts ?? new FirstContactStageTexts();
            string before = stageTexts.GetDisplayText(result.PreviousStage, result.Slot.Id);
            string after = stageTexts.GetDisplayText(result.NewStage, result.Slot.Id);
            return text +
                   $"{FirstContactTerminalLocalization.LocalizeToken(before)} -> {FirstContactTerminalLocalization.LocalizeToken(after)}\n";
        }

        private static string LocalizeCategory(string category)
        {
            return FirstContactTerminalLocalization
                .LocalizeBootstrapCategory(NormalizeTerminalLine(category, T("fallback.unknown", "UNKNOWN")))
                .ToUpperInvariant();
        }

        private static string LocalizeMeaning(string meaning)
        {
            return FirstContactTerminalLocalization
                .LocalizeMeaning(NormalizeTerminalLine(meaning, "[MEANING?]"))
                .ToUpperInvariant();
        }

        private string BuildSlotScoreBlock(
            IReadOnlyList<FirstContactSlotScore> slotScores,
            FirstContactSemanticSettings settings)
        {
            if (slotScores == null || slotScores.Count == 0)
            {
                return string.Empty;
            }

            string text = "\n" + Header("meaning_signals", "[MEANING SIGNALS]") + "\n";
            for (int i = 0; i < slotScores.Count; i++)
            {
                FirstContactSlotScore score = slotScores[i];
                if (score.Slot == null)
                {
                    continue;
                }

                string marker = score.IsActive ? ">" : " ";
                text += $"{marker} {score.Slot.Id}: {BuildSignalMatchLabel(score.Score, settings)}";
                if (_debugSettings != null && _debugSettings.showScoresOnTerminal)
                {
                    text += $" ({score.Score:0.000})";
                }

                text += "\n";
            }

            return text;
        }

        private static string BuildSignalMatchLabel(float score, FirstContactSemanticSettings settings)
        {
            settings ??= ScriptableObject.CreateInstance<FirstContactSemanticSettings>();
            if (score >= settings.solvedThreshold)
            {
                return T("alignment.lock", "LOCK");
            }

            if (score >= settings.partialThreshold)
            {
                return T("alignment.strong", "STRONG");
            }

            if (score >= settings.hintThreshold)
            {
                return T("alignment.faint", "FAINT");
            }

            if (score >= Mathf.Min(0.28f, settings.hintThreshold))
            {
                return T("alignment.weak", "WEAK");
            }

            return T("alignment.none", "NONE");
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
            if (_terminalDisplay == null || probeTexture == null)
            {
                return;
            }

            RawImage image = EnsureProbePreview();
            if (image == null)
            {
                return;
            }

            ApplyProbePreviewLayout(layout);
            image.texture = probeTexture;
            if (_probePreviewAspect != null)
            {
                _probePreviewAspect.aspectRatio = Mathf.Max(0.01f, probeTexture.width / (float)Mathf.Max(1, probeTexture.height));
            }

            _probePreviewScanline?.SetScanning(scanActive);
            _probePreviewRoot.SetActive(true);
            if (layout == ProbePreviewLayout.Dispatch)
            {
                _terminalDisplay.SetContentTopInsetNormalized(GetProbePreviewTextTopInset(layout));
            }
            else if (layout == ProbePreviewLayout.Review)
            {
                _terminalDisplay.SetContentTopInsetNormalized(GetProbePreviewTextTopInset(layout));
            }
        }

        private void ClearProbePreview()
        {
            _probePreviewScanline?.SetScanning(false);
            if (_probePreviewImage != null)
            {
                _probePreviewImage.texture = null;
            }

            if (_probePreviewRoot != null)
            {
                _probePreviewRoot.SetActive(false);
            }
        }

        private RawImage EnsureProbePreview()
        {
            if (_probePreviewImage != null)
            {
                return _probePreviewImage;
            }

            RectTransform screenRect = _terminalDisplay.ScreenRectTransform;
            if (screenRect == null)
            {
                return null;
            }

            _probePreviewRoot = new GameObject(
                "FirstContactProbePreview",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            _probePreviewRoot.transform.SetParent(screenRect, false);

            RectTransform rootRect = (RectTransform)_probePreviewRoot.transform;
            rootRect.anchorMin = new Vector2(0.12f, 0.50f);
            rootRect.anchorMax = new Vector2(0.88f, 0.93f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            Image background = _probePreviewRoot.GetComponent<Image>();
            background.color = new Color(0.01f, 0.015f, 0.012f, 0.88f);
            background.raycastTarget = false;

            GameObject imageObject = new(
                "Image",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage),
                typeof(AspectRatioFitter));
            imageObject.transform.SetParent(_probePreviewRoot.transform, false);

            RectTransform imageRect = (RectTransform)imageObject.transform;
            imageRect.anchorMin = new Vector2(0.04f, 0.06f);
            imageRect.anchorMax = new Vector2(0.96f, 0.94f);
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;

            _probePreviewImage = imageObject.GetComponent<RawImage>();
            _probePreviewImage.color = Color.white;
            _probePreviewImage.raycastTarget = false;

            _probePreviewAspect = imageObject.GetComponent<AspectRatioFitter>();
            _probePreviewAspect.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            _probePreviewAspect.aspectRatio = 1f;

            GameObject scanObject = new(
                "Scanline",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            scanObject.transform.SetParent(_probePreviewRoot.transform, false);

            RectTransform scanRect = (RectTransform)scanObject.transform;
            scanRect.anchorMin = new Vector2(0.04f, 0.5f);
            scanRect.anchorMax = new Vector2(0.96f, 0.5f);
            scanRect.offsetMin = new Vector2(0f, -1.5f);
            scanRect.offsetMax = new Vector2(0f, 1.5f);

            Image scanImage = scanObject.GetComponent<Image>();
            scanImage.color = new Color(0.35f, 1f, 0.5f, 0.58f);
            scanImage.raycastTarget = false;

            _probePreviewScanline = _probePreviewRoot.AddComponent<FirstContactProbePreviewScanline>();
            _probePreviewScanline.Configure(scanRect);
            _probePreviewScanline.SetScanning(false);

            _probePreviewRoot.SetActive(false);
            return _probePreviewImage;
        }

        private void ApplyProbePreviewLayout(ProbePreviewLayout layout)
        {
            if (_probePreviewRoot == null)
            {
                return;
            }

            RectTransform rootRect = (RectTransform)_probePreviewRoot.transform;
            rootRect.anchorMin = GetProbePreviewAnchorMin(layout);
            rootRect.anchorMax = GetProbePreviewAnchorMax(layout);

            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
        }

        private Vector2 GetProbePreviewAnchorMin(ProbePreviewLayout layout)
        {
            if (_presentationSettings == null)
            {
                return layout == ProbePreviewLayout.Dispatch
                    ? new Vector2(0.54f, 0.36f)
                    : new Vector2(0.12f, 0.5f);
            }

            return layout == ProbePreviewLayout.Dispatch
                ? _presentationSettings.probeDispatchAnchorMin
                : _presentationSettings.probeReviewAnchorMin;
        }

        private Vector2 GetProbePreviewAnchorMax(ProbePreviewLayout layout)
        {
            if (_presentationSettings == null)
            {
                return layout == ProbePreviewLayout.Dispatch
                    ? new Vector2(0.93f, 0.76f)
                    : new Vector2(0.88f, 0.93f);
            }

            return layout == ProbePreviewLayout.Dispatch
                ? _presentationSettings.probeDispatchAnchorMax
                : _presentationSettings.probeReviewAnchorMax;
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

        private enum ProbePreviewLayout
        {
            Review,
            Dispatch
        }
    }

    internal sealed class FirstContactProbePreviewScanline : MonoBehaviour
    {
        private RectTransform _line;
        private bool _scanning;
        private float _phase;

        public void Configure(RectTransform line)
        {
            _line = line;
        }

        public void SetScanning(bool scanning)
        {
            _scanning = scanning;
            _phase = 0f;
            if (_line != null)
            {
                _line.gameObject.SetActive(scanning);
            }
        }

        private void Update()
        {
            if (!_scanning || _line == null)
            {
                return;
            }

            RectTransform parent = _line.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            float height = Mathf.Max(1f, parent.rect.height);
            _phase = (_phase + Time.deltaTime * 0.72f) % 1f;
            float y = Mathf.Lerp((height * 0.42f) - 2f, (-height * 0.42f) + 2f, _phase);
            _line.anchoredPosition = new Vector2(0f, y);
        }
    }

}
