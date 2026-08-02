using System.Collections;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactFacilityElevatorArrival : MonoBehaviour
    {
        [Header("Authored Elevator Doors")]
        [SerializeField] private Transform leftDoor;
        [SerializeField] private Transform rightDoor;
        [SerializeField] private Transform leftOpenAnchor;
        [SerializeField] private Transform rightOpenAnchor;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip arrivalChimeClip;
        [SerializeField] private AudioClip doorMotorClip;

        [Header("Arrival Timing")]
        [SerializeField, Min(0f)] private float closedHoldSeconds = 1.4f;
        [SerializeField, Min(0.05f)] private float doorOpenSeconds = 1.6f;

        private Vector3 _leftClosedLocalPosition;
        private Quaternion _leftClosedLocalRotation;
        private Vector3 _rightClosedLocalPosition;
        private Quaternion _rightClosedLocalRotation;
        private bool _captured;

        public bool IsConfigured =>
            leftDoor != null &&
            rightDoor != null &&
            leftOpenAnchor != null &&
            rightOpenAnchor != null;

        private void Awake()
        {
            CaptureClosedState();
            PrepareClosed();
        }

        private void OnDisable()
        {
            PrepareClosed();
        }

        public void PrepareClosed()
        {
            CaptureClosedState();
            if (!_captured || leftDoor == null || rightDoor == null)
            {
                return;
            }

            leftDoor.SetLocalPositionAndRotation(
                _leftClosedLocalPosition,
                _leftClosedLocalRotation);
            rightDoor.SetLocalPositionAndRotation(
                _rightClosedLocalPosition,
                _rightClosedLocalRotation);
            SetDoorCollidersEnabled(true);
        }

        public IEnumerator ArriveAndOpenRoutine()
        {
            PrepareClosed();
            if (!IsConfigured)
            {
                yield break;
            }

            if (closedHoldSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(closedHoldSeconds);
            }

            PlayOneShot(arrivalChimeClip, 0.65f);
            PlayOneShot(doorMotorClip, 0.55f);

            Vector3 leftStartPosition = leftDoor.localPosition;
            Quaternion leftStartRotation = leftDoor.localRotation;
            Vector3 rightStartPosition = rightDoor.localPosition;
            Quaternion rightStartRotation = rightDoor.localRotation;
            float elapsed = 0f;
            float duration = Mathf.Max(0.05f, doorOpenSeconds);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                leftDoor.SetLocalPositionAndRotation(
                    Vector3.Lerp(leftStartPosition, leftOpenAnchor.localPosition, progress),
                    Quaternion.Slerp(leftStartRotation, leftOpenAnchor.localRotation, progress));
                rightDoor.SetLocalPositionAndRotation(
                    Vector3.Lerp(rightStartPosition, rightOpenAnchor.localPosition, progress),
                    Quaternion.Slerp(rightStartRotation, rightOpenAnchor.localRotation, progress));
                yield return null;
            }

            leftDoor.SetLocalPositionAndRotation(
                leftOpenAnchor.localPosition,
                leftOpenAnchor.localRotation);
            rightDoor.SetLocalPositionAndRotation(
                rightOpenAnchor.localPosition,
                rightOpenAnchor.localRotation);
            SetDoorCollidersEnabled(false);
        }

        private void CaptureClosedState()
        {
            if (_captured || leftDoor == null || rightDoor == null)
            {
                return;
            }

            _leftClosedLocalPosition = leftDoor.localPosition;
            _leftClosedLocalRotation = leftDoor.localRotation;
            _rightClosedLocalPosition = rightDoor.localPosition;
            _rightClosedLocalRotation = rightDoor.localRotation;
            _captured = true;
        }

        private void SetDoorCollidersEnabled(bool enabled)
        {
            SetCollidersEnabled(leftDoor, enabled);
            SetCollidersEnabled(rightDoor, enabled);
        }

        private static void SetCollidersEnabled(Transform root, bool enabled)
        {
            if (root == null)
            {
                return;
            }

            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = enabled;
            }
        }

        private void PlayOneShot(AudioClip clip, float volume)
        {
            if (audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip, volume);
            }
        }

        private void OnDrawGizmosSelected()
        {
            DrawDoorTravel(leftDoor, leftOpenAnchor);
            DrawDoorTravel(rightDoor, rightOpenAnchor);
        }

        private static void DrawDoorTravel(Transform door, Transform openAnchor)
        {
            if (door == null || openAnchor == null)
            {
                return;
            }

            Gizmos.color = new Color(0.25f, 0.85f, 1f, 0.9f);
            Gizmos.DrawLine(door.position, openAnchor.position);
            Gizmos.DrawWireSphere(openAnchor.position, 0.08f);
        }
    }
}
