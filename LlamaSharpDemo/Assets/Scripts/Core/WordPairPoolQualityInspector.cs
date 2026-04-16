using DoodleDiplomacy.AI;
using DoodleDiplomacy.Data;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace DoodleDiplomacy.Core
{
    public class WordPairPoolQualityInspector : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private AIPipelineBridge pipelineBridge;
        [SerializeField] private WordPairPool wordPairPool;

        [Header("Runtime")]
        [SerializeField] private bool startEnabled = false;
        [SerializeField] private bool autoGenerateFirstPairOnEnable = true;
        [SerializeField] private bool autoGenerateOnPairNavigation = true;
        [SerializeField] private bool wrapAround = true;
        [SerializeField] private bool enableInBuild = false;

        [Header("Key Bindings")]
        [SerializeField] private KeyCode toggleInspectorKey = KeyCode.F8;
        [SerializeField] private KeyCode previousPairKey = KeyCode.F9;
        [SerializeField] private KeyCode nextPairKey = KeyCode.F10;
        [SerializeField] private KeyCode regeneratePairKey = KeyCode.F11;

        private bool _isInspectorEnabled;
        private bool _isGenerating;
        private int _currentPairIndex = -1;
        private int? _queuedPairIndex;

        private void Awake()
        {
#if !UNITY_EDITOR
            if (!enableInBuild)
            {
                enabled = false;
                return;
            }
#endif
            _isInspectorEnabled = startEnabled;
            ResolveReferences();
        }

        private void Update()
        {
            if (WasKeyPressed(toggleInspectorKey))
            {
                ToggleInspector();
            }

            if (!_isInspectorEnabled)
            {
                return;
            }

            if (!TryEnsureReady())
            {
                return;
            }

            if (_isGenerating)
            {
                if (WasKeyPressed(previousPairKey))
                {
                    QueueRelativeStep(-1);
                }

                if (WasKeyPressed(nextPairKey))
                {
                    QueueRelativeStep(1);
                }

                if (WasKeyPressed(regeneratePairKey))
                {
                    QueueCurrentPair();
                }

                return;
            }

            if (WasKeyPressed(previousPairKey))
            {
                NavigateRelative(-1);
                return;
            }

            if (WasKeyPressed(nextPairKey))
            {
                NavigateRelative(1);
                return;
            }

            if (WasKeyPressed(regeneratePairKey))
            {
                RegenerateCurrentPair();
            }
        }

        private void ToggleInspector()
        {
            _isInspectorEnabled = !_isInspectorEnabled;
            Debug.Log(
                $"[WordPairPoolQualityInspector] Inspector {(_isInspectorEnabled ? "enabled" : "disabled")} " +
                $"(toggle={toggleInspectorKey}, prev={previousPairKey}, next={nextPairKey}, regenerate={regeneratePairKey}).");

            if (!_isInspectorEnabled)
            {
                return;
            }

            ResolveReferences();
            if (!TryEnsureReady())
            {
                return;
            }

            if (autoGenerateFirstPairOnEnable && _currentPairIndex < 0 && !_isGenerating)
            {
                NavigateToIndex(0);
            }
        }

        private void ResolveReferences()
        {
            pipelineBridge ??= AIPipelineBridge.Instance;
            if (pipelineBridge == null)
            {
                pipelineBridge = FindFirstObjectByType<AIPipelineBridge>();
            }

            if (wordPairPool == null && pipelineBridge != null)
            {
                wordPairPool = pipelineBridge.CurrentWordPairPool;
            }
        }

        private bool TryEnsureReady()
        {
            if (pipelineBridge == null)
            {
                Debug.LogWarning("[WordPairPoolQualityInspector] AIPipelineBridge reference is missing.");
                return false;
            }

            if (wordPairPool == null)
            {
                Debug.LogWarning("[WordPairPoolQualityInspector] WordPairPool reference is missing.");
                return false;
            }

            if (wordPairPool.PairCount <= 0)
            {
                Debug.LogWarning("[WordPairPoolQualityInspector] WordPairPool is empty.");
                return false;
            }

            return true;
        }

        private void NavigateRelative(int step)
        {
            int pairCount = wordPairPool.PairCount;
            int baseIndex = _currentPairIndex >= 0
                ? _currentPairIndex
                : (step >= 0 ? 0 : pairCount - 1);

            int nextIndex = ResolveIndex(baseIndex + step);
            NavigateToIndex(nextIndex);
        }

        private void RegenerateCurrentPair()
        {
            int pairCount = wordPairPool.PairCount;
            if (_currentPairIndex < 0 || _currentPairIndex >= pairCount)
            {
                NavigateToIndex(0);
                return;
            }

            NavigateToIndex(_currentPairIndex);
        }

        private void NavigateToIndex(int pairIndex)
        {
            if (!TryGetPair(pairIndex, out string wordA, out string wordB, out string labelA, out string labelB))
            {
                return;
            }

            _currentPairIndex = pairIndex;
            _isGenerating = true;
            _queuedPairIndex = null;

            Debug.Log(
                $"[WordPairPoolQualityInspector] Generating {BuildPairSummary(pairIndex, wordA, wordB, labelA, labelB)}");

            if (!autoGenerateOnPairNavigation)
            {
                _isGenerating = false;
                return;
            }

            pipelineBridge.DebugGenerateObjectsForPair(wordA, wordB, labelA, labelB, success =>
            {
                _isGenerating = false;
                Debug.Log(
                    $"[WordPairPoolQualityInspector] {(success ? "Success" : "Failed")} " +
                    $"{BuildPairSummary(_currentPairIndex, wordA, wordB, labelA, labelB)}");

                if (_queuedPairIndex.HasValue)
                {
                    int queued = _queuedPairIndex.Value;
                    _queuedPairIndex = null;
                    NavigateToIndex(queued);
                }
            });
        }

        private bool TryGetPair(int pairIndex, out string wordA, out string wordB, out string labelA, out string labelB)
        {
            int resolvedIndex = ResolveIndex(pairIndex);
            if (!wordPairPool.TryGetPairAt(resolvedIndex, out wordA, out wordB, out labelA, out labelB))
            {
                Debug.LogWarning($"[WordPairPoolQualityInspector] Invalid pair index: {pairIndex}");
                return false;
            }

            return true;
        }

        private void QueueRelativeStep(int step)
        {
            int pairCount = wordPairPool.PairCount;
            int baseIndex;
            if (_queuedPairIndex.HasValue)
            {
                baseIndex = _queuedPairIndex.Value;
            }
            else if (_currentPairIndex >= 0)
            {
                baseIndex = _currentPairIndex;
            }
            else
            {
                baseIndex = step >= 0 ? 0 : pairCount - 1;
            }

            _queuedPairIndex = ResolveIndex(baseIndex + step);
        }

        private void QueueCurrentPair()
        {
            int pairCount = wordPairPool.PairCount;
            int current = _currentPairIndex >= 0 ? _currentPairIndex : 0;
            _queuedPairIndex = ResolveIndex(Mathf.Clamp(current, 0, pairCount - 1));
        }

        private int ResolveIndex(int index)
        {
            int pairCount = wordPairPool.PairCount;
            if (pairCount <= 0)
            {
                return -1;
            }

            if (wrapAround)
            {
                int wrapped = index % pairCount;
                return wrapped < 0 ? wrapped + pairCount : wrapped;
            }

            return Mathf.Clamp(index, 0, pairCount - 1);
        }

        private string BuildPairSummary(int pairIndex, string wordA, string wordB, string labelA, string labelB)
        {
            int pairCount = Mathf.Max(1, wordPairPool.PairCount);
            int clampedIndex = Mathf.Clamp(pairIndex, 0, pairCount - 1);
            return $"[{clampedIndex + 1}/{pairCount}] label=({labelA}, {labelB}) sd=({wordA}, {wordB})";
        }

        private static bool WasKeyPressed(KeyCode keyCode)
        {
#if ENABLE_INPUT_SYSTEM
            if (keyCode == KeyCode.None)
            {
                return false;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return false;
            }

            KeyControl keyControl = GetInputSystemKeyControl(keyboard, keyCode);
            return keyControl != null && keyControl.wasPressedThisFrame;
#else
            return keyCode != KeyCode.None && Input.GetKeyDown(keyCode);
#endif
        }

#if ENABLE_INPUT_SYSTEM
        private static KeyControl GetInputSystemKeyControl(Keyboard keyboard, KeyCode keyCode)
        {
            return keyCode switch
            {
                KeyCode.F8 => keyboard.f8Key,
                KeyCode.F9 => keyboard.f9Key,
                KeyCode.F10 => keyboard.f10Key,
                KeyCode.F11 => keyboard.f11Key,
                KeyCode.LeftBracket => keyboard.leftBracketKey,
                KeyCode.RightBracket => keyboard.rightBracketKey,
                KeyCode.Comma => keyboard.commaKey,
                KeyCode.Period => keyboard.periodKey,
                KeyCode.Minus => keyboard.minusKey,
                KeyCode.Equals => keyboard.equalsKey,
                KeyCode.Slash => keyboard.slashKey,
                KeyCode.BackQuote => keyboard.backquoteKey,
                KeyCode.Alpha0 => keyboard.digit0Key,
                KeyCode.Alpha1 => keyboard.digit1Key,
                KeyCode.Alpha2 => keyboard.digit2Key,
                KeyCode.Alpha3 => keyboard.digit3Key,
                KeyCode.Alpha4 => keyboard.digit4Key,
                KeyCode.Alpha5 => keyboard.digit5Key,
                KeyCode.Alpha6 => keyboard.digit6Key,
                KeyCode.Alpha7 => keyboard.digit7Key,
                KeyCode.Alpha8 => keyboard.digit8Key,
                KeyCode.Alpha9 => keyboard.digit9Key,
                _ => null
            };
        }
#endif
    }
}
