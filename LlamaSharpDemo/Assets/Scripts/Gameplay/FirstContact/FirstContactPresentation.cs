using System;
using System.Collections.Generic;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public sealed class FirstContactTerminalPresenter
    {
        public static string MeaningMapActionLabel => T("choice.meaning_map", "MEANING MAP");
        public static string AnswerActionLabel => T("choice.send_reply", "SEND REPLY");

        private readonly TerminalDisplay _terminalDisplay;
        private readonly TerminalBrainwaveDisplay _brainwaveDisplay;
        private readonly FirstContactSemanticMapDisplay _semanticMapDisplay;
        private readonly FirstContactDebugSettings _debugSettings;
        private bool _hasShownQuestionOnce;
        private int _lastIncomingTransmissionTextLength;

        public FirstContactTerminalPresenter(
            TerminalDisplay terminalDisplay,
            FirstContactDebugSettings debugSettings)
        {
            _terminalDisplay = terminalDisplay;
            _brainwaveDisplay = terminalDisplay != null
                ? terminalDisplay.GetComponent<TerminalBrainwaveDisplay>() ??
                  terminalDisplay.GetComponentInChildren<TerminalBrainwaveDisplay>(true)
                : null;
            _semanticMapDisplay = ResolveSemanticMapDisplay(terminalDisplay);
            _debugSettings = debugSettings;
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
                instant || _hasShownQuestionOnce);
            _hasShownQuestionOnce = true;
        }

        public void ShowLocalReferenceIntro(bool instant = false)
        {
            ClearVisualOverlays();
            string target = GetLocalReferenceTargetLabel();
            string text =
                Header("local_reference", "[LOCAL REFERENCE]") + "\n\n" +
                T("local_reference.need_known_signal", "TRANSLATOR NEEDS ONE KNOWN SIGNAL") + "\n\n" +
                T("line.target", "TARGET: {target}", L10n.Arg("target", target)) + "\n" +
                T("local_reference.draw_on_tablet", "DRAW {target} ON THE TABLET", L10n.Arg("target", target)) +
                BuildContinuePrompt();
            _terminalDisplay?.ShowText(text, instant);
        }

        public void ShowLocalReferenceTabletOpen(bool instant = false)
        {
            ClearVisualOverlays();
            string target = GetLocalReferenceTargetLabel();
            string text =
                Header("tablet_link_open", "[TABLET LINK OPEN]") + "\n\n" +
                T("line.target", "TARGET: {target}", L10n.Arg("target", target)) + "\n" +
                T("line.draw_target", "DRAW {target}", L10n.Arg("target", target)) + "\n\n" +
                T("line.submit_enter", "SUBMIT: ENTER");
            _terminalDisplay?.ShowText(text, instant);
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
                text += T("line.unknown_token_signal_color", "UNKNOWN TOKEN SIGNAL: {color}", L10n.Arg("color", SignalColor("red", "RED")));
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

        public void ShowQuestionChoiceEcho(AlienQuestion question, string choiceLabel, bool instant = true)
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
            bool instant = true)
        {
            ClearVisualOverlays();
            string text = BuildQuestionText(question);
            text += "\n\n" + Header("tablet_link_open", "[TABLET LINK OPEN]") + "\n\n";
            text += T("line.channel", "CHANNEL: {channel}", L10n.Arg("channel", BuildChannelLabel(source, unknownId))) + "\n";
            text += T("line.draw_one_object", "DRAW ONE OBJECT") + "\n\n";
            text += T("line.submit_enter", "SUBMIT: ENTER");
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
                T("line.category", "CATEGORY: {category}", L10n.Arg("category", NormalizeTerminalLine(category, "UNKNOWN").ToUpperInvariant())) + "\n" +
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
                T("line.category", "CATEGORY: {category}", L10n.Arg("category", NormalizeTerminalLine(category, "UNKNOWN").ToUpperInvariant())) + "\n" +
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
                becameStable);
            string label = card != null ? GetDisplayLabel(card) : string.Empty;
            string text =
                Header("signal_capture", "[SIGNAL CAPTURE]") + "\n\n" +
                T("line.visual_read", "VISUAL READ: {label}", L10n.Arg("label", NormalizeTerminalLine(label, T("fallback.unknown", "UNKNOWN")).ToUpperInvariant())) + "\n" +
                T("line.category", "CATEGORY: {category}", L10n.Arg("category", NormalizeTerminalLine(category, "UNKNOWN").ToUpperInvariant())) + "\n" +
                T("line.group", "GROUP: {group}", L10n.Arg("group", BuildGroupState(traceCount, requiredTraceCount, stable))) + "\n" +
                BuildTraceLine(previousTraceCount, traceCount, requiredTraceCount, accepted) +
                BuildContinuePrompt();
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
                T("line.category", "CATEGORY: {category}", L10n.Arg("category", NormalizeTerminalLine(category, "UNKNOWN").ToUpperInvariant())) + "\n" +
                T("line.trace_count", "TRACE: {count}/{required}",
                    L10n.Arg("count", Mathf.Max(0, traceCount).ToString("00")),
                    L10n.Arg("required", Mathf.Max(1, requiredTraceCount).ToString("00"))) + "\n" +
                T("line.group", "GROUP: {group}", L10n.Arg("group", T("cluster.stable", "STABLE"))) + "\n" +
                T("line.meaning", "MEANING: {meaning}", L10n.Arg("meaning", NormalizeTerminalLine(meaning, "[MEANING?]").ToUpperInvariant())) +
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

        public void ShowTabletImageReceived(
            FirstContactCardSource source,
            string unknownId,
            bool instant = false)
        {
            ClearVisualOverlays();
            if (source == FirstContactCardSource.LocalReference)
            {
                string target = GetLocalReferenceTargetLabel();
                string localText =
                    Header("local_reference_analysis", "[LOCAL REFERENCE ANALYSIS]") + "\n\n" +
                    T("line.image_captured", "IMAGE CAPTURED") + "\n" +
                    T("line.extracting_local_meaning_signal", "EXTRACTING LOCAL MEANING SIGNAL") + "\n" +
                    T("line.target", "TARGET: {target}", L10n.Arg("target", target));
                _terminalDisplay?.ShowText(localText, instant);
                return;
            }

            string text =
                Header("visual_probe_analysis", "[VISUAL PROBE ANALYSIS]") + "\n\n" +
                T("line.image_captured", "IMAGE CAPTURED") + "\n" +
                T("line.extracting_meaning_signal", "EXTRACTING MEANING SIGNAL") + "\n" +
                T("line.visual_probe_signal_color", "VISUAL PROBE SIGNAL: {color}", L10n.Arg("color", SignalColor("cyan", "CYAN"))) + "\n\n" +
                T("line.channel", "CHANNEL: {channel}", L10n.Arg("channel", BuildChannelLabel(source, unknownId)));
            _terminalDisplay?.ShowText(text, instant);
        }

        public void ShowLocalReferenceMismatch(
            string label,
            string reason,
            int selectedIndex,
            bool instant = true)
        {
            ClearVisualOverlays();
            string target = GetLocalReferenceTargetLabel();
            string normalizedReason = NormalizeTerminalLine(reason, T("reason.reference_not_stored", "REFERENCE NOT STORED")).ToUpperInvariant();
            string text =
                Header("reference_check", "[REFERENCE CHECK]") + "\n\n" +
                T("line.visual_read", "VISUAL READ: {label}", L10n.Arg("label", NormalizeTerminalLine(label, T("fallback.unknown", "UNKNOWN")).ToUpperInvariant())) + "\n" +
                T("line.target", "TARGET: {target}", L10n.Arg("target", target)) + "\n" +
                $"{normalizedReason}\n\n" +
                BuildChoiceLine(0, selectedIndex, T("choice.redraw", "REDRAW")) +
                BuildSelectPrompt();
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

        public void ShowLocalReferenceReview(
            string label,
            int selectedIndex,
            bool instant = true)
        {
            ClearVisualOverlays();
            string target = GetLocalReferenceTargetLabel();
            string text =
                Header("reference_check", "[REFERENCE CHECK]") + "\n\n" +
                T("line.visual_read", "VISUAL READ: {label}", L10n.Arg("label", NormalizeTerminalLine(label, target).ToUpperInvariant())) + "\n" +
                T("line.target", "TARGET: {target}", L10n.Arg("target", target)) + "\n" +
                T("line.reference_match", "REFERENCE MATCH") + "\n\n" +
                BuildChoiceLine(0, selectedIndex, T("choice.accept", "ACCEPT")) +
                BuildChoiceLine(1, selectedIndex, T("choice.redraw", "REDRAW")) +
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
                Header("visual_read", "[VISUAL READ]") + "\n\n" +
                $"{NormalizeTerminalLine(label, T("fallback.unknown", "UNKNOWN")).ToUpperInvariant()}\n\n" +
                T("line.channel", "CHANNEL: {channel}", L10n.Arg("channel", BuildChannelLabel(source, unknownId))) + "\n\n" +
                BuildChoiceLine(0, selectedIndex, T("choice.accept", "ACCEPT")) +
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
                T("line.visual_read", "VISUAL READ: {label}", L10n.Arg("label", GetDisplayLabel(card))) + "\n\n" +
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

        public void ShowLocalReferenceSignal(SemanticCardRecord card, bool instant = false)
        {
            _semanticMapDisplay?.Clear();
            string target = GetLocalReferenceTargetLabel();
            string text =
                Header("local_signal_capture", "[LOCAL SIGNAL CAPTURE]") + "\n\n" +
                T("line.local_reference_signal_color", "LOCAL REFERENCE SIGNAL: {color}", L10n.Arg("color", SignalColor("cyan", "CYAN"))) + "\n\n" +
                T("line.visual_read", "VISUAL READ: {label}", L10n.Arg("label", NormalizeTerminalLine(GetDisplayLabel(card), target).ToUpperInvariant())) + "\n" +
                T("line.mapping", "MAPPING: {target}", L10n.Arg("target", target)) +
                BuildContinuePrompt();
            _terminalDisplay?.ShowText(text, instant);
            if (_brainwaveDisplay != null && card != null && card.WaveformProfile.IsValid)
            {
                _brainwaveDisplay.PlayComparisonCapture(
                    DoodleDiplomacy.Devices.BrainwaveSemanticProfile.Invalid,
                    card.WaveformProfile);
            }
        }

        public void ShowLocalReferenceStored(
            SemanticCardRecord card,
            SemanticClusterRecord cluster,
            FirstContactSemanticMapSnapshot mapSnapshot,
            FirstContactSemanticSettings semanticSettings,
            bool instant = false)
        {
            _brainwaveDisplay?.Clear();
            ShowFullMap(mapSnapshot, semanticSettings);
            string target = GetLocalReferenceTargetLabel();
            string text =
                Header("meaning_map", "[MEANING MAP]") + "\n\n" +
                T("line.target_stored", "{target} STORED", L10n.Arg("target", target)) + "\n" +
                T("line.local_reference_ready", "LOCAL REFERENCE READY");

            if (_debugSettings != null && _debugSettings.showScoresOnTerminal && cluster != null)
            {
                text += "\n" + T("line.cluster", "CLUSTER: {cluster}", L10n.Arg("cluster", cluster.Id));
            }

            text += BuildContinuePrompt();
            _terminalDisplay?.ShowText(text, instant);
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
                $"{cluster.DisplayName}";
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
            bool instant = false)
        {
            _semanticMapDisplay?.Clear();
            string label = card != null ? GetDisplayLabel(card) : string.Empty;
            bool isProbeResult = activeResolution.HasValue && activeResolution.Value.Slot != null;
            string text = isProbeResult
                ? Header("probe_result", "[PROBE RESULT]") + "\n\n"
                : Header("reply_signal", "[REPLY SIGNAL]") + "\n\n";
            if (isProbeResult)
            {
                text += T("line.unknown_token_signal_color", "UNKNOWN TOKEN SIGNAL: {color}", L10n.Arg("color", SignalColor("red", "RED"))) + "\n";
                text += T("line.visual_probe_signal_color", "VISUAL PROBE SIGNAL: {color}", L10n.Arg("color", SignalColor("cyan", "CYAN"))) + "\n\n";
            }

            text += T("line.visual_read", "VISUAL READ: {label}", L10n.Arg("label", NormalizeTerminalLine(label, T("fallback.unknown", "UNKNOWN")).ToUpperInvariant())) + "\n";

            if (isProbeResult)
            {
                FirstContactResolutionResult result = activeResolution.Value;
                text += T("line.translation_alignment", "TRANSLATION ALIGNMENT: {alignment}", L10n.Arg("alignment", BuildSignalMatchLabel(result.Score, semanticSettings))) + "\n";
                text += BuildTokenUpdateBlock(result);
            }
            else
            {
                text += T("line.translation_alignment", "TRANSLATION ALIGNMENT: {alignment}", L10n.Arg("alignment", T("alignment.stored", "STORED"))) + "\n";
            }

            if (isProbeResult && cluster != null && cluster.IsStable)
            {
                text += "\n" + T("line.memory_map_updated", "MEMORY MAP: {name} UPDATED", L10n.Arg("name", cluster.DisplayName)) + "\n";
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
            return "\n\n" + T("prompt.continue", "PRESS ENTER TO CONTINUE");
        }

        private static string BuildSelectPrompt()
        {
            return "\n" + T("prompt.select", "PRESS ENTER TO SELECT");
        }

        private static string BuildChannelLabel(FirstContactCardSource source, string unknownId)
        {
            if (source == FirstContactCardSource.LocalReference)
            {
                return T("channel.local_reference", "LOCAL REFERENCE");
            }

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
                line += token;
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
                ? L10n.Label(card.Label ?? string.Empty).ToUpperInvariant()
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
                string label = cluster.Members[i].Label ?? string.Empty;
                if (string.IsNullOrWhiteSpace(label))
                {
                    continue;
                }

                if (line.Length > 0)
                {
                    line += " / ";
                }

                line += L10n.Label(label).ToUpperInvariant();
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
                T("line.token", "TOKEN: {token}", L10n.Arg("token", result.Slot.Id)) + "\n";

            if (!result.Changed)
            {
                return text + T("line.no_change", "NO CHANGE") + "\n";
            }

            FirstContactStageTexts stageTexts = result.Slot.Definition?.stageTexts ?? new FirstContactStageTexts();
            string before = stageTexts.GetDisplayText(result.PreviousStage, result.Slot.Id);
            string after = stageTexts.GetDisplayText(result.NewStage, result.Slot.Id);
            return text + $"{before} -> {after}\n";
        }

        private string BuildSlotScoreBlock(
            IReadOnlyList<FirstContactSlotScore> slotScores,
            FirstContactSemanticSettings settings)
        {
            if (slotScores == null || slotScores.Count == 0)
            {
                return string.Empty;
            }

            string text = "\n" + Header("token_signals", "[TOKEN SIGNALS]") + "\n";
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
            bool becameStable)
        {
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

            _semanticMapDisplay?.ShowBootstrapResultTransition(
                beforeMapSnapshot,
                mapSnapshot,
                activeCardNodeId,
                categoryNodeId,
                accepted,
                becameStable);
        }

        private static string BuildTraceLine(
            int previousTraceCount,
            int traceCount,
            int requiredTraceCount,
            bool accepted)
        {
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

        private static string GetLocalReferenceTargetLabel()
        {
            string label = L10n.Label("earth");
            return string.IsNullOrWhiteSpace(label) ? "EARTH" : label.Trim().ToUpperInvariant();
        }

        private void ClearVisualOverlays()
        {
            _brainwaveDisplay?.Clear();
            _semanticMapDisplay?.Clear();
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
    }

    public interface IFirstContactActionPresenter
    {
        void ShowSubmit(string prompt, Action onSubmit);
        void ShowConfirmation(string prompt, string label, Action onConfirm, Action onRedraw);
        void Hide();
    }

    [DisallowMultipleComponent]
    public sealed class FirstContactActionButtonPanel : MonoBehaviour, IFirstContactActionPresenter
    {
        private Canvas _canvas;
        private GameObject _panel;
        private TextMeshProUGUI _promptText;
        private RectTransform _buttonRoot;

        public void ShowSubmit(string prompt, Action onSubmit)
        {
            EnsureBuilt();
            ClearButtons();
            _promptText.text = prompt ?? string.Empty;
            AddButton(L10n.T("ui.day1.submit", "Submit"), () => onSubmit?.Invoke(), 180f);
            SetVisible(true);
        }

        public void ShowConfirmation(string prompt, string label, Action onConfirm, Action onRedraw)
        {
            EnsureBuilt();
            ClearButtons();
            _promptText.text = string.IsNullOrWhiteSpace(label)
                ? prompt ?? string.Empty
                : $"{prompt}\n{label.ToUpperInvariant()}";
            AddButton(L10n.T("ui.day1.confirm", "Confirm"), () => onConfirm?.Invoke(), 180f);
            AddButton(L10n.T("ui.day1.redraw", "Redraw"), () => onRedraw?.Invoke(), 180f);
            SetVisible(true);
        }

        public void Hide()
        {
            SetVisible(false);
        }

        private void EnsureBuilt()
        {
            if (_panel != null)
            {
                return;
            }

            GameObject canvasObject = new("FirstContactActionCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(canvasObject);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 145;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            _panel = new GameObject("FirstContactActionPanel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(VerticalLayoutGroup));
            _panel.transform.SetParent(canvasObject.transform, false);

            RectTransform panelRect = (RectTransform)_panel.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0f);
            panelRect.anchorMax = new Vector2(0.5f, 0f);
            panelRect.pivot = new Vector2(0.5f, 0f);
            panelRect.anchoredPosition = new Vector2(0f, 42f);
            panelRect.sizeDelta = new Vector2(1120f, 150f);

            Image panelImage = _panel.GetComponent<Image>();
            panelImage.color = new Color(0.03f, 0.04f, 0.05f, 0.86f);
            panelImage.raycastTarget = true;

            VerticalLayoutGroup panelLayout = _panel.GetComponent<VerticalLayoutGroup>();
            panelLayout.padding = new RectOffset(22, 22, 16, 18);
            panelLayout.spacing = 10f;
            panelLayout.childAlignment = TextAnchor.MiddleCenter;
            panelLayout.childControlWidth = true;
            panelLayout.childControlHeight = true;
            panelLayout.childForceExpandWidth = true;
            panelLayout.childForceExpandHeight = false;

            _promptText = CreateText("Prompt", _panel.transform, 20f, FontStyles.Normal);
            LayoutElement promptLayout = _promptText.gameObject.AddComponent<LayoutElement>();
            promptLayout.preferredHeight = 38f;
            promptLayout.flexibleHeight = 0f;

            GameObject row = new("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(_panel.transform, false);
            _buttonRoot = (RectTransform)row.transform;
            HorizontalLayoutGroup rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childControlWidth = false;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            LayoutElement rowElement = row.AddComponent<LayoutElement>();
            rowElement.preferredHeight = 62f;
            rowElement.flexibleWidth = 1f;
        }

        private Button AddButton(string label, Action onClick, float preferredWidth)
        {
            GameObject buttonObject = new($"FirstContactButton_{label}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(_buttonRoot, false);

            Image image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.12f, 0.15f, 0.18f, 0.96f);

            Button button = buttonObject.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = new Color(0.12f, 0.15f, 0.18f, 0.96f);
            colors.highlightedColor = new Color(0.22f, 0.29f, 0.34f, 1f);
            colors.pressedColor = new Color(0.08f, 0.12f, 0.15f, 1f);
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;
            button.onClick.AddListener(() => onClick?.Invoke());

            LayoutElement element = buttonObject.GetComponent<LayoutElement>();
            element.preferredWidth = preferredWidth;
            element.preferredHeight = 56f;

            TextMeshProUGUI text = CreateText("Label", buttonObject.transform, 20f, FontStyles.Bold);
            text.text = label;
            text.enableAutoSizing = true;
            text.fontSizeMin = 11f;
            text.fontSizeMax = 20f;
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12f, 4f);
            textRect.offsetMax = new Vector2(-12f, -4f);
            return button;
        }

        private static TextMeshProUGUI CreateText(string name, Transform parent, float fontSize, FontStyles style)
        {
            GameObject textObject = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = string.Empty;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            text.characterSpacing = 0f;
            return text;
        }

        private void ClearButtons()
        {
            if (_buttonRoot == null)
            {
                return;
            }

            for (int i = _buttonRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(_buttonRoot.GetChild(i).gameObject);
            }
        }

        private void SetVisible(bool visible)
        {
            if (_panel != null)
            {
                _panel.SetActive(visible);
            }
        }

        private void OnDestroy()
        {
            if (_canvas != null)
            {
                Destroy(_canvas.gameObject);
            }
        }
    }
}
