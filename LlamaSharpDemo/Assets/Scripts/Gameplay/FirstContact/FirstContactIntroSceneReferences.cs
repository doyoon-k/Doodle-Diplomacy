using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public enum FirstContactIntroSegment
    {
        Surface,
        Facility
    }

    [DisallowMultipleComponent]
    public sealed class FirstContactIntroSceneReferences : MonoBehaviour
    {
        [Header("Scene")]
        [SerializeField] private string sceneId = "first-contact-intro";
        [SerializeField] private FirstContactIntroSegment segment;

        [Header("Roots")]
        [SerializeField] private Transform environmentRoot;
        [SerializeField] private Transform characterRoot;
        [SerializeField] private Transform cinematicRoot;
        [SerializeField] private Transform triggerRoot;
        [SerializeField] private Transform audioRoot;

        [Header("Traversal")]
        [SerializeField] private Transform playerSpawn;
        [SerializeField] private Transform entryPoint;
        [SerializeField] private Transform exitPoint;
        [SerializeField] private Transform[] routePoints = System.Array.Empty<Transform>();

        public string SceneId => sceneId;
        public FirstContactIntroSegment Segment => segment;
        public Transform EnvironmentRoot => environmentRoot;
        public Transform CharacterRoot => characterRoot;
        public Transform CinematicRoot => cinematicRoot;
        public Transform TriggerRoot => triggerRoot;
        public Transform AudioRoot => audioRoot;
        public Transform PlayerSpawn => playerSpawn;
        public Transform EntryPoint => entryPoint;
        public Transform ExitPoint => exitPoint;
        public Transform[] RoutePoints => routePoints;

        public void Configure(
            string id,
            FirstContactIntroSegment sceneSegment,
            Transform environment,
            Transform characters,
            Transform cinematics,
            Transform triggers,
            Transform audio,
            Transform spawn,
            Transform entry,
            Transform exit,
            Transform[] route)
        {
            sceneId = id;
            segment = sceneSegment;
            environmentRoot = environment;
            characterRoot = characters;
            cinematicRoot = cinematics;
            triggerRoot = triggers;
            audioRoot = audio;
            playerSpawn = spawn;
            entryPoint = entry;
            exitPoint = exit;
            routePoints = route ?? System.Array.Empty<Transform>();
        }

        private void OnDrawGizmosSelected()
        {
            if (routePoints == null || routePoints.Length == 0)
            {
                return;
            }

            Gizmos.color = new Color(0.15f, 0.85f, 1f, 0.9f);
            Transform previous = null;
            foreach (Transform point in routePoints)
            {
                if (point == null)
                {
                    continue;
                }

                Gizmos.DrawSphere(point.position, 0.18f);
                if (previous != null)
                {
                    Gizmos.DrawLine(previous.position, point.position);
                }

                previous = point;
            }
        }
    }
}
