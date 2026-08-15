using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoodleDiplomacy.Gameplay
{
    public sealed class GameFlowDirector : MonoBehaviour, IGameFlowController, IGameFlowPreloader
    {
        [Tooltip("Ordered game flow asset defining scenes and default modes to load.")]
        [SerializeField] private GameFlowAsset gameFlow;
        [Tooltip("Gameplay mode host that receives loaded scene references and mode transitions.")]
        [SerializeField] private GameplayModeHost gameplayModeHost;
        [Tooltip("Automatically load the first flow entry when this director starts.")]
        [SerializeField] private bool loadFirstEntryOnStart;

        private int _currentEntryIndex = -1;
        private Scene _loadedEntryScene;
        private Coroutine _loadRoutine;
        private Coroutine _preloadRoutine;
        private int _preloadingEntryIndex = -1;
        private int _preloadedEntryIndex = -1;
        private Scene _preloadedEntryScene;
        private string _preloadingSceneName;
        private bool _preloadedSceneSuspended;
        private readonly List<GameObject> _preloadedActiveRoots = new();

        public int CurrentEntryIndex => _currentEntryIndex;
        public FlowEntryDefinition CurrentEntry =>
            gameFlow != null &&
            _currentEntryIndex >= 0 &&
            _currentEntryIndex < gameFlow.entries.Length
                ? gameFlow.entries[_currentEntryIndex]
                : null;

        private void Start()
        {
            if (loadFirstEntryOnStart)
            {
                LoadEntry(0);
            }
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandlePreloadedSceneLoaded;
        }

        public void LoadEntry(int index)
        {
            LoadEntry(index, GameplayModeExitReason.Replaced);
        }

        private void LoadEntry(int index, GameplayModeExitReason outgoingExitReason)
        {
            if (gameFlow == null || gameFlow.entries == null)
            {
                Debug.LogError("[GameFlowDirector] Game flow is not assigned.", this);
                return;
            }

            if (index < 0 || index >= gameFlow.entries.Length)
            {
                Debug.LogError($"[GameFlowDirector] Flow entry index '{index}' is out of range.", this);
                return;
            }

            if (_loadRoutine != null)
            {
                StopCoroutine(_loadRoutine);
            }

            _loadRoutine = StartCoroutine(
                LoadEntryRoutine(index, outgoingExitReason));
        }

        public void LoadNextEntry()
        {
            LoadEntry(
                _currentEntryIndex + 1,
                GameplayModeExitReason.Completed);
        }

        public void CompleteCurrentEntry()
        {
            LoadEntry(
                _currentEntryIndex + 1,
                GameplayModeExitReason.Completed);
        }

        public void PreloadNextEntry()
        {
            int nextIndex = _currentEntryIndex + 1;
            if (!TryGetEntry(nextIndex, out FlowEntryDefinition definition))
            {
                return;
            }

            if (_loadedEntryScene.IsValid() &&
                _loadedEntryScene.isLoaded &&
                string.Equals(
                    _loadedEntryScene.name,
                    definition.sceneName,
                    System.StringComparison.Ordinal))
            {
                return;
            }

            if (_loadRoutine != null ||
                _preloadedEntryIndex == nextIndex ||
                _preloadingEntryIndex == nextIndex)
            {
                return;
            }

            if (_preloadRoutine != null || _preloadedEntryScene.IsValid())
            {
                Debug.LogWarning(
                    "[GameFlowDirector] A different flow entry is already being preloaded.",
                    this);
                return;
            }

            _preloadRoutine = StartCoroutine(
                PreloadEntryRoutine(nextIndex, definition));
        }

        private IEnumerator LoadEntryRoutine(
            int index,
            GameplayModeExitReason outgoingExitReason)
        {
            if (!TryGetEntry(index, out FlowEntryDefinition definition))
            {
                Debug.LogError("[GameFlowDirector] Flow entry definition or scene name is missing.", this);
                _loadRoutine = null;
                yield break;
            }

            gameplayModeHost = gameplayModeHost != null ? gameplayModeHost : GameplayModeHost.Instance;
            if (gameplayModeHost == null)
            {
                Debug.LogError("[GameFlowDirector] GameplayModeHost is missing.", this);
                _loadRoutine = null;
                yield break;
            }

            if (_loadedEntryScene.IsValid() &&
                _loadedEntryScene.isLoaded &&
                string.Equals(
                    _loadedEntryScene.name,
                    definition.sceneName,
                    System.StringComparison.Ordinal))
            {
                gameplayModeHost.ExitActiveMode(outgoingExitReason);
                if (!TryEnterLoadedEntry(
                        index,
                        definition,
                        _loadedEntryScene,
                        wasPreloaded: false,
                        handoffState: null))
                {
                    _loadRoutine = null;
                    yield break;
                }

                _loadRoutine = null;
                yield break;
            }

            if (_preloadingEntryIndex == index)
            {
                while (_preloadRoutine != null)
                {
                    yield return null;
                }
            }

            bool usePreloadedScene =
                _preloadedEntryIndex == index &&
                _preloadedEntryScene.IsValid() &&
                _preloadedEntryScene.isLoaded;

            if (usePreloadedScene)
            {
                yield return ActivatePreloadedEntryRoutine(
                    index,
                    definition,
                    outgoingExitReason);
                _loadRoutine = null;
                yield break;
            }

            gameplayModeHost.ExitActiveMode(outgoingExitReason);

            if (_loadedEntryScene.IsValid() && definition.unloadPreviousScene)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(_loadedEntryScene);
                if (unload != null)
                {
                    yield return unload;
                }
            }

            AsyncOperation load = SceneManager.LoadSceneAsync(definition.sceneName, LoadSceneMode.Additive);
            if (load == null)
            {
                Debug.LogError($"[GameFlowDirector] Failed to load scene '{definition.sceneName}'.", this);
                _loadRoutine = null;
                yield break;
            }

            yield return load;

            _loadedEntryScene = SceneManager.GetSceneByName(definition.sceneName);
            if (!_loadedEntryScene.IsValid())
            {
                Debug.LogError($"[GameFlowDirector] Loaded scene '{definition.sceneName}' is invalid.", this);
                _loadRoutine = null;
                yield break;
            }

            if (!TryEnterLoadedEntry(
                    index,
                    definition,
                    _loadedEntryScene,
                    wasPreloaded: false,
                    handoffState: null))
            {
                _loadRoutine = null;
                yield break;
            }

            _loadRoutine = null;
        }

        private IEnumerator PreloadEntryRoutine(
            int index,
            FlowEntryDefinition definition)
        {
            _preloadingEntryIndex = index;
            _preloadingSceneName = definition.sceneName;
            _preloadedEntryScene = default;
            _preloadedActiveRoots.Clear();
            _preloadedSceneSuspended = false;
            SceneManager.sceneLoaded += HandlePreloadedSceneLoaded;

            AsyncOperation load = SceneManager.LoadSceneAsync(
                definition.sceneName,
                LoadSceneMode.Additive);
            if (load == null)
            {
                SceneManager.sceneLoaded -= HandlePreloadedSceneLoaded;
                Debug.LogError(
                    $"[GameFlowDirector] Failed to preload scene '{definition.sceneName}'.",
                    this);
                ResetPreloadTracking();
                yield break;
            }

            yield return load;
            SceneManager.sceneLoaded -= HandlePreloadedSceneLoaded;

            if (!_preloadedEntryScene.IsValid())
            {
                _preloadedEntryScene = SceneManager.GetSceneByName(definition.sceneName);
            }

            if (!_preloadedEntryScene.IsValid() || !_preloadedEntryScene.isLoaded)
            {
                Debug.LogError(
                    $"[GameFlowDirector] Preloaded scene '{definition.sceneName}' is invalid.",
                    this);
                ResetPreloadTracking();
                yield break;
            }

            if (!_preloadedSceneSuspended)
            {
                SuspendSceneRoots(_preloadedEntryScene, _preloadedActiveRoots);
                _preloadedSceneSuspended = true;
            }

            _preloadedEntryIndex = index;
            _preloadingEntryIndex = -1;
            _preloadingSceneName = null;
            _preloadRoutine = null;
            Debug.Log(
                $"[GameFlowDirector] Preloaded flow scene '{definition.sceneName}'.",
                this);
        }

        private IEnumerator ActivatePreloadedEntryRoutine(
            int index,
            FlowEntryDefinition definition,
            GameplayModeExitReason outgoingExitReason)
        {
            Scene previousScene = _loadedEntryScene;
            IGameplaySceneInstaller previousInstaller = previousScene.IsValid()
                ? FindInstallerInScene(previousScene)
                : null;
            object handoffState =
                (previousInstaller as IGameplaySceneHandoff)?.CaptureHandoffState();

            gameplayModeHost.ExitActiveMode(outgoingExitReason);
            if (previousScene.IsValid() && definition.unloadPreviousScene)
            {
                SuspendSceneRoots(previousScene, activeRoots: null);
            }

            Scene targetScene = _preloadedEntryScene;
            RestorePreloadedSceneRoots(targetScene);
            _loadedEntryScene = targetScene;

            bool entered = TryEnterLoadedEntry(
                index,
                definition,
                targetScene,
                wasPreloaded: true,
                handoffState);
            ClearCompletedPreload();
            if (!entered)
            {
                yield break;
            }

            if (previousScene.IsValid() && definition.unloadPreviousScene)
            {
                AsyncOperation unload = SceneManager.UnloadSceneAsync(previousScene);
                if (unload != null)
                {
                    yield return unload;
                }
            }
        }

        private bool TryEnterLoadedEntry(
            int index,
            FlowEntryDefinition definition,
            Scene scene,
            bool wasPreloaded,
            object handoffState)
        {
            SceneManager.SetActiveScene(scene);
            IGameplaySceneInstaller installer = FindInstallerInScene(scene);
            if (installer == null)
            {
                Debug.LogError($"[GameFlowDirector] Scene '{definition.sceneName}' has no IGameplaySceneInstaller.", this);
                return false;
            }

            if (installer is IGameplaySceneEntryPreparer preparer)
            {
                preparer.PrepareEntry(definition);
            }

            bool appliedHandoffState = false;
            if (handoffState != null && installer is IGameplaySceneHandoff handoffReceiver)
            {
                handoffReceiver.ApplyHandoffState(handoffState);
                appliedHandoffState = true;
            }

            MonoBehaviour modeBehaviour = installer is IGameplaySceneModeResolver resolver
                ? resolver.GetModeBehaviour(definition)
                : installer.GetDefaultModeBehaviour();
            modeBehaviour = modeBehaviour != null ? modeBehaviour : installer.GetDefaultModeBehaviour();
            GameplayModeContext context = installer.CreateContext(gameplayModeHost);
            context.Services.Register<IGameFlowController>(this);
            context.Services.Register<IGameFlowPreloader>(this);
            context.Services.Register(definition);
            context.Services.Register(
                new GameplaySceneTransitionContext(
                    wasPreloaded,
                    appliedHandoffState));

            if (!gameplayModeHost.EnterMode(modeBehaviour, context))
            {
                Debug.LogError($"[GameFlowDirector] Failed to enter gameplay mode for '{definition.entryId}'.", this);
                return false;
            }

            _currentEntryIndex = index;
            if (definition.autoStartSession && gameplayModeHost.ActiveMode is IGameplaySessionController session)
            {
                session.StartGame(definition.startSessionWithIntro);
            }

            return true;
        }

        private void HandlePreloadedSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (_preloadingEntryIndex < 0 ||
                !string.Equals(scene.name, _preloadingSceneName, System.StringComparison.Ordinal))
            {
                return;
            }

            _preloadedEntryScene = scene;
            SuspendSceneRoots(scene, _preloadedActiveRoots);
            _preloadedSceneSuspended = true;
        }

        private static void SuspendSceneRoots(
            Scene scene,
            List<GameObject> activeRoots)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root == null || !root.activeSelf)
                {
                    continue;
                }

                activeRoots?.Add(root);
                root.SetActive(false);
            }
        }

        private void RestorePreloadedSceneRoots(Scene scene)
        {
            for (int i = 0; i < _preloadedActiveRoots.Count; i++)
            {
                GameObject root = _preloadedActiveRoots[i];
                if (root != null && root.scene == scene)
                {
                    root.SetActive(true);
                }
            }

            _preloadedActiveRoots.Clear();
            _preloadedSceneSuspended = false;
        }

        private bool TryGetEntry(int index, out FlowEntryDefinition definition)
        {
            definition = null;
            if (gameFlow == null || gameFlow.entries == null ||
                index < 0 || index >= gameFlow.entries.Length)
            {
                return false;
            }

            definition = gameFlow.entries[index];
            return definition != null &&
                   !string.IsNullOrWhiteSpace(definition.sceneName);
        }

        private void ClearCompletedPreload()
        {
            _preloadingEntryIndex = -1;
            _preloadedEntryIndex = -1;
            _preloadedEntryScene = default;
            _preloadingSceneName = null;
            _preloadedActiveRoots.Clear();
            _preloadedSceneSuspended = false;
            _preloadRoutine = null;
        }

        private void ResetPreloadTracking()
        {
            ClearCompletedPreload();
        }

        private static IGameplaySceneInstaller FindInstallerInScene(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour is IGameplaySceneInstaller installer)
                    {
                        return installer;
                    }
                }
            }

            return null;
        }
    }
}
