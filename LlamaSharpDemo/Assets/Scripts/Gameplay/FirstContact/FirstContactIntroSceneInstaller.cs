using System;
using DoodleDiplomacy.Data;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactIntroSceneInstaller : MonoBehaviour,
        IGameplaySceneInstaller,
        IGameplaySceneModeResolver,
        IGameplaySceneEntryPreparer,
        IGameplaySceneHandoff
    {
        private const string TranslationModeTag = "first-contact-translation";

        private sealed class ElevatorHandoffState
        {
            public FirstContactIntroPlayerController Player;
            public Vector3 PlayerPositionInElevatorSpace;
            public Quaternion PlayerRotationInElevatorSpace;
            public FirstContactIntroGuideController Guide;
            public Vector3 GuidePositionInElevatorSpace;
            public Quaternion GuideRotationInElevatorSpace;
            public bool HasDialogue;
            public string DialogueSpeaker;
            public string DialogueText;
        }

        [SerializeField] private string sceneId = "first-contact-intro";
        [SerializeField] private FirstContactIntroMode defaultModeBehaviour;

        [Header("Embedded Meeting Gameplay")]
        [Tooltip("Gameplay references moved from GameScene into the Facility scene.")]
        [SerializeField] private SceneReferenceHub embeddedGameplayReferences;
        [Tooltip("Inactive camera/UI/gameplay systems enabled after the president takes the meeting seat.")]
        [SerializeField] private GameObject embeddedGameplayRoot;
        [Tooltip("Facility-only presentation roots hidden when the embedded meeting gameplay begins.")]
        [SerializeField] private GameObject[] introPresentationRoots = Array.Empty<GameObject>();

        private bool _embeddedGameplayPrepared;

        public string SceneId => string.IsNullOrWhiteSpace(sceneId) ? gameObject.scene.name : sceneId;

        private void Awake()
        {
            FirstContactIntroSceneReferences references =
                defaultModeBehaviour != null
                    ? defaultModeBehaviour.SceneReferences
                    : FindComponentInScene<FirstContactIntroSceneReferences>();
            FirstContactTemporaryAudio.Apply(
                references,
                FindComponentInScene<FirstContactSecretElevatorSequence>(),
                FindComponentInScene<FirstContactFacilityElevatorArrival>());
        }

        public void Configure(string id, FirstContactIntroMode mode)
        {
            sceneId = id;
            defaultModeBehaviour = mode;
        }

        public void ConfigureEmbeddedGameplay(
            SceneReferenceHub gameplayReferences,
            GameObject gameplayRoot,
            GameObject[] presentationRoots)
        {
            embeddedGameplayReferences = gameplayReferences;
            embeddedGameplayRoot = gameplayRoot;
            introPresentationRoots = presentationRoots ?? Array.Empty<GameObject>();
        }

        public void PrepareEntry(FlowEntryDefinition entry)
        {
            _embeddedGameplayPrepared = IsEmbeddedGameplayEntry(entry);
            if (embeddedGameplayRoot != null)
            {
                embeddedGameplayRoot.SetActive(_embeddedGameplayPrepared);
            }

            for (int i = 0; i < introPresentationRoots.Length; i++)
            {
                GameObject root = introPresentationRoots[i];
                if (root != null)
                {
                    root.SetActive(!_embeddedGameplayPrepared);
                }
            }

            FirstContactIntroSequenceController sequence =
                defaultModeBehaviour != null
                    ? defaultModeBehaviour.SequenceController
                    : FindComponentInScene<FirstContactIntroSequenceController>();
            if (sequence?.Player != null)
            {
                sequence.Player.gameObject.SetActive(!_embeddedGameplayPrepared);
            }

            if (sequence?.Guide != null)
            {
                // The Facility director is scene continuity, not an intro-only
                // presentation object. Keep the actor who walked in with the
                // player alive when the embedded meeting gameplay takes over.
                sequence.Guide.gameObject.SetActive(true);
            }

            if (sequence != null)
            {
                sequence.RebindMeetingCastContinuity();
            }
        }

        public GameplayModeContext CreateContext(GameplayModeHost host)
        {
            if (_embeddedGameplayPrepared && embeddedGameplayReferences != null)
            {
                return embeddedGameplayReferences.CreateContext(host);
            }

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

        public MonoBehaviour GetModeBehaviour(FlowEntryDefinition entry)
        {
            if (IsEmbeddedGameplayEntry(entry) && embeddedGameplayReferences != null)
            {
                return embeddedGameplayReferences.GetModeBehaviour(entry);
            }

            return defaultModeBehaviour;
        }

        private bool IsEmbeddedGameplayEntry(FlowEntryDefinition entry)
        {
            return embeddedGameplayReferences != null &&
                   entry != null &&
                   string.Equals(
                       entry.entryTag,
                       TranslationModeTag,
                       StringComparison.Ordinal);
        }

#if UNITY_EDITOR
        public bool TryStartEmbeddedGameplayForDirectPreview()
        {
            const string meetingEntryPath =
                "Assets/Data/FirstContact/FlowEntry_FirstContactTranslationAfterIntro.asset";
            FlowEntryDefinition directPreviewMeetingEntry =
                UnityEditor.AssetDatabase.LoadAssetAtPath<FlowEntryDefinition>(meetingEntryPath);
            if (embeddedGameplayReferences == null ||
                embeddedGameplayRoot == null ||
                directPreviewMeetingEntry == null ||
                !IsEmbeddedGameplayEntry(directPreviewMeetingEntry))
            {
                Debug.LogError(
                    "[FirstContactIntroSceneInstaller] Direct meeting preview references are incomplete.",
                    this);
                return false;
            }

            MonoBehaviour modeBehaviour = GetModeBehaviour(directPreviewMeetingEntry);
            if (modeBehaviour is not IGameplayMode ||
                modeBehaviour is not IGameplaySessionController session)
            {
                Debug.LogError(
                    "[FirstContactIntroSceneInstaller] The direct meeting preview mode is invalid.",
                    modeBehaviour != null ? modeBehaviour : this);
                return false;
            }

            GameplayModeHost host = GameplayModeHost.Instance;
            bool createdPreviewHost = host == null;
            if (createdPreviewHost)
            {
                var hostObject = new GameObject("DirectPreview_GameplayModeHost")
                {
                    hideFlags = HideFlags.DontSave
                };
                host = hostObject.AddComponent<GameplayModeHost>();
            }

            // Bind consumers before activating the dormant gameplay hierarchy so
            // their first OnEnable/Update observes the same authoritative host.
            embeddedGameplayReferences.ConfigureRuntime(host);
            PrepareEntry(directPreviewMeetingEntry);

            GameplayModeContext context = CreateContext(host);
            context.Services.Register(directPreviewMeetingEntry);
            if (!host.EnterMode(modeBehaviour, context))
            {
                PrepareEntry(null);
                if (createdPreviewHost)
                {
                    Destroy(host.gameObject);
                }

                return false;
            }

            if (directPreviewMeetingEntry.autoStartSession)
            {
                session.StartGame(directPreviewMeetingEntry.startSessionWithIntro);
            }

            Debug.Log(
                "[FirstContactIntroSceneInstaller] Entered embedded meeting mode for direct Facility preview.",
                this);
            return true;
        }
#endif

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

            FirstContactIntroSequenceController sequence =
                defaultModeBehaviour != null
                    ? defaultModeBehaviour.SequenceController
                    : FindComponentInScene<FirstContactIntroSequenceController>();
            FirstContactIntroGuideController guide = sequence != null
                ? sequence.Guide
                : null;
            string dialogueSpeaker = string.Empty;
            string dialogueText = string.Empty;
            bool hasDialogue = sequence != null &&
                               sequence.TryCaptureDialogueForSceneHandoff(
                                   out dialogueSpeaker,
                                   out dialogueText);
            var handoffState = new ElevatorHandoffState
            {
                Player = player,
                PlayerPositionInElevatorSpace =
                    elevatorSpace.InverseTransformPoint(player.transform.position),
                PlayerRotationInElevatorSpace =
                    Quaternion.Inverse(elevatorSpace.rotation) *
                    player.transform.rotation,
                Guide = guide,
                GuidePositionInElevatorSpace = guide != null
                    ? elevatorSpace.InverseTransformPoint(guide.transform.position)
                    : Vector3.zero,
                GuideRotationInElevatorSpace = guide != null
                    ? Quaternion.Inverse(elevatorSpace.rotation) *
                      guide.transform.rotation
                    : Quaternion.identity,
                HasDialogue = hasDialogue,
                DialogueSpeaker = hasDialogue ? dialogueSpeaker : string.Empty,
                DialogueText = hasDialogue ? dialogueText : string.Empty
            };

            // The player is already detached from the car by this point. Make it a
            // root once more so the old Surface roots can be suspended and unloaded
            // without disabling the rig that the player is still controlling.
            player.transform.SetParent(null, true);
            sequence?.ReleasePlayerForSceneHandoff(player);
            DontDestroyOnLoad(player.gameObject);
            if (guide != null)
            {
                guide.transform.SetParent(null, true);
                sequence?.ReleaseGuideForSceneHandoff(guide);
                DontDestroyOnLoad(guide.gameObject);
            }

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
            if (elevatorState.HasDialogue)
            {
                sequence?.RestoreDialogueFromSceneHandoff(
                    elevatorState.DialogueSpeaker,
                    elevatorState.DialogueText);
            }

            FirstContactIntroGuideController authoredFacilityGuide =
                sequence != null ? sequence.Guide : null;
            FirstContactIntroGuideController guide = elevatorState.Guide;
            if (guide != null)
            {
                guide.Stop();
                guide.transform.SetParent(null, true);
                guide.transform.SetPositionAndRotation(
                    elevatorSpace.TransformPoint(
                        elevatorState.GuidePositionInElevatorSpace),
                    elevatorSpace.rotation *
                    elevatorState.GuideRotationInElevatorSpace);
                if (guide.gameObject.scene != gameObject.scene)
                {
                    SceneManager.MoveGameObjectToScene(
                        guide.gameObject,
                        gameObject.scene);
                }

                if (authoredFacilityGuide != null &&
                    authoredFacilityGuide != guide)
                {
                    guide.CopyConfigurationFrom(authoredFacilityGuide);
                }

                sequence?.AdoptGuideFromSceneHandoff(guide);
                RebindBriefingDirectorLookTarget(guide);
                sequence?.RebindMeetingCastContinuity();
                if (authoredFacilityGuide != null &&
                    authoredFacilityGuide != guide)
                {
                    authoredFacilityGuide.gameObject.SetActive(false);
                }
            }

            // Keep the authored Facility rig for direct scene testing. During the
            // real Surface handoff the persistent rig owns the camera and listener.
            if (authoredFacilityPlayer != null &&
                authoredFacilityPlayer != player)
            {
                authoredFacilityPlayer.gameObject.SetActive(false);
            }
        }

        private void RebindBriefingDirectorLookTarget(
            FirstContactIntroGuideController guide)
        {
            FirstContactBriefingPresentation presentation =
                FindComponentInScene<FirstContactBriefingPresentation>();
            FirstContactBriefingLookTarget lookTarget = guide != null
                ? guide.GetComponentInChildren<FirstContactBriefingLookTarget>(true)
                : null;
            if (presentation == null || lookTarget == null)
            {
                return;
            }

            presentation.SetDirectorLookTarget(lookTarget.transform);
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
