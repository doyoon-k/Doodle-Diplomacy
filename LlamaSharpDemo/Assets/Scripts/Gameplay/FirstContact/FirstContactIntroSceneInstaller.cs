using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactIntroSceneInstaller : MonoBehaviour,
        IGameplaySceneInstaller,
        IGameplaySceneHandoff
    {
        private sealed class ElevatorHandoffState
        {
            public Quaternion ViewRotationInElevatorSpace;
        }

        [SerializeField] private string sceneId = "first-contact-intro";
        [SerializeField] private FirstContactIntroMode defaultModeBehaviour;

        public string SceneId => string.IsNullOrWhiteSpace(sceneId) ? gameObject.scene.name : sceneId;

        public void Configure(string id, FirstContactIntroMode mode)
        {
            sceneId = id;
            defaultModeBehaviour = mode;
        }

        public GameplayModeContext CreateContext(GameplayModeHost host)
        {
            return new GameplayModeContext(
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        public MonoBehaviour GetDefaultModeBehaviour()
        {
            return defaultModeBehaviour;
        }

        public object CaptureHandoffState()
        {
            FirstContactIntroSceneReferences references =
                defaultModeBehaviour != null
                    ? defaultModeBehaviour.SceneReferences
                    : null;
            if (references == null ||
                references.Segment != FirstContactIntroSegment.Surface ||
                references.ExitPoint == null)
            {
                return null;
            }

            FirstContactIntroPlayerController player =
                FindComponentInScene<FirstContactIntroPlayerController>();
            if (player == null || player.ViewCamera == null)
            {
                return null;
            }

            return new ElevatorHandoffState
            {
                ViewRotationInElevatorSpace =
                    Quaternion.Inverse(references.ExitPoint.rotation) *
                    player.ViewCamera.transform.rotation
            };
        }

        public void ApplyHandoffState(object handoffState)
        {
            if (handoffState is not ElevatorHandoffState elevatorState)
            {
                return;
            }

            FirstContactIntroSceneReferences references =
                defaultModeBehaviour != null
                    ? defaultModeBehaviour.SceneReferences
                    : null;
            if (references == null ||
                references.Segment != FirstContactIntroSegment.Facility ||
                references.PlayerSpawn == null)
            {
                return;
            }

            FirstContactIntroPlayerController player =
                FindComponentInScene<FirstContactIntroPlayerController>();
            player?.ApplyViewWorldRotation(
                references.PlayerSpawn.rotation *
                elevatorState.ViewRotationInElevatorSpace);
        }

        private T FindComponentInScene<T>() where T : Component
        {
            foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }
    }
}
