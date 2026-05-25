using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Localization;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DoodleDiplomacy.Dialogue
{
    public class DialogueSystem : MonoBehaviour
    {
        public static DialogueSystem Instance { get; private set; }

        [Header("Display")]
        [SerializeField] private WorldSpaceDialogue worldSpaceDialogue;
        [SerializeField] private SubtitleDisplay subtitleDisplay;

        [Header("Typing")]
        [Tooltip("초당 표시할 글자 수")]
        [SerializeField] private float typingSpeed = 30f;

        [Header("Events")]
        public UnityEvent OnSequenceComplete = new UnityEvent();

        private Coroutine _playbackCoroutine;
        private bool _isTyping;
        private bool _clickedWhileTyping;
        private bool _clickPending;

        public bool IsPlaying => _playbackCoroutine != null;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (!GetAdvanceThisFrame()) return;

            if (_isTyping)
                _clickedWhileTyping = true;
            else
                _clickPending = true;
        }

        public void PlaySequence(DialogueSequence sequence)
        {
            PlaySequence(sequence, Array.Empty<L10nArg>());
        }

        public void PlaySequence(DialogueSequence sequence, params L10nArg[] args)
        {
            if (sequence == null) return;
            if (_playbackCoroutine != null) StopCoroutine(_playbackCoroutine);
            _playbackCoroutine = StartCoroutine(PlaySequenceRoutine(sequence, args));
        }

        public IEnumerator PlaySequenceAndWait(DialogueSequence sequence, params L10nArg[] args)
        {
            PlaySequence(sequence, args);
            while (IsPlaying)
            {
                yield return null;
            }
        }

        public void StopSequence()
        {
            if (_playbackCoroutine != null)
            {
                StopCoroutine(_playbackCoroutine);
                _playbackCoroutine = null;
            }
            _isTyping = false;
            HideAll();
        }

        private IEnumerator PlaySequenceRoutine(DialogueSequence sequence, IReadOnlyList<L10nArg> args)
        {
            foreach (var line in sequence.lines)
                yield return StartCoroutine(PlayLine(line, args));

            HideAll();
            _playbackCoroutine = null;
            OnSequenceComplete?.Invoke();
        }

        private IEnumerator PlayLine(DialogueLineData line, IReadOnlyList<L10nArg> args)
        {
            System.Action<string> textSetter;
            string speaker = ResolveLocalizedSpeaker(line, args);
            string text = ResolveLocalizedText(line, args);

            if (line.displayMode == DisplayMode.WorldSpace)
            {
                worldSpaceDialogue?.Show("", null);
                textSetter = t => worldSpaceDialogue?.SetText(t);
            }
            else
            {
                subtitleDisplay?.Show(speaker, "");
                textSetter = t => subtitleDisplay?.SetText(t);
            }

            yield return StartCoroutine(TypeText(text, textSetter));

            // 타이핑 완료 후 진행 방식 처리
            switch (line.advanceType)
            {
                case AdvanceType.Click:
                    _clickPending = false;
                    while (!_clickPending) yield return null;
                    _clickPending = false;
                    break;

                case AdvanceType.Wait:
                    yield return new WaitForSeconds(line.autoDelay);
                    break;

                // AdvanceType.Auto: 타이핑 완료 즉시 다음 줄로
            }
        }

        private IEnumerator TypeText(string fullText, System.Action<string> setter)
        {
            fullText ??= string.Empty;
            _isTyping = true;
            _clickedWhileTyping = false;
            setter?.Invoke("");

            if (typingSpeed <= 0f)
            {
                setter?.Invoke(fullText);
                _isTyping = false;
                yield break;
            }

            float interval = 1f / typingSpeed;
            float elapsed = 0f;
            int charsShown = 0;

            while (charsShown < fullText.Length)
            {
                if (_clickedWhileTyping)
                {
                    setter?.Invoke(fullText);
                    _clickedWhileTyping = false;
                    break;
                }

                elapsed += Time.deltaTime;
                int target = Mathf.Min(Mathf.FloorToInt(elapsed / interval) + 1, fullText.Length);
                if (target > charsShown)
                {
                    charsShown = target;
                    setter?.Invoke(fullText.Substring(0, charsShown));
                }
                yield return null;
            }

            setter?.Invoke(fullText);
            _isTyping = false;
        }

        public static string ResolveLocalizedSpeaker(DialogueLineData line, IReadOnlyList<L10nArg> args = null)
        {
            return line == null
                ? string.Empty
                : ResolveLocalizedValue(line.speakerLocalizationKey, line.characterID, args);
        }

        public static string ResolveLocalizedText(DialogueLineData line, IReadOnlyList<L10nArg> args = null)
        {
            return line == null
                ? string.Empty
                : ResolveLocalizedValue(line.localizationKey, line.text, args);
        }

        private static string ResolveLocalizedValue(string key, string fallback, IReadOnlyList<L10nArg> args)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return FormatTemplate(fallback, args);
            }

            return L10n.T(key, fallback ?? string.Empty, ToArgArray(args));
        }

        private static string FormatTemplate(string template, IReadOnlyList<L10nArg> args)
        {
            if (string.IsNullOrEmpty(template) || args == null || args.Count == 0)
            {
                return template ?? string.Empty;
            }

            string result = template;
            for (int i = 0; i < args.Count; i++)
            {
                L10nArg arg = args[i];
                if (!string.IsNullOrWhiteSpace(arg.Key))
                {
                    result = result.Replace("{" + arg.Key + "}", arg.Value);
                }
            }

            return result;
        }

        private static L10nArg[] ToArgArray(IReadOnlyList<L10nArg> args)
        {
            if (args == null || args.Count == 0)
            {
                return Array.Empty<L10nArg>();
            }

            var array = new L10nArg[args.Count];
            for (int i = 0; i < args.Count; i++)
            {
                array[i] = args[i];
            }

            return array;
        }

        private void HideAll()
        {
            worldSpaceDialogue?.Hide();
            subtitleDisplay?.Hide();
        }

        private bool GetAdvanceThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            return (mouse != null && mouse.leftButton.wasPressedThisFrame) ||
                   (keyboard != null &&
                    (keyboard.spaceKey.wasPressedThisFrame ||
                     keyboard.enterKey.wasPressedThisFrame));
#else
            return Input.GetMouseButtonDown(0) ||
                   Input.GetKeyDown(KeyCode.Space) ||
                   Input.GetKeyDown(KeyCode.Return);
#endif
        }
    }
}
