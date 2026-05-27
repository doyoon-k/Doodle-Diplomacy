using System;
using DoodleDiplomacy.Core;
using DoodleDiplomacy.Interaction;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay
{
    public class GameplayModeHost : MonoBehaviour, IGameplayStateObservable
    {
        public static GameplayModeHost Instance { get; private set; }

        [Header("Mode")]
        [Tooltip("Scene reference hub that supplies shared gameplay dependencies for the active mode.")]
        [SerializeField] private SceneReferenceHub sceneReferences;
        [Tooltip("MonoBehaviour implementing IGameplayMode that should be entered by default.")]
        [SerializeField] private MonoBehaviour defaultModeBehaviour;
        [Tooltip("Enter the default gameplay mode automatically on Start.")]
        [SerializeField] private bool enterDefaultModeOnStart = true;
        [Tooltip("Validate required SceneReferenceHub references during Awake and log missing assignments.")]
        [SerializeField] private bool validateSceneReferencesOnAwake = true;

        private GameplayModeContext _context;
        private IGameplayMode _activeMode;
        private IGameplayStateObservable _activeStateObservable;
        private bool _hasEnteredMode;
        private GameplayModeContext _activeContext;

        public event Action<GameState> StateChanged;

        public IGameplayMode ActiveMode => _activeMode;
        public string ActiveModeId => _activeMode != null ? _activeMode.ModeId : string.Empty;
        public GameState CurrentState => _activeMode != null ? _activeMode.CurrentState : GameState.Title;
        public GameplayModeContext Context => _activeContext ?? _context;
        public bool HasActiveMode => _activeMode != null && _hasEnteredMode;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;

            if (sceneReferences == null)
            {
                sceneReferences = GetComponent<SceneReferenceHub>();
            }

            if (validateSceneReferencesOnAwake && sceneReferences != null)
            {
                sceneReferences.ValidateReferences();
            }

            if (sceneReferences != null)
            {
                sceneReferences.ConfigureRuntime(this);
                _context = sceneReferences.CreateContext(this);
                if (defaultModeBehaviour == null)
                {
                    defaultModeBehaviour = sceneReferences.GetDefaultModeBehaviour();
                }
            }

            if (defaultModeBehaviour == null)
            {
                defaultModeBehaviour = FindModeBehaviourOnObject();
            }
        }

        private void Start()
        {
            EnsureDefaultModeEntered();
        }

        private void Update()
        {
            _activeMode?.Tick(Time.deltaTime);
        }

        private void OnDestroy()
        {
            ExitActiveMode();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public bool EnterMode(MonoBehaviour modeBehaviour)
        {
            return EnterMode(modeBehaviour, null);
        }

        public bool EnsureDefaultModeEntered()
        {
            if (!enterDefaultModeOnStart)
            {
                return HasActiveMode;
            }

            if (HasActiveMode)
            {
                return true;
            }

            return defaultModeBehaviour != null && EnterMode(defaultModeBehaviour);
        }

        public bool EnterMode(MonoBehaviour modeBehaviour, GameplayModeContext contextOverride)
        {
            if (modeBehaviour == null)
            {
                Debug.LogError("[GameplayModeHost] Cannot enter a null gameplay mode.", this);
                return false;
            }

            if (modeBehaviour is not IGameplayMode mode)
            {
                Debug.LogError($"[GameplayModeHost] '{modeBehaviour.name}' does not implement IGameplayMode.", modeBehaviour);
                return false;
            }

            GameplayModeContext context = contextOverride ?? _context;
            if (context == null)
            {
                Debug.LogError("[GameplayModeHost] Cannot enter gameplay mode because SceneReferenceHub/context is missing.", this);
                return false;
            }

            ExitActiveMode();
            _activeMode = mode;
            _activeContext = context;
            _activeStateObservable = mode as IGameplayStateObservable;
            if (_activeStateObservable != null)
            {
                _activeStateObservable.StateChanged += HandleActiveModeStateChanged;
            }

            _activeMode.Enter(context);
            _hasEnteredMode = true;
            StateChanged?.Invoke(_activeMode.CurrentState);
            Debug.Log($"[GameplayModeHost] Entered mode '{_activeMode.ModeId}'.", this);
            return true;
        }

        public void ExitActiveMode()
        {
            if (_activeStateObservable != null)
            {
                _activeStateObservable.StateChanged -= HandleActiveModeStateChanged;
                _activeStateObservable = null;
            }

            if (_activeMode != null)
            {
                _activeMode.Exit();
            }

            _activeMode = null;
            _activeContext = null;
            _hasEnteredMode = false;
        }

        public bool TryHandleInteraction(InteractionType type, InteractableObject source)
        {
            if (_activeMode == null || !_hasEnteredMode)
            {
                return false;
            }

            _activeMode.HandleInteraction(type, source);
            return true;
        }

        private void HandleActiveModeStateChanged(GameState state)
        {
            StateChanged?.Invoke(state);
        }

        private MonoBehaviour FindModeBehaviourOnObject()
        {
            foreach (MonoBehaviour behaviour in GetComponents<MonoBehaviour>())
            {
                if (behaviour != this && behaviour is IGameplayMode)
                {
                    return behaviour;
                }
            }

            return null;
        }
    }
}
