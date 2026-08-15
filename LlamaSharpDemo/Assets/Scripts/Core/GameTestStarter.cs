using System.Collections;
using DoodleDiplomacy.Gameplay;
using DoodleDiplomacy.Gameplay.FirstContact;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
namespace DoodleDiplomacy.Core
{
    /// <summary>
    /// Editor-only entry point that loads the authored Facility and skips directly
    /// to its seated terminal gameplay state.
    /// </summary>
    public sealed class GameTestStarter : MonoBehaviour
    {
        [Tooltip("Load the canonical First Contact Facility and enter its gameplay without dialogue.")]
        [SerializeField] private bool launchFirstContactFacilityGameplay;
        [SerializeField] private string firstContactFacilitySceneName = "FC_Intro_Facility";

        [Tooltip("false = WaitingForRound부터 시작 (빠른 테스트), true = Intro부터 시작")]
        [SerializeField] private bool isFirstPlay = false;

        private IEnumerator Start()
        {
            if (launchFirstContactFacilityGameplay)
            {
                string sceneName = string.IsNullOrWhiteSpace(firstContactFacilitySceneName)
                    ? "FC_Intro_Facility"
                    : firstContactFacilitySceneName.Trim();
                if (!Application.CanStreamedLevelBeLoaded(sceneName))
                {
                    Debug.LogError(
                        $"[GameTestStarter] Scene '{sceneName}' is not available in Build Settings.",
                        this);
                    yield break;
                }

                FirstContactGameplayTestLaunchRequest.Request(sceneName);
                SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
                yield break;
            }

            GameplayModeHost host = GameplayModeHost.Instance;
            host?.EnsureDefaultModeEntered();
            if (host != null && host.ActiveMode is IGameplaySessionController session)
            {
                session.StartGame(isFirstPlay);
                yield break;
            }

            Debug.LogError("[GameTestStarter] Gameplay session controller가 없습니다!");
        }
    }
}

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    internal static class FirstContactGameplayTestLaunchRequest
    {
        private static string _expectedSceneName = string.Empty;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            _expectedSceneName = string.Empty;
        }

        public static void Request(string expectedSceneName)
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            _expectedSceneName = expectedSceneName?.Trim() ?? string.Empty;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode _)
        {
            if (string.IsNullOrWhiteSpace(_expectedSceneName) ||
                !string.Equals(scene.name, _expectedSceneName, System.StringComparison.Ordinal))
            {
                return;
            }

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            _expectedSceneName = string.Empty;

            FirstContactIntroSceneInstaller installer = FindInstaller(scene);
            if (installer == null ||
                !installer.TryStartEmbeddedGameplayForDirectPreview(
                    startWithIntro: false,
                    suppressNarrativeCues: true))
            {
                Debug.LogError(
                    "[FirstContactGameplayTest] Could not enter seated Facility gameplay test mode.");
            }
        }

        private static FirstContactIntroSceneInstaller FindInstaller(Scene scene)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                FirstContactIntroSceneInstaller installer =
                    roots[i].GetComponentInChildren<FirstContactIntroSceneInstaller>(true);
                if (installer != null)
                {
                    return installer;
                }
            }

            return null;
        }
    }
}
#endif
