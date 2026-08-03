using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactIntroSceneInstaller : MonoBehaviour,
        IGameplaySceneInstaller,
        IGameplaySceneHandoff
    {
        private sealed class ElevatorHandoffState
        {
            public FirstContactIntroPlayerController Player;
            public Vector3 PlayerPositionInElevatorSpace;
            public Quaternion PlayerRotationInElevatorSpace;
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

            FirstContactSecretElevatorSequence surfaceElevator =
                FindComponentInScene<FirstContactSecretElevatorSequence>();
            Transform elevatorSpace = surfaceElevator != null
                ? surfaceElevator.ElevatorTransitionSpace
                : null;
            if (elevatorSpace == null)
            {
                elevatorSpace = references.ExitPoint;
            }

            var handoffState = new ElevatorHandoffState
            {
                Player = player,
                PlayerPositionInElevatorSpace =
                    elevatorSpace.InverseTransformPoint(player.transform.position),
                PlayerRotationInElevatorSpace =
                    Quaternion.Inverse(elevatorSpace.rotation) *
                    player.transform.rotation
            };

            // The player is already detached from the car by this point. Make it a
            // root once more so the old Surface roots can be suspended and unloaded
            // without disabling the rig that the player is still controlling.
            player.transform.SetParent(null, true);
            defaultModeBehaviour?.SequenceController?
                .ReleasePlayerForSceneHandoff(player);
            DontDestroyOnLoad(player.gameObject);
            return handoffState;
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

            FirstContactIntroPlayerController authoredFacilityPlayer =
                FindComponentInScene<FirstContactIntroPlayerController>();
            FirstContactIntroPlayerController player = elevatorState.Player;
            if (player == null)
            {
                return;
            }

            FirstContactFacilityElevatorArrival facilityElevator =
                FindComponentInScene<FirstContactFacilityElevatorArrival>();
            Transform elevatorSpace = facilityElevator != null
                ? facilityElevator.transform
                : references.PlayerSpawn;
            Vector3 targetPosition = elevatorSpace.TransformPoint(
                elevatorState.PlayerPositionInElevatorSpace);
            Quaternion targetRotation =
                elevatorSpace.rotation *
                elevatorState.PlayerRotationInElevatorSpace;

            player.RepositionPreservingView(targetPosition, targetRotation);
            if (player.gameObject.scene != gameObject.scene)
            {
                SceneManager.MoveGameObjectToScene(
                    player.gameObject,
                    gameObject.scene);
            }

            FirstContactIntroHud facilityHud =
                FindComponentInScene<FirstContactIntroHud>();
            FirstContactIntroSequenceController sequence =
                defaultModeBehaviour != null
                    ? defaultModeBehaviour.SequenceController
                    : FindComponentInScene<FirstContactIntroSequenceController>();
            sequence?.AdoptPlayerFromSceneHandoff(player, facilityHud);

            // Keep the authored Facility rig for direct scene testing. During the
            // real Surface handoff the persistent rig owns the camera and listener.
            if (authoredFacilityPlayer != null &&
                authoredFacilityPlayer != player)
            {
                authoredFacilityPlayer.gameObject.SetActive(false);
            }
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
