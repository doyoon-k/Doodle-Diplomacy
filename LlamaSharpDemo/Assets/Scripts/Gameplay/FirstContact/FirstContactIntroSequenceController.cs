using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Narrative;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactIntroSequenceController : MonoBehaviour
    {
        [Serializable]
        private sealed class BriefingVisualCueEvent : UnityEvent<string>
        {
        }

        [SerializeField] private FirstContactIntroSegment segment;
        [SerializeField] private FirstContactIntroMode mode;
        [SerializeField] private FirstContactIntroPlayerController player;
        [SerializeField] private FirstContactIntroHud hud;
        [SerializeField] private FirstContactIntroGuideController guide;
        [SerializeField] private FirstContactIntroInteractable exitVehicleInteraction;
        [SerializeField] private FirstContactIntroInteractable elevatorInteraction;
        [SerializeField] private FirstContactIntroInteractable briefingSeatInteraction;
        [SerializeField] private FirstContactIntroInteractable meetingRoomInteraction;
        [SerializeField] private FirstContactNewsBroadcastPlayer newsBroadcast;
        [SerializeField] private NarrativeScenarioAsset narrativeScenario;
        [SerializeField] private FirstContactVehicleRouteController vehicleRoute;
        [SerializeField] private FirstContactSecretElevatorSequence secretElevatorSequence;
        [SerializeField] private Transform newsCameraAnchor;
        [SerializeField, Min(0f)] private float newsExitBlendSeconds = 0.8f;
        [Header("Car Conversation Timing")]
        [SerializeField, Min(0f)] private float postNewsDialogueDelaySeconds = 1f;
        [SerializeField, Min(0f)] private float turnDelayAfterArrivalLineSeconds = 2f;
        [Tooltip("The physical pizza sign to glance at from the vehicle. If empty, the prototype sign is found at runtime.")]
        [SerializeField] private Transform pizzaSignLookTarget;
        [SerializeField] private Transform pizzaSignCameraAnchor;
        [SerializeField, Min(0f)] private float pizzaSignFocusBlendSeconds = 0.45f;
        [SerializeField, Min(0f)] private float pizzaSignLeadSeconds = 0.25f;
        [SerializeField, Min(0f)] private float pizzaSignExitBlendSeconds = 0.55f;
        [SerializeField, Min(0f)] private float vehicleExitSeconds = 0.85f;
        [Header("Director Vehicle Exit")]
        [Tooltip("The Adjutant instance authored inside the car. If empty, Car/Adjutant is resolved at runtime.")]
        [SerializeField] private Transform vehicleDirectorActor;
        [SerializeField] private Transform directorVehicleExitAnchor;
        [SerializeField, Min(0f)] private float directorVehicleExitSeconds = 1.1f;
        [Header("Pizza Restaurant Sequence")]
        [Tooltip("Distance the player walks from the vehicle exit before the approach dialogue begins.")]
        [SerializeField, Min(0f)] private float pizzaApproachDialogueTravelDistance = 2.5f;
        [SerializeField, Min(0)] private int citizenEncounterGuidePointIndex = 2;
        [SerializeField, Min(0.5f)] private float citizenEncounterStartDistance = 3.25f;
        [SerializeField, Min(1f)] private float citizenPrivateExchangeDistance = 6f;
        [SerializeField, Min(0f)] private float citizenLookTurnSeconds = 0.45f;
        [SerializeField, Min(0f)] private float citizenDialogueExitPauseSeconds = 0.35f;
        [SerializeField] private Transform citizenSpeakerActor;
        [SerializeField] private Transform[] citizenLookActors = Array.Empty<Transform>();
        [Header("Storage Secret Entrance")]
        [SerializeField, Min(0)] private int secretRevealGuidePointIndex = 7;
        [SerializeField, Min(0.5f)] private float secretRevealStartDistance = 3.25f;
        [SerializeField, Min(0f)] private float minimumElevatorDescentSeconds = 2.5f;
        [Header("Facility Briefing")]
        [SerializeField] private Transform briefingWideCameraAnchor;
        [SerializeField] private Transform briefingProjectorCameraAnchor;
        [SerializeField, Min(0f)] private float facilityCorridorLeadSeconds = 0.65f;
        [SerializeField, Min(0f)] private float briefingSeatMoveSeconds = 0.45f;
        [SerializeField, Min(0f)] private float briefingCameraBlendSeconds = 0.55f;
        [SerializeField, Min(0f)] private float briefingExitBlendSeconds = 0.55f;
        [Tooltip("Raised for BriefingSlide* runtime cues. Add the projector image swap here when slide resources are ready.")]
        [SerializeField] private BriefingVisualCueEvent briefingVisualCue = new();

        private Coroutine _seatRoutine;
        private Coroutine _newsRoutine;
        private Coroutine _carArrivalRoutine;
        private Coroutine _vehicleExitRoutine;
        private Coroutine _directorVehicleExitRoutine;
        private Coroutine _pizzaApproachDialogueRoutine;
        private Coroutine _citizenEncounterRoutine;
        private Coroutine _pizzaPrivateExchangeRoutine;
        private Coroutine _secretDoorRevealRoutine;
        private Coroutine _elevatorRideRoutine;
        private Coroutine _facilityCorridorDialogueRoutine;
        private FirstContactNewsSubtitleDisplay _dialogueDisplay;
        private readonly List<NarrativeBeat> _activeDialogueBeats = new();
        private bool _begun;
        private bool _busy;
        private bool _newsClockSubscribed;
        private bool _surfaceDirectorPrepared;
        private bool _directorReadyToGuide;
        private FirstContactIntroGuideController _authoredSurfaceGuide;
        private bool _authoredSurfaceGuideActive;
        private Transform _directorOriginalParent;
        private int _directorOriginalSiblingIndex;
        private Vector3 _directorOriginalLocalPosition;
        private Quaternion _directorOriginalLocalRotation;
        private Transform _activeBriefingCameraAnchor;
        private bool _facilityGuideAtBriefing;
        private bool _facilityCorridorDialogueComplete;

        public bool IsBusy => _busy;
        public FirstContactIntroSegment Segment => segment;
        public Transform NewsCameraAnchor => newsCameraAnchor;

        public void SetNewsBroadcast(FirstContactNewsBroadcastPlayer broadcast)
        {
            newsBroadcast = broadcast;
        }

        public void SetNewsCameraAnchor(Transform cameraAnchor)
        {
            newsCameraAnchor = cameraAnchor;
        }

        public void Configure(
            FirstContactIntroSegment sceneSegment,
            FirstContactIntroMode introMode,
            FirstContactIntroPlayerController playerController,
            FirstContactIntroHud introHud,
            FirstContactIntroGuideController guideController,
            FirstContactIntroInteractable exitVehicle,
            FirstContactIntroInteractable elevator,
            FirstContactIntroInteractable briefingSeat,
            FirstContactIntroInteractable meetingRoom,
            FirstContactNewsBroadcastPlayer broadcast = null,
            Transform broadcastCameraAnchor = null)
        {
            segment = sceneSegment;
            mode = introMode;
            player = playerController;
            hud = introHud;
            guide = guideController;
            exitVehicleInteraction = exitVehicle;
            elevatorInteraction = elevator;
            briefingSeatInteraction = briefingSeat;
            meetingRoomInteraction = meetingRoom;
            newsBroadcast = broadcast;
            newsCameraAnchor = broadcastCameraAnchor;
        }

        private IEnumerator Start()
        {
            yield return null;
            if (!_begun && GameplayModeHost.Instance == null)
            {
                Begin();
            }
        }

#if UNITY_EDITOR
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void BeginDirectEditorPreview()
        {
            FirstContactIntroSequenceController sequence =
                FindFirstObjectByType<FirstContactIntroSequenceController>(
                    FindObjectsInactive.Exclude);
            if (sequence != null)
            {
                sequence.Begin();
            }
        }
#endif

        private void OnEnable()
        {
            SubscribeGuide();
        }

        private void OnDisable()
        {
            UnsubscribeNewsPlaybackClock();
            UnsubscribeGuide();
            vehicleRoute?.StopAndRestore();
            RestoreSurfaceDirectorActor();
            player?.RestoreView();
            hud?.SetCrosshairVisible(true);
            secretElevatorSequence?.ResetSequence();
        }

        public void Begin()
        {
            if (_begun)
            {
                return;
            }

            _begun = true;
            _busy = false;
            SubscribeGuide();
            player?.SetControlEnabled(true);
            secretElevatorSequence?.ResetSequence();

            if (segment == FirstContactIntroSegment.Surface)
            {
                EnsureVehicleRoute();
                PrepareSurfaceDirectorActor();
                exitVehicleInteraction?.SetAvailable(false);
                elevatorInteraction?.SetAvailable(false);
                player?.SetMovementEnabled(false);
                player?.SetInteractionEnabled(false);
                player?.SetLookEnabled(false);
                player?.SetContextualInteraction(null);
                hud?.ClearObjective();
                guide?.Stop();

                if (newsBroadcast != null && newsBroadcast.IsConfigured)
                {
                    vehicleRoute?.PlanCruiseDuration(CalculatePlannedCruiseSeconds());
                    vehicleRoute?.BeginCruise();
                    newsBroadcast.SetSubtitleHost(hud);
                    player?.LockViewTo(newsCameraAnchor);
                    hud?.SetCrosshairVisible(false);
                    _newsRoutine = StartCoroutine(NewsBroadcastRoutine());
                }
                else
                {
                    vehicleRoute?.StopAndRestore();
                    EnableVehicleExit();
                }
            }
            else
            {
                _facilityGuideAtBriefing = false;
                _facilityCorridorDialogueComplete = false;
                player?.SetContextualInteraction(null);
                briefingSeatInteraction?.SetAvailable(false);
                meetingRoomInteraction?.SetAvailable(false);
                hud?.SetObjective(
                    "first_contact.intro.objective.follow_to_briefing",
                    "Follow the director to the briefing room.");
                guide?.Begin(player != null ? player.transform : null);
                _facilityCorridorDialogueRoutine = StartCoroutine(
                    FacilityCorridorDialogueRoutine());
            }
        }

        public void Stop()
        {
            _begun = false;
            _busy = false;
            if (_seatRoutine != null)
            {
                StopCoroutine(_seatRoutine);
                _seatRoutine = null;
            }

            if (_newsRoutine != null)
            {
                StopCoroutine(_newsRoutine);
                _newsRoutine = null;
            }

            if (_carArrivalRoutine != null)
            {
                StopCoroutine(_carArrivalRoutine);
                _carArrivalRoutine = null;
            }

            if (_vehicleExitRoutine != null)
            {
                StopCoroutine(_vehicleExitRoutine);
                _vehicleExitRoutine = null;
            }

            if (_directorVehicleExitRoutine != null)
            {
                StopCoroutine(_directorVehicleExitRoutine);
                _directorVehicleExitRoutine = null;
            }

            if (_pizzaApproachDialogueRoutine != null)
            {
                StopCoroutine(_pizzaApproachDialogueRoutine);
                _pizzaApproachDialogueRoutine = null;
            }

            if (_citizenEncounterRoutine != null)
            {
                StopCoroutine(_citizenEncounterRoutine);
                _citizenEncounterRoutine = null;
            }

            if (_pizzaPrivateExchangeRoutine != null)
            {
                StopCoroutine(_pizzaPrivateExchangeRoutine);
                _pizzaPrivateExchangeRoutine = null;
            }

            if (_secretDoorRevealRoutine != null)
            {
                StopCoroutine(_secretDoorRevealRoutine);
                _secretDoorRevealRoutine = null;
            }

            if (_elevatorRideRoutine != null)
            {
                StopCoroutine(_elevatorRideRoutine);
                _elevatorRideRoutine = null;
            }

            if (_facilityCorridorDialogueRoutine != null)
            {
                StopCoroutine(_facilityCorridorDialogueRoutine);
                _facilityCorridorDialogueRoutine = null;
            }

            _facilityGuideAtBriefing = false;
            _facilityCorridorDialogueComplete = false;
            _activeBriefingCameraAnchor = null;

            UnsubscribeNewsPlaybackClock();
            newsBroadcast?.StopBroadcast();
            guide?.Stop();
            vehicleRoute?.StopAndRestore();
            RestoreSurfaceDirectorActor();
            _dialogueDisplay?.HideImmediate();
            player?.RestoreView();
            player?.SetContextualInteraction(null);
            player?.SetControlEnabled(false);
            hud?.ClearObjective();
            hud?.ClearPrompt();
            hud?.SetCrosshairVisible(true);
            secretElevatorSequence?.ResetSequence();
        }

        private IEnumerator NewsBroadcastRoutine()
        {
            SubscribeNewsPlaybackClock();
            vehicleRoute?.SetCruisePaused(true);
            yield return newsBroadcast.PlayBroadcastRoutine();
            UnsubscribeNewsPlaybackClock();
            vehicleRoute?.SetCruisePaused(false);
            _newsRoutine = null;
            if (player != null)
            {
                yield return player.BlendToRestoredView(newsExitBlendSeconds);
            }

            if (_begun && !_busy)
            {
                _carArrivalRoutine = StartCoroutine(CarArrivalRoutine());
            }
        }

        private IEnumerator CarArrivalRoutine()
        {
            player?.SetMovementEnabled(false);
            // The player stays seated, but can freely look around while the president and director talk.
            player?.SetLookEnabled(true);
            player?.SetInteractionEnabled(false);
            hud?.ClearObjective();

            if (postNewsDialogueDelaySeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(postNewsDialogueDelaySeconds);
            }

            yield return PlayDialogueEventRoutine("intro.car.after_news");

            if (vehicleRoute != null)
            {
                if (!vehicleRoute.IsArrivalStarted)
                {
                    vehicleRoute.BeginArrival();
                }

                yield return vehicleRoute.WaitForSignRevealRoutine();
                vehicleRoute.BeginBraking();
                yield return vehicleRoute.WaitUntilStoppedRoutine();

                if (vehicleRoute.SignLookTarget != null)
                {
                    pizzaSignLookTarget = vehicleRoute.SignLookTarget;
                }
            }

            ResolvePizzaSignLookTarget();
            if (pizzaSignLookTarget != null)
            {
                // Keep the player in the car. This is a glance toward the sign, not a camera teleport.
                player?.SetLookEnabled(false);
                if (player != null)
                {
                    yield return player.BlendViewToLookAt(
                        pizzaSignLookTarget,
                        pizzaSignFocusBlendSeconds);
                }

                if (pizzaSignLeadSeconds > 0f)
                {
                    yield return new WaitForSecondsRealtime(pizzaSignLeadSeconds);
                }
            }

            yield return PlayDialogueEventRoutine("intro.car.pizza_sign");

            if (pizzaSignLookTarget != null && player != null)
            {
                yield return player.BlendToRestoredGazeView(pizzaSignExitBlendSeconds);
            }

            _dialogueDisplay?.Hide();
            _carArrivalRoutine = null;
            if (_begun && !_busy)
            {
                EnableVehicleExit();
            }
        }

        private IEnumerator PlayDialogueEventRoutine(
            string triggerEvent,
            bool playWhileSequenceBusy = false)
        {
            NarrativeScenarioAsset scenario = GetNarrativeScenario();
            if (scenario == null || string.IsNullOrWhiteSpace(triggerEvent))
            {
                yield break;
            }

            _activeDialogueBeats.Clear();
            IReadOnlyList<NarrativeBeat> beats = scenario.Beats;
            for (int i = 0; i < beats.Count; i++)
            {
                NarrativeBeat beat = beats[i];
                if (beat != null &&
                    beat.enabled &&
                    string.Equals(
                        beat.triggerEvent,
                        triggerEvent,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _activeDialogueBeats.Add(beat);
                }
            }

            if (_activeDialogueBeats.Count == 0)
            {
                yield break;
            }

            _activeDialogueBeats.Sort((left, right) => left.order.CompareTo(right.order));
            FirstContactNewsSubtitleDisplay display = GetDialogueDisplay();
            for (int i = 0; i < _activeDialogueBeats.Count; i++)
            {
                NarrativeBeat beat = _activeDialogueBeats[i];
                NarrativeTrace.Emit(scenario.ScenarioId, beat.id, "enter");
                yield return PrepareDialogueBeatRoutine(beat.runtimeCue);

                bool silentBeat = IsSilentDialogueBeat(beat);
                if (silentBeat)
                {
                    display?.Hide();
                }
                else
                {
                    display?.ShowDialogue(beat.ResolveSpeaker(), beat.ResolveText());
                }

                float elapsed = 0f;
                float duration = Mathf.Max(0.1f, beat.minimumSeconds);
                while (_begun &&
                       (playWhileSequenceBusy || !_busy) &&
                       elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                if (beat.WaitForAdvance && !silentBeat &&
                    CanContinueDialogue(playWhileSequenceBusy))
                {
                    display?.SetAdvancePromptVisible(true);
                    while (CanContinueDialogue(playWhileSequenceBusy) &&
                           !WasDialogueAdvancePressed())
                    {
                        yield return null;
                    }
                }

                display?.SetAdvancePromptVisible(false);
                display?.Hide();
                NarrativeTrace.Emit(scenario.ScenarioId, beat.id, "exit");
                HandleDialogueRuntimeCue(beat.runtimeCue);

                if (!CanContinueDialogue(playWhileSequenceBusy))
                {
                    yield break;
                }
            }
        }

        private bool CanContinueDialogue(bool playWhileSequenceBusy)
        {
            return _begun && (playWhileSequenceBusy || !_busy);
        }

        private static bool WasDialogueAdvancePressed()
        {
            return Keyboard.current != null &&
                   Keyboard.current.spaceKey.wasPressedThisFrame;
        }

        private IEnumerator PrepareDialogueBeatRoutine(string runtimeCue)
        {
            if (string.IsNullOrWhiteSpace(runtimeCue))
            {
                yield break;
            }

            bool isBriefingSlide = runtimeCue.StartsWith(
                "BriefingSlide",
                StringComparison.OrdinalIgnoreCase);
            Transform briefingAnchor = null;
            if (string.Equals(
                    runtimeCue,
                    "BriefingWide",
                    StringComparison.OrdinalIgnoreCase))
            {
                briefingAnchor = briefingWideCameraAnchor;
            }
            else if (runtimeCue.StartsWith(
                         "BriefingProjector",
                         StringComparison.OrdinalIgnoreCase) ||
                     isBriefingSlide)
            {
                briefingAnchor = briefingProjectorCameraAnchor;
            }

            if (isBriefingSlide)
            {
                briefingVisualCue?.Invoke(runtimeCue);
            }

            if (briefingAnchor != null &&
                briefingAnchor != _activeBriefingCameraAnchor &&
                player != null)
            {
                yield return player.BlendViewToAnchor(
                    briefingAnchor,
                    briefingCameraBlendSeconds);
                _activeBriefingCameraAnchor = briefingAnchor;
                yield break;
            }

            if (string.Equals(
                    runtimeCue,
                    "PizzaAwkwardSilence",
                    StringComparison.OrdinalIgnoreCase))
            {
                ResolveCitizenLookActors();
                if (guide != null)
                {
                    yield return TurnActorsTowardRoutine(
                        citizenLookActors,
                        guide.transform,
                        citizenLookTurnSeconds);
                }

                yield break;
            }

            if (string.Equals(
                    runtimeCue,
                    "PizzaPresidentCover",
                    StringComparison.OrdinalIgnoreCase))
            {
                ResolveCitizenLookActors();
                if (player != null)
                {
                    yield return TurnActorsTowardRoutine(
                        citizenLookActors,
                        player.transform,
                        citizenLookTurnSeconds);
                }
            }
        }

        private static bool IsSilentDialogueBeat(NarrativeBeat beat)
        {
            return beat != null &&
                   string.Equals(
                       beat.runtimeCue,
                       "PizzaAwkwardSilence",
                       StringComparison.OrdinalIgnoreCase);
        }

        private FirstContactNewsSubtitleDisplay GetDialogueDisplay()
        {
            if (_dialogueDisplay != null || hud == null)
            {
                return _dialogueDisplay;
            }

            _dialogueDisplay = hud.GetComponent<FirstContactNewsSubtitleDisplay>();
            if (_dialogueDisplay == null)
            {
                _dialogueDisplay = hud.gameObject.AddComponent<FirstContactNewsSubtitleDisplay>();
            }

            return _dialogueDisplay;
        }

        private NarrativeScenarioAsset GetNarrativeScenario()
        {
            return narrativeScenario != null
                ? narrativeScenario
                : newsBroadcast != null
                    ? newsBroadcast.NarrativeScenario
                    : null;
        }

        private void HandleDialogueRuntimeCue(string runtimeCue)
        {
            if (vehicleRoute == null || string.IsNullOrWhiteSpace(runtimeCue))
            {
                return;
            }

            if (string.Equals(
                    runtimeCue,
                    "CarAfterNewsDirectorArrival",
                    StringComparison.OrdinalIgnoreCase))
            {
                vehicleRoute.BeginArrival();
            }
        }

        private float CalculatePlannedCruiseSeconds()
        {
            float plannedSeconds = newsExitBlendSeconds +
                                   postNewsDialogueDelaySeconds +
                                   turnDelayAfterArrivalLineSeconds;
            if (newsBroadcast != null)
            {
                plannedSeconds += newsBroadcast.EstimatedPlaybackSeconds;
            }

            NarrativeScenarioAsset scenario = newsBroadcast != null
                ? newsBroadcast.NarrativeScenario
                : null;
            if (scenario == null)
            {
                return plannedSeconds;
            }

            var arrivalBeats = new List<NarrativeBeat>();
            IReadOnlyList<NarrativeBeat> beats = scenario.Beats;
            for (int i = 0; i < beats.Count; i++)
            {
                NarrativeBeat beat = beats[i];
                if (beat != null &&
                    beat.enabled &&
                    string.Equals(
                        beat.triggerEvent,
                        "intro.car.after_news",
                        StringComparison.OrdinalIgnoreCase))
                {
                    arrivalBeats.Add(beat);
                }
            }

            arrivalBeats.Sort((left, right) => left.order.CompareTo(right.order));
            for (int i = 0; i < arrivalBeats.Count; i++)
            {
                NarrativeBeat beat = arrivalBeats[i];
                plannedSeconds += Mathf.Max(0.1f, beat.minimumSeconds);
                if (string.Equals(
                        beat.runtimeCue,
                        "CarAfterNewsDirectorArrival",
                        StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            return plannedSeconds;
        }

        private void SubscribeNewsPlaybackClock()
        {
            if (_newsClockSubscribed || newsBroadcast == null)
            {
                return;
            }

            newsBroadcast.PlaybackClockChanged += HandleNewsPlaybackClockChanged;
            _newsClockSubscribed = true;
        }

        private void UnsubscribeNewsPlaybackClock()
        {
            if (!_newsClockSubscribed || newsBroadcast == null)
            {
                _newsClockSubscribed = false;
                return;
            }

            newsBroadcast.PlaybackClockChanged -= HandleNewsPlaybackClockChanged;
            _newsClockSubscribed = false;
        }

        private void HandleNewsPlaybackClockChanged(bool running)
        {
            vehicleRoute?.SetCruisePaused(!running);
        }

        private void EnsureVehicleRoute()
        {
            if (vehicleRoute == null)
            {
                vehicleRoute = GetComponent<FirstContactVehicleRouteController>();
            }

            if (vehicleRoute == null)
            {
                Debug.LogError(
                    "[FirstContactIntro] FC_Intro_Surface is missing its authored vehicle route controller. " +
                    "Runtime route construction is disabled.",
                    this);
                return;
            }

            vehicleRoute.Configure(player != null ? player.transform : null);
        }

        private void ResolvePizzaSignLookTarget()
        {
            if (pizzaSignLookTarget != null)
            {
                return;
            }

            // The existing SHOT_Pizza_Sign is a camera position, not the sign itself.
            // Prefer the physical sign so the camera can stay in the car and merely turn toward it.
            GameObject sign = GameObject.Find("SignBacking");
            if (sign != null)
            {
                pizzaSignLookTarget = sign.transform;
                return;
            }

            // Retain a safe fallback for scenes that do not yet include the prototype sign mesh.
            if (pizzaSignCameraAnchor == null)
            {
                GameObject shot = GameObject.Find("SHOT_Pizza_Sign");
                pizzaSignCameraAnchor = shot != null ? shot.transform : null;
            }

            pizzaSignLookTarget = pizzaSignCameraAnchor;
        }

        private void EnableVehicleExit()
        {
            BeginSurfaceDirectorExit();
            player?.RestoreView();
            hud?.SetCrosshairVisible(true);
            exitVehicleInteraction?.SetAvailable(true);
            player?.SetLookEnabled(true);
            player?.SetInteractionEnabled(true);
            player?.SetMovementEnabled(false);
            player?.SetContextualInteraction(exitVehicleInteraction);
            hud?.SetObjective(
                "first_contact.intro.objective.exit_vehicle",
                "Exit the vehicle.");
        }

        public bool HandleInteraction(
            FirstContactIntroInteractable interactable,
            FirstContactIntroPlayerController sourcePlayer)
        {
            if (!_begun || _busy || interactable == null || sourcePlayer == null)
            {
                return false;
            }

            switch (interactable.Action)
            {
                case FirstContactIntroInteractionAction.ExitVehicle:
                    interactable.SetAvailable(false);
                    sourcePlayer.SetContextualInteraction(null);
                    _busy = true;
                    _vehicleExitRoutine = StartCoroutine(
                        VehicleExitRoutine(interactable, sourcePlayer));
                    return true;

                case FirstContactIntroInteractionAction.UseElevator:
                    if (segment == FirstContactIntroSegment.Surface &&
                        secretElevatorSequence != null)
                    {
                        if (!secretElevatorSequence.IsRevealed ||
                            !secretElevatorSequence.IsInsideElevator(
                                sourcePlayer.transform))
                        {
                            hud?.SetObjective(
                                "first_contact.intro.objective.board_elevator",
                                "Board the elevator and use the control.");
                            return false;
                        }

                        interactable.SetAvailable(false);
                        _busy = true;
                        sourcePlayer.SetContextualInteraction(null);
                        _elevatorRideRoutine = StartCoroutine(
                            ElevatorRideRoutine(sourcePlayer));
                        return true;
                    }

                    goto case FirstContactIntroInteractionAction.EnterMeetingRoom;

                case FirstContactIntroInteractionAction.EnterMeetingRoom:
                    _busy = true;
                    sourcePlayer.SetContextualInteraction(null);
                    sourcePlayer.SetControlEnabled(false);
                    hud?.ClearObjective();
                    mode?.CompleteSegment();
                    return true;

                case FirstContactIntroInteractionAction.TakeBriefingSeat:
                    _seatRoutine = StartCoroutine(SeatRoutine(interactable, sourcePlayer));
                    return true;

                default:
                    return false;
            }
        }

        private IEnumerator VehicleExitRoutine(
            FirstContactIntroInteractable interactable,
            FirstContactIntroPlayerController sourcePlayer)
        {
            sourcePlayer.SetMovementEnabled(false);
            sourcePlayer.SetLookEnabled(false);
            sourcePlayer.SetInteractionEnabled(false);

            vehicleRoute?.DetachPlayer(sourcePlayer.transform);
            Transform exitTarget = interactable.Target;
            if (exitTarget != null)
            {
                yield return sourcePlayer.MoveToWorldPose(
                    exitTarget,
                    vehicleExitSeconds);
            }

            while (_directorVehicleExitRoutine != null)
            {
                yield return null;
            }

            sourcePlayer.SetLookEnabled(true);
            sourcePlayer.SetMovementEnabled(true);
            sourcePlayer.SetInteractionEnabled(true);
            guide?.AddManualHoldPoint(citizenEncounterGuidePointIndex);
            guide?.AddManualHoldPoint(secretRevealGuidePointIndex);
            guide?.Begin(
                sourcePlayer.transform,
                warpToStart: !_directorReadyToGuide);
            if (_pizzaApproachDialogueRoutine == null)
            {
                _pizzaApproachDialogueRoutine = StartCoroutine(
                    PizzaApproachDialogueRoutine(sourcePlayer.transform));
            }
            hud?.SetObjective(
                "first_contact.intro.objective.follow_director",
                "Follow the director.");
            _busy = false;
            _vehicleExitRoutine = null;
        }

        private IEnumerator PizzaApproachDialogueRoutine(Transform sourcePlayer)
        {
            if (sourcePlayer == null)
            {
                _pizzaApproachDialogueRoutine = null;
                yield break;
            }

            Vector3 startPosition = sourcePlayer.position;
            float requiredDistance = Mathf.Max(0f, pizzaApproachDialogueTravelDistance);
            while (_begun &&
                   guide != null &&
                   guide.CurrentPointIndex < citizenEncounterGuidePointIndex &&
                   HorizontalDistance(startPosition, sourcePlayer.position) < requiredDistance)
            {
                yield return null;
            }

            bool reachedCitizenEncounter = guide == null ||
                                           guide.CurrentPointIndex >= citizenEncounterGuidePointIndex;
            if (_begun && !reachedCitizenEncounter)
            {
                yield return PlayDialogueEventRoutine("intro.pizza.approach");
                _dialogueDisplay?.Hide();
            }

            _pizzaApproachDialogueRoutine = null;
        }

        private void PrepareSurfaceDirectorActor()
        {
            if (_surfaceDirectorPrepared ||
                segment != FirstContactIntroSegment.Surface)
            {
                return;
            }

            if (vehicleDirectorActor == null)
            {
                GameObject car = GameObject.Find("Car");
                vehicleDirectorActor = car != null
                    ? car.transform.Find("Adjutant")
                    : null;
            }

            if (vehicleDirectorActor == null)
            {
                _directorReadyToGuide = false;
                return;
            }

            _surfaceDirectorPrepared = true;
            _directorReadyToGuide = false;
            _directorOriginalParent = vehicleDirectorActor.parent;
            _directorOriginalSiblingIndex = vehicleDirectorActor.GetSiblingIndex();
            _directorOriginalLocalPosition = vehicleDirectorActor.localPosition;
            _directorOriginalLocalRotation = vehicleDirectorActor.localRotation;

            FirstContactIntroGuideController actorGuide =
                vehicleDirectorActor.GetComponent<FirstContactIntroGuideController>();
            if (actorGuide == null)
            {
                Debug.LogError(
                    "[FirstContactIntro] The scene-authored director is missing its guide controller. " +
                    "Runtime guide construction is disabled.",
                    this);
                _surfaceDirectorPrepared = false;
                return;
            }

            if (guide != null && guide != actorGuide)
            {
                _authoredSurfaceGuide = guide;
                _authoredSurfaceGuideActive = guide.gameObject.activeSelf;
                UnsubscribeGuide();
                _authoredSurfaceGuide.Stop();
                actorGuide.CopyConfigurationFrom(_authoredSurfaceGuide);
                _authoredSurfaceGuide.gameObject.SetActive(false);
            }

            actorGuide.Stop();
            guide = actorGuide;
            SubscribeGuide();
        }

        private void BeginSurfaceDirectorExit()
        {
            if (segment != FirstContactIntroSegment.Surface ||
                _directorVehicleExitRoutine != null ||
                _directorReadyToGuide)
            {
                return;
            }

            if (!_surfaceDirectorPrepared || vehicleDirectorActor == null)
            {
                _directorReadyToGuide = false;
                return;
            }

            _directorVehicleExitRoutine = StartCoroutine(
                SurfaceDirectorExitRoutine());
        }

        private IEnumerator SurfaceDirectorExitRoutine()
        {
            Transform actor = vehicleDirectorActor;
            if (actor == null || directorVehicleExitAnchor == null)
            {
                Debug.LogError(
                    "[FirstContactIntro] The scene-authored director or vehicle exit anchor is missing.",
                    this);
                _directorVehicleExitRoutine = null;
                yield break;
            }

            Vector3 startPosition = actor.position;
            Quaternion startRotation = actor.rotation;
            Vector3 exitPosition = directorVehicleExitAnchor.position;
            Quaternion exitRotation = directorVehicleExitAnchor.rotation;

            actor.SetParent(null, true);
            guide?.ApplyVisualForwardCorrection();
            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, directorVehicleExitSeconds);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                actor.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, exitPosition, progress),
                    Quaternion.Slerp(startRotation, exitRotation, progress));
                yield return null;
            }

            actor.SetPositionAndRotation(exitPosition, exitRotation);
            _directorReadyToGuide = true;
            _directorVehicleExitRoutine = null;
        }

        private void RestoreSurfaceDirectorActor()
        {
            if (!_surfaceDirectorPrepared)
            {
                return;
            }

            FirstContactIntroGuideController runtimeGuide = guide;
            UnsubscribeGuide();
            runtimeGuide?.Stop();

            if (vehicleDirectorActor != null)
            {
                vehicleDirectorActor.SetParent(_directorOriginalParent, false);
                vehicleDirectorActor.localPosition = _directorOriginalLocalPosition;
                vehicleDirectorActor.localRotation = _directorOriginalLocalRotation;
                vehicleDirectorActor.SetSiblingIndex(_directorOriginalSiblingIndex);
            }

            if (_authoredSurfaceGuide != null)
            {
                guide = _authoredSurfaceGuide;
                _authoredSurfaceGuide.gameObject.SetActive(
                    _authoredSurfaceGuideActive);
            }
            else
            {
                guide = runtimeGuide;
            }

            _surfaceDirectorPrepared = false;
            _directorReadyToGuide = false;
            _authoredSurfaceGuide = null;
            _directorOriginalParent = null;
        }

        private IEnumerator SeatRoutine(
            FirstContactIntroInteractable interactable,
            FirstContactIntroPlayerController sourcePlayer)
        {
            _busy = true;
            interactable.SetAvailable(false);
            Collider seatCollider = interactable.GetComponent<Collider>();
            if (seatCollider != null)
            {
                seatCollider.enabled = false;
            }

            sourcePlayer.SetMovementEnabled(false);
            sourcePlayer.SetLookEnabled(false);
            sourcePlayer.SetInteractionEnabled(false);
            sourcePlayer.SetContextualInteraction(null);
            hud?.SetCrosshairVisible(false);
            hud?.ClearPrompt();
            hud?.ClearObjective();

            if (interactable.Target != null)
            {
                yield return sourcePlayer.MoveToWorldPose(
                    interactable.Target,
                    briefingSeatMoveSeconds);
            }

            sourcePlayer.Teleport(interactable.Target, seated: true);
            if (briefingWideCameraAnchor != null)
            {
                yield return sourcePlayer.BlendViewToAnchor(
                    briefingWideCameraAnchor,
                    briefingCameraBlendSeconds);
                _activeBriefingCameraAnchor = briefingWideCameraAnchor;
            }

            yield return PlayDialogueEventRoutine(
                "intro.facility.briefing",
                playWhileSequenceBusy: true);

            _dialogueDisplay?.Hide();
            _activeBriefingCameraAnchor = null;
            yield return sourcePlayer.BlendToRestoredView(
                briefingExitBlendSeconds);

            sourcePlayer.Teleport(interactable.SecondaryTarget, seated: false);
            hud?.SetCrosshairVisible(true);
            sourcePlayer.SetLookEnabled(true);
            sourcePlayer.SetMovementEnabled(true);
            sourcePlayer.SetInteractionEnabled(true);
            guide?.Resume();
            hud?.SetObjective(
                "first_contact.intro.objective.follow_to_meeting",
                "Follow the director to the meeting room.");
            _busy = false;
            _seatRoutine = null;
        }

        private IEnumerator FacilityCorridorDialogueRoutine()
        {
            if (facilityCorridorLeadSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(
                    facilityCorridorLeadSeconds);
            }

            yield return PlayDialogueEventRoutine(
                "intro.facility.corridor");
            _facilityCorridorDialogueComplete = true;
            _facilityCorridorDialogueRoutine = null;
            TryEnableBriefingSeat();
        }

        private void TryEnableBriefingSeat()
        {
            if (!_begun || segment != FirstContactIntroSegment.Facility ||
                !_facilityGuideAtBriefing ||
                !_facilityCorridorDialogueComplete)
            {
                return;
            }

            briefingSeatInteraction?.SetAvailable(true);
            hud?.SetObjective(
                "first_contact.intro.objective.take_briefing_seat",
                "Take your seat.");
        }

        private void HandleGuideManualHold(int pointIndex)
        {
            if (segment == FirstContactIntroSegment.Surface)
            {
                if (pointIndex == citizenEncounterGuidePointIndex &&
                    _citizenEncounterRoutine == null)
                {
                    _citizenEncounterRoutine = StartCoroutine(
                        CitizenEncounterRoutine());
                }

                if (pointIndex == secretRevealGuidePointIndex &&
                    _secretDoorRevealRoutine == null)
                {
                    _secretDoorRevealRoutine = StartCoroutine(
                        SecretDoorRevealRoutine());
                }

                return;
            }

            if (segment != FirstContactIntroSegment.Facility)
            {
                return;
            }

            _facilityGuideAtBriefing = true;
            TryEnableBriefingSeat();
        }

        private IEnumerator CitizenEncounterRoutine()
        {
            while (_begun && player != null && guide != null &&
                   HorizontalDistance(player.transform.position, guide.transform.position) >
                   citizenEncounterStartDistance)
            {
                yield return null;
            }

            if (!_begun)
            {
                _citizenEncounterRoutine = null;
                yield break;
            }

            _busy = true;
            player?.SetMovementEnabled(false);
            player?.SetInteractionEnabled(false);
            player?.SetLookEnabled(true);
            hud?.ClearObjective();

            ResolveCitizenLookActors();
            if (player != null)
            {
                yield return TurnActorsTowardRoutine(
                    citizenLookActors,
                    player.transform,
                    citizenLookTurnSeconds);
                if (guide != null && citizenSpeakerActor != null)
                {
                    yield return TurnActorsTowardRoutine(
                        new[] { guide.transform },
                        citizenSpeakerActor,
                        citizenLookTurnSeconds * 0.65f);
                }
            }

            yield return PlayDialogueEventRoutine(
                "intro.pizza.citizen_encounter",
                playWhileSequenceBusy: true);

            if (citizenDialogueExitPauseSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(
                    citizenDialogueExitPauseSeconds);
            }

            player?.SetMovementEnabled(true);
            player?.SetInteractionEnabled(true);
            guide?.Resume();
            hud?.SetObjective(
                "first_contact.intro.objective.follow_director",
                "Follow the director.");
            _busy = false;
            _citizenEncounterRoutine = null;
            if (_begun && _pizzaPrivateExchangeRoutine == null)
            {
                _pizzaPrivateExchangeRoutine = StartCoroutine(
                    PizzaPrivateExchangeRoutine());
            }
        }

        private void ResolveCitizenLookActors()
        {
            if (citizenLookActors != null && citizenLookActors.Length > 0)
            {
                if (citizenSpeakerActor == null)
                {
                    citizenSpeakerActor = FindClosestCitizenActor(
                        guide != null
                            ? guide.transform.position
                            : transform.position);
                }

                return;
            }

            var actors = new List<Transform>();
            for (int index = 1; index <= 4; index++)
            {
                GameObject citizen = GameObject.Find(
                    $"Citizen_{index:00}_Placeholder");
                if (citizen != null)
                {
                    actors.Add(citizen.transform);
                }
            }

            citizenLookActors = actors.ToArray();
            if (citizenSpeakerActor == null && citizenLookActors.Length > 0)
            {
                citizenSpeakerActor = FindClosestCitizenActor(
                    guide != null
                        ? guide.transform.position
                        : transform.position);
            }
        }

        private Transform FindClosestCitizenActor(Vector3 position)
        {
            Transform closestCitizen = null;
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < citizenLookActors.Length; i++)
            {
                Transform citizen = citizenLookActors[i];
                if (citizen == null)
                {
                    continue;
                }

                float distance = HorizontalDistance(position, citizen.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestCitizen = citizen;
                }
            }

            return closestCitizen;
        }

        private IEnumerator PizzaPrivateExchangeRoutine()
        {
            while (_begun && !HasPartyLeftCitizenEarshot())
            {
                yield return null;
            }

            if (_begun)
            {
                yield return PlayDialogueEventRoutine(
                    "intro.pizza.private_exchange");
                _dialogueDisplay?.Hide();
            }

            _pizzaPrivateExchangeRoutine = null;
        }

        private IEnumerator SecretDoorRevealRoutine()
        {
            while (_begun && player != null && guide != null &&
                   HorizontalDistance(
                       player.transform.position,
                       guide.transform.position) > secretRevealStartDistance)
            {
                yield return null;
            }

            while (_begun && _pizzaPrivateExchangeRoutine != null)
            {
                yield return null;
            }

            if (!_begun)
            {
                _secretDoorRevealRoutine = null;
                yield break;
            }

            _busy = true;
            player?.SetMovementEnabled(false);
            player?.SetInteractionEnabled(false);
            player?.SetLookEnabled(true);
            hud?.ClearObjective();

            yield return PlayDialogueEventRoutine(
                "intro.pizza.storage.approach",
                playWhileSequenceBusy: true);

            if (secretElevatorSequence != null)
            {
                yield return secretElevatorSequence.RevealRoutine(
                    guide != null ? guide.transform : null);
            }

            yield return PlayDialogueEventRoutine(
                "intro.pizza.storage.reveal",
                playWhileSequenceBusy: true);

            player?.SetMovementEnabled(true);
            player?.SetInteractionEnabled(true);
            guide?.Resume();
            hud?.SetObjective(
                "first_contact.intro.objective.follow_director",
                "Follow the director.");
            _busy = false;
            _secretDoorRevealRoutine = null;
        }

        private IEnumerator ElevatorRideRoutine(
            FirstContactIntroPlayerController sourcePlayer)
        {
            sourcePlayer.SetMovementEnabled(false);
            sourcePlayer.SetInteractionEnabled(false);
            sourcePlayer.SetLookEnabled(true);
            hud?.ClearPrompt();
            hud?.ClearObjective();

            if (secretElevatorSequence != null)
            {
                yield return secretElevatorSequence.CloseDoorRoutine();
                secretElevatorSequence.BeginDescent();
            }

            float descentStartedAt = Time.realtimeSinceStartup;
            yield return PlayDialogueEventRoutine(
                "intro.elevator.descent",
                playWhileSequenceBusy: true);

            float elapsed = Time.realtimeSinceStartup - descentStartedAt;
            float remaining = Mathf.Max(0f, minimumElevatorDescentSeconds - elapsed);
            if (remaining > 0f)
            {
                yield return new WaitForSecondsRealtime(remaining);
            }

            secretElevatorSequence?.StopDescent();
            sourcePlayer.SetLookEnabled(false);
            _elevatorRideRoutine = null;
            mode?.CompleteSegment();
        }

        private bool HasPartyLeftCitizenEarshot()
        {
            if (citizenLookActors == null || citizenLookActors.Length == 0)
            {
                return true;
            }

            if (player == null || guide == null)
            {
                return false;
            }

            float requiredDistance = Mathf.Max(
                1f,
                citizenPrivateExchangeDistance);
            return DistanceFromClosestCitizen(player.transform.position) >=
                       requiredDistance &&
                   DistanceFromClosestCitizen(guide.transform.position) >=
                       requiredDistance;
        }

        private float DistanceFromClosestCitizen(Vector3 position)
        {
            float closestDistance = float.PositiveInfinity;
            for (int i = 0; i < citizenLookActors.Length; i++)
            {
                Transform citizen = citizenLookActors[i];
                if (citizen != null)
                {
                    closestDistance = Mathf.Min(
                        closestDistance,
                        HorizontalDistance(position, citizen.position));
                }
            }

            return closestDistance;
        }

        private static IEnumerator TurnActorsTowardRoutine(
            IReadOnlyList<Transform> actors,
            Transform target,
            float seconds)
        {
            if (actors == null || target == null)
            {
                yield break;
            }

            var starts = new Quaternion[actors.Count];
            var targets = new Quaternion[actors.Count];
            for (int i = 0; i < actors.Count; i++)
            {
                Transform actor = actors[i];
                if (actor == null)
                {
                    continue;
                }

                starts[i] = actor.rotation;
                Vector3 direction = target.position - actor.position;
                direction.y = 0f;
                targets[i] = direction.sqrMagnitude > 0.001f
                    ? Quaternion.LookRotation(direction.normalized, Vector3.up)
                    : actor.rotation;
            }

            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, seconds);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                for (int i = 0; i < actors.Count; i++)
                {
                    if (actors[i] != null)
                    {
                        actors[i].rotation = Quaternion.Slerp(
                            starts[i],
                            targets[i],
                            progress);
                    }
                }

                yield return null;
            }
        }

        private static float HorizontalDistance(Vector3 left, Vector3 right)
        {
            left.y = 0f;
            right.y = 0f;
            return Vector3.Distance(left, right);
        }

        private void HandleGuideDestination()
        {
            if (segment == FirstContactIntroSegment.Surface)
            {
                elevatorInteraction?.SetAvailable(true);
                hud?.SetObjective(
                    "first_contact.intro.objective.board_elevator",
                    "Board the elevator and use the control.");
            }
            else
            {
                meetingRoomInteraction?.SetAvailable(true);
                player?.SetContextualInteraction(meetingRoomInteraction);
                hud?.SetObjective(
                    "first_contact.intro.objective.enter_meeting",
                    "Enter the meeting room.");
            }
        }

        private void SubscribeGuide()
        {
            if (guide == null)
            {
                return;
            }

            guide.ReachedManualHoldPoint -= HandleGuideManualHold;
            guide.ReachedDestination -= HandleGuideDestination;
            guide.ReachedManualHoldPoint += HandleGuideManualHold;
            guide.ReachedDestination += HandleGuideDestination;
        }

        private void UnsubscribeGuide()
        {
            if (guide == null)
            {
                return;
            }

            guide.ReachedManualHoldPoint -= HandleGuideManualHold;
            guide.ReachedDestination -= HandleGuideDestination;
        }
    }
}
