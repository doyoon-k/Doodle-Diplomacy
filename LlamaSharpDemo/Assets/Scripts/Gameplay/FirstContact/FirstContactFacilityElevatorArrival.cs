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
        [SerializeField, Min(0.05f)] private float doorCloseSeconds = 1.15f;

        private Vector3 _leftClosedLocalPosition;
        private Quaternion _leftClosedLocalRotation;
        private Vector3 _rightClosedLocalPosition;
        private Quaternion _rightClosedLocalRotation;
        private bool _captured;
        private bool _isOpen;

        public bool IsConfigured =>
            leftDoor != null &&
            rightDoor != null &&
            leftOpenAnchor != null &&
            rightOpenAnchor != null;

        public bool IsOpen => _isOpen;

        public bool TryGetDoorFacingRotation(
            Vector3 cabinPosition,
            out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (leftDoor == null || rightDoor == null)
            {
                return false;
            }

            Vector3 doorCenter = (leftDoor.position + rightDoor.position) * 0.5f;
            Vector3 towardDoor = doorCenter - cabinPosition;
            towardDoor = Vector3.ProjectOnPlane(towardDoor, Vector3.up);
            if (towardDoor.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            rotation = Quaternion.LookRotation(towardDoor.normalized, Vector3.up);
            return true;
        }

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
            _isOpen = false;
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
            yield return OpenRoutine();
        }

        public IEnumerator OpenRoutine()
        {
            CaptureClosedState();
            if (!IsConfigured || _isOpen)
            {
                yield break;
            }

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
            _isOpen = true;
        }

        public IEnumerator CloseRoutine()
        {
            CaptureClosedState();
            if (!IsConfigured || !_isOpen)
            {
                PrepareClosed();
                yield break;
            }

            PlayOneShot(doorMotorClip, 0.55f);
            Vector3 leftStartPosition = leftDoor.localPosition;
            Quaternion leftStartRotation = leftDoor.localRotation;
            Vector3 rightStartPosition = rightDoor.localPosition;
            Quaternion rightStartRotation = rightDoor.localRotation;
            float elapsed = 0f;
            float duration = Mathf.Max(0.05f, doorCloseSeconds);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                leftDoor.SetLocalPositionAndRotation(
                    Vector3.Lerp(leftStartPosition, _leftClosedLocalPosition, progress),
                    Quaternion.Slerp(leftStartRotation, _leftClosedLocalRotation, progress));
                rightDoor.SetLocalPositionAndRotation(
                    Vector3.Lerp(rightStartPosition, _rightClosedLocalPosition, progress),
                    Quaternion.Slerp(rightStartRotation, _rightClosedLocalRotation, progress));
                yield return null;
            }

            leftDoor.SetLocalPositionAndRotation(
                _leftClosedLocalPosition,
                _leftClosedLocalRotation);
            rightDoor.SetLocalPositionAndRotation(
                _rightClosedLocalPosition,
                _rightClosedLocalRotation);
            SetDoorCollidersEnabled(true);
            _isOpen = false;
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
