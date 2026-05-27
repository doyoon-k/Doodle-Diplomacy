using DoodleDiplomacy.Core;
using UnityEngine;

namespace DoodleDiplomacy.Devices
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TerminalDisplay))]
    public sealed class TerminalBrainwaveDisplay : MonoBehaviour
    {
        private const string DefaultGraphName = "BrainwaveGraph";

        [Header("References")]
        [Tooltip("Terminal text display whose screen panel hosts the generated brainwave graph.")]
        [SerializeField] private TerminalDisplay terminalDisplay;
        [Tooltip("Optional prebuilt graph component. If empty and Auto Create Graph is enabled, one is created under the terminal screen.")]
        [SerializeField] private BrainwaveGraphDisplay brainwaveGraph;

        [Header("Layout")]
        [Tooltip("Create the brainwave graph object automatically when no graph reference is assigned.")]
        [SerializeField] private bool autoCreateGraph = true;
        [Tooltip("Fraction of the terminal screen height reserved for the waveform area.")]
        [SerializeField, Range(0.2f, 0.7f)] private float graphHeightRatio = 0.42f;
        [Tooltip("Fraction of the terminal screen height used as the top inset for terminal text while brainwaves are active.")]
        [SerializeField, Range(0f, 0.85f)] private float textTopInsetRatio = 0.46f;
        [Tooltip("Normalized left and right padding applied to the waveform area.")]
        [SerializeField, Range(0f, 0.1f)] private float horizontalInsetRatio = 0.03f;
        [Tooltip("Normalized top padding applied above the waveform area.")]
        [SerializeField, Range(0f, 0.1f)] private float topInsetRatio = 0.04f;

        [Header("Signal Shape")]
        [Tooltip("Vertical distance from graph center to the upper/lower waveform while the terminal is still searching.")]
        [SerializeField, Range(0f, 0.45f)] private float searchingChannelSpread = 0.24f;
        [Tooltip("Vertical distance from graph center to the upper/lower waveform after the reaction trace is locked. Lower values make all waveforms converge more tightly, independent of reaction tier.")]
        [SerializeField, Range(0f, 0.45f)] private float lockedChannelSpread = 0.1f;

        private void Reset()
        {
            terminalDisplay = GetComponent<TerminalDisplay>();
        }

        private void Awake()
        {
            ResolveReferences();
            ConfigureGraphLayout();
            ConfigureGraphSignalShape();
        }

        private void OnValidate()
        {
            graphHeightRatio = Mathf.Clamp(graphHeightRatio, 0.2f, 0.7f);
            textTopInsetRatio = Mathf.Clamp01(textTopInsetRatio);
            horizontalInsetRatio = Mathf.Clamp(horizontalInsetRatio, 0f, 0.1f);
            topInsetRatio = Mathf.Clamp(topInsetRatio, 0f, 0.1f);
            searchingChannelSpread = Mathf.Clamp(searchingChannelSpread, 0f, 0.45f);
            lockedChannelSpread = Mathf.Clamp(lockedChannelSpread, 0f, 0.45f);

            ResolveReferences();
            ConfigureGraphLayout();
            ConfigureGraphSignalShape();
        }

        public void PlaySearching(string label, int sampleIndex, int sessionSeed)
        {
            BrainwaveGraphDisplay graph = EnsureGraph();
            if (graph == null)
            {
                return;
            }

            ApplyActiveLayout();
            graph.PlaySearching(label, sampleIndex, sessionSeed);
        }

        public void BeginTraceLock(
            ReactionTier tier,
            string label,
            int sampleIndex,
            int sessionSeed,
            float lockDuration)
        {
            BrainwaveGraphDisplay graph = EnsureGraph();
            if (graph == null)
            {
                return;
            }

            ApplyActiveLayout();
            graph.BeginTraceLock(tier, label, sampleIndex, sessionSeed, lockDuration);
        }

        public void PlayLocked(ReactionTier tier, string label, int sampleIndex, int sessionSeed)
        {
            BrainwaveGraphDisplay graph = EnsureGraph();
            if (graph == null)
            {
                return;
            }

            ApplyActiveLayout();
            graph.PlayLocked(tier, label, sampleIndex, sessionSeed);
        }

        public void Clear()
        {
            brainwaveGraph?.Clear();
            terminalDisplay?.SetContentTopInsetNormalized(0f);
        }

        private void ApplyActiveLayout()
        {
            ConfigureGraphLayout();
            ConfigureGraphSignalShape();
            terminalDisplay?.SetContentTopInsetNormalized(textTopInsetRatio);
        }

        private BrainwaveGraphDisplay EnsureGraph()
        {
            ResolveReferences();

            if (brainwaveGraph != null)
            {
                ConfigureGraphLayout();
                ConfigureGraphSignalShape();
                return brainwaveGraph;
            }

            if (!autoCreateGraph || terminalDisplay == null)
            {
                return null;
            }

            RectTransform screenRect = terminalDisplay.ScreenRectTransform;
            if (screenRect == null)
            {
                Debug.LogWarning("[TerminalBrainwaveDisplay] Could not create brainwave graph because terminal screen panel is missing.", this);
                return null;
            }

            var graphObject = new GameObject(
                DefaultGraphName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(BrainwaveGraphDisplay));
            graphObject.transform.SetParent(screenRect, false);
            graphObject.transform.SetAsFirstSibling();

            brainwaveGraph = graphObject.GetComponent<BrainwaveGraphDisplay>();
            ConfigureGraphLayout();
            ConfigureGraphSignalShape();
            brainwaveGraph.Clear();
            return brainwaveGraph;
        }

        private void ResolveReferences()
        {
            if (terminalDisplay == null)
            {
                terminalDisplay = GetComponent<TerminalDisplay>();
            }

            if (brainwaveGraph == null && terminalDisplay != null)
            {
                brainwaveGraph = terminalDisplay.GetComponentInChildren<BrainwaveGraphDisplay>(true);
            }
        }

        private void ConfigureGraphLayout()
        {
            if (brainwaveGraph == null)
            {
                return;
            }

            RectTransform graphRect = brainwaveGraph.rectTransform;
            if (graphRect == null)
            {
                return;
            }

            float heightRatio = Mathf.Clamp(graphHeightRatio, 0.2f, 0.7f);
            float horizontalInset = Mathf.Clamp(horizontalInsetRatio, 0f, 0.1f);
            float topInset = Mathf.Clamp(topInsetRatio, 0f, 0.1f);
            graphRect.anchorMin = new Vector2(horizontalInset, 1f - heightRatio);
            graphRect.anchorMax = new Vector2(1f - horizontalInset, 1f - topInset);
            graphRect.offsetMin = Vector2.zero;
            graphRect.offsetMax = Vector2.zero;
            graphRect.pivot = new Vector2(0.5f, 0.5f);
        }

        private void ConfigureGraphSignalShape()
        {
            if (brainwaveGraph == null)
            {
                return;
            }

            brainwaveGraph.ConfigureChannelSpread(searchingChannelSpread, lockedChannelSpread);
        }
    }
}
