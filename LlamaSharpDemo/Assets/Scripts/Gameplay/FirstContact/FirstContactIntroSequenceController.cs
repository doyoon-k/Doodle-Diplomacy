using System.Collections;
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
        [SerializeField, Min(0f)] private float placeholderBriefingSeconds = 2f;

        private Coroutine _seatRoutine;
        private bool _begun;
        private bool _busy;

        public bool IsBusy => _busy;
        public FirstContactIntroSegment Segment => segment;

        public void Configure(
            FirstContactIntroSegment sceneSegment,
            FirstContactIntroMode introMode,
            FirstContactIntroPlayerController playerController,
            FirstContactIntroHud introHud,
            FirstContactIntroGuideController guideController,
            FirstContactIntroInteractable exitVehicle,
            FirstContactIntroInteractable elevator,
            FirstContactIntroInteractable briefingSeat,
            FirstContactIntroInteractable meetingRoom)
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
        }

        private IEnumerator Start()
        {
            yield return null;
            if (!_begun && GameplayModeHost.Instance == null)
            {
                Begin();
            }
        }

        private void OnEnable()
        {
            SubscribeGuide();
        }

        private void OnDisable()
        {
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
                exitVehicleInteraction?.SetAvailable(true);
                elevatorInteraction?.SetAvailable(false);
                player?.SetMovementEnabled(false);
                player?.SetContextualInteraction(exitVehicleInteraction);
                hud?.SetObjective(
                    "first_contact.intro.objective.exit_vehicle",
                    "Exit the vehicle.");
                guide?.Stop();
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

            guide?.Stop();
            player?.SetContextualInteraction(null);
            player?.SetControlEnabled(false);
            hud?.ClearObjective();
            hud?.ClearPrompt();
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
