using System.Collections;
using DoodleDiplomacy.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoodleDiplomacy.Gameplay
{
    public sealed class GameFlowDirector : MonoBehaviour, IGameFlowController
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

        public void LoadEntry(int index)
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

            _loadRoutine = StartCoroutine(LoadEntryRoutine(index));
        }

        public void LoadNextEntry()
        {
            LoadEntry(_currentEntryIndex + 1);
        }

        public void CompleteCurrentEntry()
        {
            LoadNextEntry();
        }

        private IEnumerator LoadEntryRoutine(int index)
        {
            FlowEntryDefinition definition = gameFlow.entries[index];
            if (definition == null || string.IsNullOrWhiteSpace(definition.sceneName))
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

            gameplayModeHost.ExitActiveMode();

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

            SceneManager.SetActiveScene(_loadedEntryScene);
            IGameplaySceneInstaller installer = FindInstallerInScene(_loadedEntryScene);
            if (installer == null)
            {
                Debug.LogError($"[GameFlowDirector] Scene '{definition.sceneName}' has no IGameplaySceneInstaller.", this);
                _loadRoutine = null;
                yield break;
            }

            MonoBehaviour modeBehaviour = installer.GetDefaultModeBehaviour();
            GameplayModeContext context = installer.CreateContext(gameplayModeHost);
            context.Services.Register<IGameFlowController>(this);
            context.Services.Register(definition);

            if (!gameplayModeHost.EnterMode(modeBehaviour, context))
            {
                Debug.LogError($"[GameFlowDirector] Failed to enter gameplay mode for '{definition.entryId}'.", this);
                _loadRoutine = null;
                yield break;
            }

            _currentEntryIndex = index;
            if (definition.autoStartSession && gameplayModeHost.ActiveMode is IGameplaySessionController session)
            {
                session.StartGame(definition.startSessionWithIntro);
            }

            _loadRoutine = null;
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
