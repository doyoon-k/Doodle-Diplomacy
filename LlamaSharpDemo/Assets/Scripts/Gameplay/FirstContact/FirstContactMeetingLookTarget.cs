using DoodleDiplomacy.Narrative;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactMeetingLookTarget : MonoBehaviour
    {
        [SerializeField] private MeetingLookTarget target = MeetingLookTarget.KeepCurrent;
        [SerializeField] private Color sceneViewColor = new(0.25f, 0.9f, 0.72f, 0.95f);

        public MeetingLookTarget Target => target;
        public Color SceneViewColor => sceneViewColor;

        public void Configure(MeetingLookTarget lookTarget)
        {
            target = lookTarget;
        }
    }
}
