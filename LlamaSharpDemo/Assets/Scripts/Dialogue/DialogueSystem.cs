using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using DoodleDiplomacy.Data;

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
            if (!GetClickThisFrame()) return;

            if (_isTyping)
                _clickedWhileTyping = true;
            else
                _clickPending = true;
        }

        public void PlaySequence(DialogueSequence sequence)
        {
            if (sequence == null) return;
            if (_playbackCoroutine != null) StopCoroutine(_playbackCoroutine);
            _playbackCoroutine = StartCoroutine(PlaySequenceRoutine(sequence));
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

        private IEnumerator PlaySequenceRoutine(DialogueSequence sequence)
        {
            foreach (var line in sequence.lines)
                yield return StartCoroutine(PlayLine(line));

            HideAll();
            _playbackCoroutine = null;
            OnSequenceComplete?.Invoke();
        }

        private IEnumerator PlayLine(DialogueLineData line)
        {
            System.Action<string> textSetter;

            if (line.displayMode == DisplayMode.WorldSpace)
            {
                worldSpaceDialogue?.Show("", null);
                textSetter = t => worldSpaceDialogue?.SetText(t);
            }
            else
            {
                subtitleDisplay?.Show(line.characterID, "");
                textSetter = t => subtitleDisplay?.SetText(t);
            }

            yield return StartCoroutine(TypeText(line.text, textSetter));

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

        private void HideAll()
        {
            worldSpaceDialogue?.Hide();
            subtitleDisplay?.Hide();
        }

        private bool GetClickThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            var mouse = Mouse.current;
            return mouse != null && mouse.leftButton.wasPressedThisFrame;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }
    }
}
