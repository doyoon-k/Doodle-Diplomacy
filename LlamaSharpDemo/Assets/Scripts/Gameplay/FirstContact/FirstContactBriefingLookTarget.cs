using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactBriefingLookTarget : MonoBehaviour
    {
        [SerializeField] private Color sceneViewColor = new(1f, 0.68f, 0.15f, 0.95f);

        public Color SceneViewColor => sceneViewColor;
    }
}
