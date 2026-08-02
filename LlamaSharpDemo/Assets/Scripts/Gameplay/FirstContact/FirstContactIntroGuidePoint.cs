using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactIntroGuidePoint : MonoBehaviour
    {
        [SerializeField] private string displayName = "Guide Target";
        [SerializeField, Min(0)] private int routeOrder;
        [SerializeField] private bool pauseOnArrival;
        [SerializeField, Min(0.05f)] private float gizmoRadius = 0.28f;
        [SerializeField] private Color routeColor = new(0.2f, 0.8f, 1f, 0.9f);

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? gameObject.name
            : displayName;
        public int RouteOrder => routeOrder;
        public bool PauseOnArrival => pauseOnArrival;
        public float GizmoRadius => gizmoRadius;
        public Color RouteColor => routeColor;

        public void Configure(string pointDisplayName, int order, bool pause)
        {
            displayName = pointDisplayName;
            routeOrder = Mathf.Max(0, order);
            pauseOnArrival = pause;
            routeColor = pause
                ? new Color(1f, 0.35f, 0.15f, 0.95f)
                : new Color(0.2f, 0.8f, 1f, 0.9f);
        }

        private void OnValidate()
        {
            routeOrder = Mathf.Max(0, routeOrder);
            gizmoRadius = Mathf.Max(0.05f, gizmoRadius);
        }

        private void OnDrawGizmos()
        {
            Color previousColor = Gizmos.color;
            Color color = pauseOnArrival
                ? new Color(1f, 0.35f, 0.15f, 0.95f)
                : routeColor;
            Gizmos.color = color;
            Gizmos.DrawSphere(transform.position, gizmoRadius);
            Gizmos.DrawLine(
                transform.position,
                transform.position + Vector3.up * 1.1f);

            Vector3 arrowOrigin = transform.position + Vector3.up * 0.15f;
            Vector3 forward = transform.forward * (gizmoRadius * 2.4f);
            Gizmos.DrawLine(arrowOrigin, arrowOrigin + forward);
            Gizmos.DrawLine(
                arrowOrigin + forward,
                arrowOrigin + forward * 0.65f + transform.right * gizmoRadius * 0.6f);
            Gizmos.DrawLine(
                arrowOrigin + forward,
                arrowOrigin + forward * 0.65f - transform.right * gizmoRadius * 0.6f);
            Gizmos.color = previousColor;
        }
    }
}
