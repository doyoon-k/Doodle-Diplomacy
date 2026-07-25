using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Narrative;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactIntroSequenceController : MonoBehaviour
    {
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
        [SerializeField] private Transform newsCameraAnchor;
        [SerializeField, Min(0f)] private float newsExitBlendSeconds = 0.8f;
        [Tooltip("The physical pizza sign to glance at from the vehicle. If empty, the prototype sign is found at runtime.")]
        [SerializeField] private Transform pizzaSignLookTarget;
        [SerializeField] private Transform pizzaSignCameraAnchor;
        [SerializeField, Min(0f)] private float pizzaSignFocusBlendSeconds = 0.45f;
        [SerializeField, Min(0f)] private float pizzaSignLeadSeconds = 0.25f;
        [SerializeField, Min(0f)] private float pizzaSignExitBlendSeconds = 0.55f;
        [SerializeField, Min(0f)] private float placeholderBriefingSeconds = 2f;

        private Coroutine _seatRoutine;
        private Coroutine _newsRoutine;
        private Coroutine _carArrivalRoutine;
        private FirstContactNewsSubtitleDisplay _dialogueDisplay;
        private readonly List<NarrativeBeat> _activeDialogueBeats = new();
        private bool _begun;
        private bool _busy;

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
            player?.RestoreView();
            hud?.SetCrosshairVisible(true);
            UnsubscribeGuide();
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

            if (segment == FirstContactIntroSegment.Surface)
            {
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
                    newsBroadcast.SetSubtitleHost(hud);
                    player?.LockViewTo(newsCameraAnchor);
                    hud?.SetCrosshairVisible(false);
                    _newsRoutine = StartCoroutine(NewsBroadcastRoutine());
                }
                else
                {
                    EnableVehicleExit();
                }
            }
            else
            {
                player?.SetContextualInteraction(null);
                briefingSeatInteraction?.SetAvailable(false);
                meetingRoomInteraction?.SetAvailable(false);
                hud?.SetObjective(
                    "first_contact.intro.objective.follow_to_briefing",
                    "Follow the director to the briefing room.");
                guide?.Begin(player != null ? player.transform : null);
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

            newsBroadcast?.StopBroadcast();
            _dialogueDisplay?.HideImmediate();
            player?.RestoreView();
            guide?.Stop();
            player?.SetContextualInteraction(null);
            player?.SetControlEnabled(false);
            hud?.ClearObjective();
            hud?.ClearPrompt();
            hud?.SetCrosshairVisible(true);
        }

        private IEnumerator NewsBroadcastRoutine()
        {
            yield return newsBroadcast.PlayBroadcastRoutine();
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

            yield return PlayDialogueEventRoutine("intro.car.after_news");

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

        private IEnumerator PlayDialogueEventRoutine(string triggerEvent)
        {
            NarrativeScenarioAsset scenario = newsBroadcast != null
                ? newsBroadcast.NarrativeScenario
                : null;
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
                display?.Show(beat.ResolveSpeaker(), beat.ResolveText());

                float elapsed = 0f;
                float duration = Mathf.Max(0.1f, beat.minimumSeconds);
                while (_begun && !_busy && elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }

                display?.Hide();
                NarrativeTrace.Emit(scenario.ScenarioId, beat.id, "exit");
            }
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
                    sourcePlayer.Teleport(interactable.Target);
                    sourcePlayer.SetMovementEnabled(true);
                    guide?.Begin(sourcePlayer.transform);
                    hud?.SetObjective(
                        "first_contact.intro.objective.follow_director",
                        "Follow the director.");
                    return true;

                case FirstContactIntroInteractionAction.UseElevator:
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
            sourcePlayer.SetInteractionEnabled(false);
            sourcePlayer.Teleport(interactable.Target, seated: true);
            hud?.ClearObjective();

            yield return new WaitForSeconds(placeholderBriefingSeconds);

            sourcePlayer.Teleport(interactable.SecondaryTarget, seated: false);
            sourcePlayer.SetMovementEnabled(true);
            sourcePlayer.SetInteractionEnabled(true);
            guide?.Resume();
            hud?.SetObjective(
                "first_contact.intro.objective.follow_to_meeting",
                "Follow the director to the meeting room.");
            _busy = false;
            _seatRoutine = null;
        }

        private void HandleGuideManualHold(int pointIndex)
        {
            if (segment != FirstContactIntroSegment.Facility)
            {
                return;
            }

            briefingSeatInteraction?.SetAvailable(true);
            hud?.SetObjective(
                "first_contact.intro.objective.take_briefing_seat",
                "Take your seat.");
        }

        private void HandleGuideDestination()
        {
            if (segment == FirstContactIntroSegment.Surface)
            {
                elevatorInteraction?.SetAvailable(true);
                player?.SetContextualInteraction(elevatorInteraction);
                hud?.SetObjective(
                    "first_contact.intro.objective.use_elevator",
                    "Enter the elevator.");
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
