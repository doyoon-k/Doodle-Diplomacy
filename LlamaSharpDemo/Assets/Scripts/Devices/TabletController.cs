using System.Collections;
using DoodleDiplomacy.Core;
using UnityEngine;
using UnityEngine.Events;

namespace DoodleDiplomacy.Devices
{
    public enum TabletState { OnTable, Raising, Raised, Lowering }

    public class TabletController : MonoBehaviour
    {
        [Header("Animation")]
        [SerializeField] private Vector3 raisedOffset = new(0f, 0.5f, -0.3f);
        [SerializeField] private Vector3 raisedEuler = new(-30f, 0f, 0f);
        [SerializeField] private float raiseDuration = 0.4f;
        [SerializeField] private float lowerDuration = 0.3f;

        [Header("Drawing")]
        [SerializeField] private DrawingBoardController drawingBoard;

        [Header("Events")]
        public UnityEvent OnRaised = new();
        public UnityEvent OnLowered = new();

        private Vector3 _originalLocalPos;
        private Quaternion _originalLocalRot;
        private Coroutine _animRoutine;
        private bool _enableDrawingWhenRaised;
        private TabletDrawingHudController _drawingHud;

        public TabletState CurrentState { get; private set; } = TabletState.OnTable;

        private void Awake()
        {
            _originalLocalPos = transform.localPosition;
            _originalLocalRot = transform.localRotation;

            if (drawingBoard == null)
            {
                drawingBoard = GetComponent<DrawingBoardController>();
            }

            EnsureDrawingHud();
            SetDrawingEnabled(false);
        }

        public void Raise()
        {
            Raise(enableDrawingInput: true);
        }

        public void Raise(bool enableDrawingInput)
        {
            _enableDrawingWhenRaised = enableDrawingInput;

            if (CurrentState is TabletState.Raised or TabletState.Raising)
            {
                if (CurrentState == TabletState.Raised)
                {
                    SetDrawingEnabled(_enableDrawingWhenRaised);
                }

                return;
            }

            StartAnim(
                TabletState.Raising,
                TabletState.Raised,
                _originalLocalPos + raisedOffset,
                Quaternion.Euler(raisedEuler),
                raiseDuration,
                () =>
                {
                    SetDrawingEnabled(_enableDrawingWhenRaised);
                    OnRaised?.Invoke();
                });
        }

        public void Lower()
        {
            if (CurrentState is TabletState.OnTable or TabletState.Lowering)
            {
                return;
            }

            SetDrawingEnabled(false);
            StartAnim(
                TabletState.Lowering,
                TabletState.OnTable,
                _originalLocalPos,
                _originalLocalRot,
                lowerDuration,
                () => OnLowered?.Invoke());
        }

        public void OnGameStateChanged(GameState state)
        {
            _drawingHud?.OnGameStateChanged(state);

            switch (state)
            {
                case GameState.Drawing:
                    Raise(enableDrawingInput: true);
                    break;
                case GameState.PreviewReady:
                case GameState.PreviewAnalyzing:
                case GameState.Preview:
                    Raise(enableDrawingInput: false);
                    break;
                default:
                    Lower();
                    break;
            }
        }

        [ContextMenu("Test: Raise")]
        private void TestRaise() => Raise();

        [ContextMenu("Test: Lower")]
        private void TestLower() => Lower();

        private void StartAnim(
            TabletState during,
            TabletState after,
            Vector3 targetPos,
            Quaternion targetRot,
            float duration,
            System.Action onComplete)
        {
            if (_animRoutine != null)
            {
                StopCoroutine(_animRoutine);
            }

            CurrentState = during;
            _animRoutine = StartCoroutine(AnimRoutine(targetPos, targetRot, duration, after, onComplete));
        }

        private IEnumerator AnimRoutine(
            Vector3 targetPos,
            Quaternion targetRot,
            float duration,
            TabletState finalState,
            System.Action onComplete)
        {
            Vector3 startPos = transform.localPosition;
            Quaternion startRot = transform.localRotation;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                transform.localPosition = Vector3.Lerp(startPos, targetPos, t);
                transform.localRotation = Quaternion.Slerp(startRot, targetRot, t);
                yield return null;
            }

            transform.localPosition = targetPos;
            transform.localRotation = targetRot;
            CurrentState = finalState;
            _animRoutine = null;
            onComplete?.Invoke();
        }

        private void SetDrawingEnabled(bool enabled)
        {
            if (drawingBoard != null)
            {
                drawingBoard.enabled = enabled;
            }
        }

        private void EnsureDrawingHud()
        {
            _drawingHud = GetComponent<TabletDrawingHudController>();
            if (_drawingHud == null)
            {
                _drawingHud = gameObject.AddComponent<TabletDrawingHudController>();
            }

            _drawingHud.Initialize(drawingBoard);
        }
    }
}
