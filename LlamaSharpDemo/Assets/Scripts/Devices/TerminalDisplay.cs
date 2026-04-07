using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using DoodleDiplomacy.Core;

namespace DoodleDiplomacy.Devices
{
    public class TerminalDisplay : MonoBehaviour
    {
        [Header("Display")]
        [SerializeField] private TextMeshProUGUI textMesh;
        [SerializeField] private GameObject screenPanel;

        [Header("Typing")]
        [Tooltip("글자당 대기 시간 (초)")]
        [SerializeField] private float typingSpeed = 0.05f;
        [Tooltip("해석 노이즈 효과 — 랜덤 문자가 잠깐 표시됩니다")]
        [SerializeField] private bool useNoise = true;
        [SerializeField] private float noiseDisplayTime = 0.02f;

        [Header("Cursor")]
        [SerializeField] private bool showCursor = true;
        [SerializeField] private float cursorBlinkRate = 0.5f;

        [Header("Events")]
        public UnityEvent OnTypingComplete = new();

        private Coroutine _typingRoutine;
        private Coroutine _cursorRoutine;
        private string _currentText = "";
        private bool _isTyping;

        private static readonly char[] NoiseChars =
            "!@#$%^&*<>?/\\|~`0123456789ABCDEFXYZabcxyz".ToCharArray();

        public bool IsTyping() => _isTyping;

        private void Awake()
        {
            Clear();
        }

        // ── 공개 API ──────────────────────────────────────────────────────────

        public void ShowText(string text)
        {
            if (_typingRoutine != null) StopCoroutine(_typingRoutine);
            if (_cursorRoutine != null) { StopCoroutine(_cursorRoutine); _cursorRoutine = null; }
            _typingRoutine = StartCoroutine(TypingRoutine(text));
        }

        public void Clear()
        {
            if (_typingRoutine != null) { StopCoroutine(_typingRoutine); _typingRoutine = null; }
            if (_cursorRoutine != null) { StopCoroutine(_cursorRoutine); _cursorRoutine = null; }
            _isTyping = false;
            _currentText = "";
            if (textMesh != null) textMesh.text = "";
        }

        /// <summary>
        /// RoundManager.OnStateChanged에 연결.
        /// Interpreter 진입: 더미 텍스트 타이핑 시작.
        /// InterpreterReady/WaitingForRound: 화면 초기화.
        /// </summary>
        /// <summary>
        /// RoundManager.OnStateChanged 이벤트 수신용.
        /// Interpreter 상태의 텍스트는 AIPipelineBridge 결과를 받아
        /// RoundManager가 ShowText()를 직접 호출한다.
        /// 이 메서드는 상태 퇴장 시 정리(Clear)만 담당한다.
        /// </summary>
        public void OnGameStateChanged(GameState state)
        {
            switch (state)
            {
                case GameState.InterpreterReady:
                case GameState.WaitingForRound:
                    Clear();
                    break;
            }
        }

        // ── 내부 ─────────────────────────────────────────────────────────────

        private IEnumerator TypingRoutine(string fullText)
        {
            _isTyping = true;
            _currentText = "";

            if (showCursor)
                _cursorRoutine = StartCoroutine(CursorBlink());

            for (int i = 0; i < fullText.Length; i++)
            {
                // 노이즈 효과: 특수 문자가 잠깐 보였다 사라짐
                if (useNoise && fullText[i] != '\n' && fullText[i] != ' ' && Random.value < 0.25f)
                {
                    char noise = NoiseChars[Random.Range(0, NoiseChars.Length)];
                    if (textMesh != null)
                        textMesh.text = _currentText + noise + (showCursor ? "▌" : "");
                    yield return new WaitForSeconds(noiseDisplayTime);
                }

                _currentText += fullText[i];
                if (textMesh != null)
                    textMesh.text = _currentText + (showCursor ? "▌" : "");

                // 개행·공백은 빠르게 처리
                float delay = fullText[i] is '\n' or ' ' ? typingSpeed * 0.3f : typingSpeed;
                yield return new WaitForSeconds(delay);
            }

            _isTyping = false;
            _typingRoutine = null;
            // _cursorRoutine은 계속 유지해 커서 깜빡임 지속

            OnTypingComplete?.Invoke();
        }

        private IEnumerator CursorBlink()
        {
            bool visible = true;
            while (true)
            {
                yield return new WaitForSeconds(cursorBlinkRate);
                // 타이핑 중에는 TypingRoutine이 텍스트를 갱신하므로 개입하지 않음
                if (!_isTyping && textMesh != null)
                {
                    visible = !visible;
                    textMesh.text = _currentText + (visible ? "▌" : "");
                }
            }
        }

        // ── Inspector 컨텍스트 메뉴 테스트 ───────────────────────────────────

        [ContextMenu("Test: ShowDummyText")]
        private void TestShow() =>
            ShowText("[TRANSLATOR v1.0]\n> Decoding...\n> Hello, Ambassador!\n> _");

        [ContextMenu("Test: Clear")]
        private void TestClear() => Clear();
    }
}
