using System;
using System.Collections.Generic;
using DoodleDiplomacy.Camera;
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

        [Header("Embedded Gameplay Camera Poses")]
        [Tooltip("The stationary gameplay cameras whose authored room pose changes between briefing practice and the meeting.")]
        [SerializeField] private Transform[] transferableGameplayCameras = Array.Empty<Transform>();
        [Tooltip("Authored briefing-room poses paired with Transferable Gameplay Cameras.")]
        [SerializeField] private Transform[] briefingGameplayCameraPoses = Array.Empty<Transform>();
        [Tooltip("Authored meeting-room poses paired with Transferable Gameplay Cameras.")]
        [SerializeField] private Transform[] meetingGameplayCameraPoses = Array.Empty<Transform>();

        private bool _embeddedGameplayPrepared;
        private bool _briefingPracticeActive;
        private bool _briefingPracticePresentationSuspended;
        private bool _briefingPracticeCameraWasEnabled;
        private AudioListener _briefingPracticeAudioListener;
        private bool _briefingPracticeAudioListenerWasEnabled;

        public string SceneId => string.IsNullOrWhiteSpace(sceneId) ? gameObject.scene.name : sceneId;
        public FirstContactTranslationMode EmbeddedTranslationMode =>
            embeddedGameplayReferences != null
                ? embeddedGameplayReferences.DefaultModeBehaviour as FirstContactTranslationMode
                : null;
        public IReadOnlyList<Transform> TransferableGameplayCameras => transferableGameplayCameras;
        public IReadOnlyList<Transform> BriefingGameplayCameraPoses => briefingGameplayCameraPoses;
        public IReadOnlyList<Transform> MeetingGameplayCameraPoses => meetingGameplayCameraPoses;

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
            RestoreBriefingPracticePresentation();
            _briefingPracticeActive = false;
            _embeddedGameplayPrepared = IsEmbeddedGameplayEntry(entry);
            if (_embeddedGameplayPrepared)
            {
                PlaceGameplayCamerasAt(meetingGameplayCameraPoses);
            }

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

        public bool TryBeginBriefingPractice(
            out GameplayModeContext context,
            out FirstContactTranslationMode translationMode)
        {
            context = null;
            translationMode = EmbeddedTranslationMode;
            if (embeddedGameplayReferences == null ||
                embeddedGameplayRoot == null ||
                translationMode == null)
            {
                Debug.LogError(
                    "[FirstContactIntroSceneInstaller] Embedded gameplay references are incomplete " +
                    $"for briefing practice. hub={embeddedGameplayReferences != null}, " +
                    $"root={embeddedGameplayRoot != null}, mode={translationMode != null}",
                    this);
                return false;
            }

            if (!PlaceGameplayCamerasAt(briefingGameplayCameraPoses))
            {
                return false;
            }

            GameplayModeHost host = GameplayModeHost.Instance;
            if (host == null && !TryCreateDirectPreviewHost(out host))
            {
                PlaceGameplayCamerasAt(meetingGameplayCameraPoses);
                return false;
            }

            // Bind input/UI consumers before activating the dormant hierarchy so
            // every OnEnable observes the intro mode that delegates practice input.
            embeddedGameplayReferences.ConfigureRuntime(host);
            _briefingPracticeActive = true;
            _briefingPracticePresentationSuspended = false;
            embeddedGameplayRoot.SetActive(true);
            context = embeddedGameplayReferences.CreateContext(host);
            if (context != null)
            {
                return true;
            }

            EndBriefingPractice();
            Debug.LogError(
                "[FirstContactIntroSceneInstaller] Failed to create the briefing practice context.",
                this);
            return false;
        }

        public void EndBriefingPractice()
        {
            if (!_briefingPracticeActive)
            {
                return;
            }

            RestoreBriefingPracticePresentation();
            _briefingPracticeActive = false;
            PlaceGameplayCamerasAt(meetingGameplayCameraPoses);
            if (embeddedGameplayRoot != null)
            {
                embeddedGameplayRoot.SetActive(_embeddedGameplayPrepared);
            }
        }

        public bool SuspendBriefingPracticePresentation()
        {
            if (!_briefingPracticeActive)
            {
                return false;
            }

            if (_briefingPracticePresentationSuspended)
            {
                return true;
            }

            UnityEngine.Camera gameplayCamera =
                embeddedGameplayReferences?.CameraController?.TargetCamera;
            if (gameplayCamera == null)
            {
                Debug.LogError(
                    "[FirstContactIntroSceneInstaller] The briefing practice camera is unavailable.",
                    this);
                return false;
            }

            _briefingPracticeCameraWasEnabled = gameplayCamera.enabled;
            gameplayCamera.enabled = false;
            _briefingPracticeAudioListener = gameplayCamera.GetComponent<AudioListener>();
            _briefingPracticeAudioListenerWasEnabled =
                _briefingPracticeAudioListener != null &&
                _briefingPracticeAudioListener.enabled;
            if (_briefingPracticeAudioListener != null)
            {
                _briefingPracticeAudioListener.enabled = false;
            }

            _briefingPracticePresentationSuspended = true;
            return true;
        }

        public bool ResumeBriefingPracticePresentation()
        {
            if (!_briefingPracticeActive)
            {
                return false;
            }

            RestoreBriefingPracticePresentation();
            return true;
        }

        private void RestoreBriefingPracticePresentation()
        {
            if (!_briefingPracticePresentationSuspended)
            {
                return;
            }

            UnityEngine.Camera gameplayCamera =
                embeddedGameplayReferences?.CameraController?.TargetCamera;
            if (gameplayCamera != null)
            {
                gameplayCamera.enabled = _briefingPracticeCameraWasEnabled;
            }

            if (_briefingPracticeAudioListener != null)
            {
                _briefingPracticeAudioListener.enabled =
                    _briefingPracticeAudioListenerWasEnabled;
            }

            _briefingPracticePresentationSuspended = false;
            _briefingPracticeCameraWasEnabled = false;
            _briefingPracticeAudioListener = null;
            _briefingPracticeAudioListenerWasEnabled = false;
        }

        private bool PlaceGameplayCamerasAt(Transform[] poses)
        {
            if (transferableGameplayCameras == null ||
                poses == null ||
                transferableGameplayCameras.Length == 0 ||
                transferableGameplayCameras.Length != poses.Length)
            {
                Debug.LogError(
                    "[FirstContactIntroSceneInstaller] Briefing and meeting camera pose references are incomplete.",
                    this);
                return false;
            }

            for (int i = 0; i < transferableGameplayCameras.Length; i++)
            {
                Transform gameplayCamera = transferableGameplayCameras[i];
                Transform pose = poses[i];
                if (gameplayCamera == null || pose == null)
                {
                    Debug.LogError(
                        $"[FirstContactIntroSceneInstaller] Camera pose pair {i} is incomplete.",
                        this);
                    return false;
                }

                gameplayCamera.SetPositionAndRotation(pose.position, pose.rotation);
            }

            CameraController cameraController = embeddedGameplayRoot != null
                ? embeddedGameplayRoot.GetComponentInChildren<CameraController>(true)
                : null;
            cameraController?.RefreshAuthoredCameraPoses();
            return true;
        }

        private bool TryCreateDirectPreviewHost(out GameplayModeHost host)
        {
            host = null;
            if (defaultModeBehaviour == null)
            {
                Debug.LogError(
                    "[FirstContactIntroSceneInstaller] Direct Facility preview requires the intro mode.",
                    this);
                return false;
            }

            var hostObject = new GameObject("DirectPreview_GameplayModeHost")
            {
                hideFlags = HideFlags.DontSave
            };
            host = hostObject.AddComponent<GameplayModeHost>();
            GameplayModeContext introContext = CreateContext(host);
            if (host.EnterMode(defaultModeBehaviour, introContext))
            {
                Debug.Log(
                    "[FirstContactIntroSceneInstaller] Created an intro-mode host for direct Facility preview.",
                    this);
                return true;
            }

            Destroy(hostObject);
            host = null;
            return false;
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
        public bool TryStartEmbeddedGameplayForDirectPreview(
            bool startWithIntro = true,
            bool suppressNarrativeCues = false)
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
            if (!host.EnterMode(
                    modeBehaviour,
                    context,
                    GameplayModeExitReason.Completed))
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
                if (suppressNarrativeCues && modeBehaviour is FirstContactTranslationMode translationMode)
                {
                    translationMode.StartGameplayTest();
                }
                else
                {
                    session.StartGame(
                        startWithIntro && directPreviewMeetingEntry.startSessionWithIntro);
                }
            }

            Debug.Log(
                suppressNarrativeCues
                    ? "[FirstContactIntroSceneInstaller] Entered dialogue-free gameplay test mode in the Facility."
                    : "[FirstContactIntroSceneInstaller] Entered embedded meeting mode for direct Facility preview.",
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
