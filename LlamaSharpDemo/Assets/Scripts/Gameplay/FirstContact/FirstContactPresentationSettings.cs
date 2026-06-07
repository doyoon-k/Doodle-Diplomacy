using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [CreateAssetMenu(
        fileName = "FirstContactPresentationSettings",
        menuName = "DoodleDiplomacy/First Contact/Presentation Settings")]
    public sealed class FirstContactPresentationSettings : ScriptableObject
    {
        [Header("Terminal")]
        [Min(0f)] public float questionReceiveDelay = 0.35f;
        public bool waitForTerminalTypingBeforeActions = true;
        [Min(0f)] public float questionReadHoldSeconds = 1.25f;
        [Min(0f)] public float updatedQuestionReadHoldSeconds = 0.75f;
        [Min(0f)] public float tokenUpdateDelay = 0.25f;
        [Min(0f)] public float cardRevealDelay = 0.35f;
        [Min(0.05f)] public float waveformLockSeconds = 0.75f;

        [Header("Drawing")]
        [Min(0f)] public float scanMinimumSeconds = 0.65f;
        [Min(0f)] public float labelRevealDelay = 0.15f;

        [Header("Answer")]
        [Min(0f)] public float answerTransmitHoldSeconds = 0.85f;
        [Min(0f)] public float nextQuestionDelay = 0.6f;
    }
}
