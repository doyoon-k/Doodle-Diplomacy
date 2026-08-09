using DoodleDiplomacy.Localization;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public enum FirstContactIntroInteractionAction
    {
        ExitVehicle,
        UseElevator,
        TakeBriefingSeat,
        EnterMeetingRoom,
        TakeMeetingSeat
    }

    [DisallowMultipleComponent]
    public sealed class FirstContactIntroInteractable : MonoBehaviour
    {
        [SerializeField] private FirstContactIntroInteractionAction action;
        [SerializeField] private string promptLocalizationKey = string.Empty;
        [SerializeField] private string promptFallback = "[ E ]  INTERACT";
        [SerializeField] private Transform target;
        [SerializeField] private Transform secondaryTarget;
        [SerializeField] private FirstContactIntroSequenceController sequenceController;
        [SerializeField] private bool available = true;

        public FirstContactIntroInteractionAction Action => action;
        public Transform Target => target;
        public Transform SecondaryTarget => secondaryTarget;
        public bool IsAvailable => available && isActiveAndEnabled;
        public string PromptLocalizationKey => promptLocalizationKey;
        public string PromptFallback => promptFallback;

        public void Configure(
            FirstContactIntroInteractionAction interactionAction,
            string localizationKey,
            string fallback,
            Transform primaryTarget,
            Transform alternateTarget,
            FirstContactIntroSequenceController controller,
            bool isAvailable)
        {
            action = interactionAction;
            promptLocalizationKey = localizationKey ?? string.Empty;
            promptFallback = fallback ?? string.Empty;
            target = primaryTarget;
            secondaryTarget = alternateTarget;
            sequenceController = controller;
            available = isAvailable;
        }

        public void SetAvailable(bool value)
        {
            available = value;
        }

        public string ResolvePrompt()
        {
            return L10n.T(promptLocalizationKey, promptFallback);
        }

        public bool TryInteract(FirstContactIntroPlayerController player)
        {
            if (!IsAvailable || player == null)
            {
                return false;
            }

            sequenceController = sequenceController != null
                ? sequenceController
                : FindFirstObjectByType<FirstContactIntroSequenceController>();
            return sequenceController != null && sequenceController.HandleInteraction(this, player);
        }
    }
}
