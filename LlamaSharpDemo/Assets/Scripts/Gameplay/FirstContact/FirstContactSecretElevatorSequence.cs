using System.Collections;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactSecretElevatorSequence : MonoBehaviour
    {
        // All moving geometry and timing anchors are authored in FC_Intro_Surface.
        [Header("Secret Entrance")]
        [SerializeField] private Transform movableShelf;
        [SerializeField] private Transform shelfOpenAnchor;
        [SerializeField] private Transform secondaryMovableShelf;
        [SerializeField] private Transform secondaryShelfOpenAnchor;
        [SerializeField] private Transform floorHatch;
        [SerializeField] private Transform hatchOpenAnchor;
        [SerializeField] private Transform secretWallPanel;
        [SerializeField] private Transform wallOpenAnchor;
        [SerializeField] private Transform directorStandAnchor;
        [SerializeField] private Transform knockTarget;
        [SerializeField] private Transform directorBoardingAnchor;
        [SerializeField] private BoxCollider elevatorBoardingVolume;
        [SerializeField] private FirstContactFacilityElevatorArrival elevatorDoors;

        [Header("Elevator Ride")]
        [SerializeField] private Transform elevatorCabinMotionRoot;
        [SerializeField] private Light elevatorCabinLight;
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private AudioClip shelfMoveClip;
        [SerializeField] private AudioClip knockClip;
        [SerializeField] private AudioClip doorMotorClip;
        [SerializeField] private AudioClip descentLoopClip;

        [Header("Timing")]
        [SerializeField, Min(0.05f)] private float directorApproachSeconds = 0.7f;
        [SerializeField, Min(0.05f)] private float shelfMoveSeconds = 0.9f;
        [SerializeField, Min(0f)] private float pauseBeforeFirstKnock = 0.3f;
        [SerializeField, Min(0f)] private float knockGapSeconds = 0.42f;
        [SerializeField, Min(0.05f)] private float wallOpenSeconds = 1.15f;
        [SerializeField, Min(0.05f)] private float wallCloseSeconds = 0.9f;
        [SerializeField, Min(0f)] private float unlockPauseSeconds = 0.35f;

        private Vector3 _shelfClosedPosition;
        private Quaternion _shelfClosedRotation;
        private Vector3 _secondaryShelfClosedPosition;
        private Quaternion _secondaryShelfClosedRotation;
        private Vector3 _hatchClosedPosition;
        private Quaternion _hatchClosedRotation;
        private Vector3 _wallClosedPosition;
        private Quaternion _wallClosedRotation;
        private Vector3 _cabinRestLocalPosition;
        private float _cabinLightRestIntensity;
        private Coroutine _descentLoopRoutine;
        private AudioClip _fallbackKnockClip;
        private AudioClip _fallbackMotorClip;
        private AudioClip _fallbackDescentClip;
        private bool _captured;
        private bool _revealed;

        public bool IsRevealed => _revealed;
        public bool AreElevatorDoorsOpen =>
            elevatorDoors != null && elevatorDoors.IsOpen;
        public Transform ElevatorTransitionSpace =>
            elevatorDoors != null
                ? elevatorDoors.transform
                : elevatorBoardingVolume != null
                    ? elevatorBoardingVolume.transform
                    : null;

        public bool TryGetDoorFacingRotation(out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (elevatorBoardingVolume == null)
            {
                return false;
            }

            Vector3 cabinCenter =
                elevatorBoardingVolume.transform.TransformPoint(
                    elevatorBoardingVolume.center);
            if (elevatorDoors != null &&
                elevatorDoors.TryGetDoorFacingRotation(cabinCenter, out rotation))
            {
                return true;
            }

            if (secretWallPanel == null)
            {
                return false;
            }

            Vector3 towardDoor = secretWallPanel.position - cabinCenter;
            towardDoor = Vector3.ProjectOnPlane(towardDoor, Vector3.up);
            if (towardDoor.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            rotation = Quaternion.LookRotation(towardDoor.normalized, Vector3.up);
            return true;
        }

        public void Configure(
            Transform shelf,
            Transform openShelfAnchor,
            Transform wallPanel,
            Transform openWallAnchor,
            Transform directorAnchor,
            Transform wallKnockTarget,
            BoxCollider boardingVolume,
            Transform cabinMotionRoot,
            Light cabinLight,
            AudioSource source)
        {
            movableShelf = shelf;
            shelfOpenAnchor = openShelfAnchor;
            secretWallPanel = wallPanel;
            wallOpenAnchor = openWallAnchor;
            directorStandAnchor = directorAnchor;
            knockTarget = wallKnockTarget;
            elevatorBoardingVolume = boardingVolume;
            elevatorCabinMotionRoot = cabinMotionRoot;
            elevatorCabinLight = cabinLight;
            audioSource = source;
            CaptureInitialState(force: true);
        }

        private void Awake()
        {
            CaptureInitialState(force: false);
            ResetSequence();
        }

        private void OnDisable()
        {
            StopDescent();
        }

        public void ResetSequence()
        {
            CaptureInitialState(force: false);
            StopDescent();
            if (movableShelf != null)
            {
                movableShelf.SetPositionAndRotation(
                    _shelfClosedPosition,
                    _shelfClosedRotation);
            }

            if (secondaryMovableShelf != null)
            {
                secondaryMovableShelf.SetPositionAndRotation(
                    _secondaryShelfClosedPosition,
                    _secondaryShelfClosedRotation);
            }

            if (floorHatch != null)
            {
                floorHatch.SetPositionAndRotation(
                    _hatchClosedPosition,
                    _hatchClosedRotation);
                SetCollidersEnabled(floorHatch, true);
            }

            if (secretWallPanel != null)
            {
                secretWallPanel.SetPositionAndRotation(
                    _wallClosedPosition,
                    _wallClosedRotation);
                SetWallColliderEnabled(true);
            }

            elevatorDoors?.PrepareClosed();

            _revealed = false;
        }

        public IEnumerator RevealRoutine(Transform director)
        {
            if (_revealed)
            {
                yield break;
            }

            if (director != null && directorStandAnchor != null)
            {
                yield return MoveActorRoutine(
                    director,
                    directorStandAnchor.position,
                    directorStandAnchor.rotation,
                    directorApproachSeconds);
            }

            PlayOneShot(shelfMoveClip ?? GetFallbackMotorClip(), 0.55f);
            bool hasSecondShelf = movableShelf != null &&
                                  shelfOpenAnchor != null &&
                                  secondaryMovableShelf != null &&
                                  secondaryShelfOpenAnchor != null;
            if (hasSecondShelf)
            {
                yield return MoveTransformPairRoutine(
                    movableShelf,
                    shelfOpenAnchor,
                    secondaryMovableShelf,
                    secondaryShelfOpenAnchor,
                    shelfMoveSeconds);
            }
            else if (movableShelf != null && shelfOpenAnchor != null)
            {
                yield return MoveTransformRoutine(
                    movableShelf,
                    shelfOpenAnchor.position,
                    shelfOpenAnchor.rotation,
                    shelfMoveSeconds);
            }

            if (floorHatch != null && hatchOpenAnchor != null)
            {
                if (unlockPauseSeconds > 0f)
                {
                    yield return new WaitForSecondsRealtime(unlockPauseSeconds);
                }

                PlayOneShot(doorMotorClip ?? GetFallbackMotorClip(), 0.65f);
                yield return MoveTransformRoutine(
                    floorHatch,
                    hatchOpenAnchor.position,
                    hatchOpenAnchor.rotation,
                    wallOpenSeconds);
                // The open panel slides underneath the surrounding authored floor.
                // Its collider must not remain in the stair mouth after the visual
                // has finished moving, especially when the route is rebaked nearby.
                SetCollidersEnabled(floorHatch, false);
                _revealed = true;
                yield break;
            }

            if (pauseBeforeFirstKnock > 0f)
            {
                yield return new WaitForSecondsRealtime(pauseBeforeFirstKnock);
            }

            yield return KnockRoutine(director);
            if (knockGapSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(knockGapSeconds);
            }

            yield return KnockRoutine(director);
            if (unlockPauseSeconds > 0f)
            {
                yield return new WaitForSecondsRealtime(unlockPauseSeconds);
            }

            PlayOneShot(doorMotorClip ?? GetFallbackMotorClip(), 0.65f);
            if (secretWallPanel != null && wallOpenAnchor != null)
            {
                yield return MoveTransformRoutine(
                    secretWallPanel,
                    wallOpenAnchor.position,
                    wallOpenAnchor.rotation,
                    wallOpenSeconds);
            }

            SetWallColliderEnabled(false);
            _revealed = true;
        }

        public IEnumerator CallElevatorRoutine()
        {
            if (!_revealed || elevatorDoors == null)
            {
                yield break;
            }

            yield return elevatorDoors.ArriveAndOpenRoutine();
        }

        public IEnumerator BoardDirectorRoutine(Transform director)
        {
            if (director == null || directorBoardingAnchor == null)
            {
                yield break;
            }

            yield return MoveActorRoutine(
                director,
                directorBoardingAnchor.position,
                directorBoardingAnchor.rotation,
                directorApproachSeconds);
        }

        public bool IsInsideElevator(Transform actor)
        {
            if (actor == null || elevatorBoardingVolume == null)
            {
                return false;
            }

            Vector3 localPoint = elevatorBoardingVolume.transform.InverseTransformPoint(
                actor.position);
            Vector3 center = elevatorBoardingVolume.center;
            Vector3 halfSize = elevatorBoardingVolume.size * 0.5f;
            return Mathf.Abs(localPoint.x - center.x) <= halfSize.x &&
                   Mathf.Abs(localPoint.z - center.z) <= halfSize.z;
        }

        public IEnumerator CloseDoorRoutine()
        {
            if (elevatorDoors != null && elevatorDoors.IsConfigured)
            {
                yield return elevatorDoors.CloseRoutine();
                yield break;
            }

            if (!_revealed || secretWallPanel == null)
            {
                yield break;
            }

            PlayOneShot(doorMotorClip ?? GetFallbackMotorClip(), 0.65f);
            yield return MoveTransformRoutine(
                secretWallPanel,
                _wallClosedPosition,
                _wallClosedRotation,
                wallCloseSeconds);
            SetWallColliderEnabled(true);
        }

        public void BeginDescent()
        {
            if (_descentLoopRoutine != null)
            {
                return;
            }

            if (audioSource != null)
            {
                audioSource.clip = descentLoopClip ?? GetFallbackDescentClip();
                audioSource.loop = true;
                audioSource.volume = 0.22f;
                audioSource.Play();
            }

            _descentLoopRoutine = StartCoroutine(DescentLoopRoutine());
        }

        public void StopDescent()
        {
            if (_descentLoopRoutine != null)
            {
                StopCoroutine(_descentLoopRoutine);
                _descentLoopRoutine = null;
            }

            if (elevatorCabinMotionRoot != null && _captured)
            {
                elevatorCabinMotionRoot.localPosition = _cabinRestLocalPosition;
            }

            if (elevatorCabinLight != null && _captured)
            {
                elevatorCabinLight.intensity = _cabinLightRestIntensity;
            }

            if (audioSource != null && audioSource.loop)
            {
                audioSource.Stop();
                audioSource.loop = false;
                audioSource.clip = null;
            }
        }

        private IEnumerator KnockRoutine(Transform director)
        {
            PlayOneShot(knockClip ?? GetFallbackKnockClip(), 0.9f);
            if (director == null || knockTarget == null)
            {
                yield return new WaitForSecondsRealtime(0.14f);
                yield break;
            }

            Vector3 start = director.position;
            Vector3 direction = knockTarget.position - start;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.001f)
            {
                yield return new WaitForSecondsRealtime(0.14f);
                yield break;
            }

            direction.Normalize();
            float elapsed = 0f;
            const float duration = 0.18f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float phase = Mathf.Clamp01(elapsed / duration);
                float reach = Mathf.Sin(phase * Mathf.PI) * 0.07f;
                director.position = start + direction * reach;
                yield return null;
            }

            director.position = start;
        }

        private IEnumerator DescentLoopRoutine()
        {
            float elapsed = 0f;
            while (true)
            {
                elapsed += Time.unscaledDeltaTime;
                if (elevatorCabinMotionRoot != null)
                {
                    float vertical = Mathf.Sin(elapsed * 37f) * 0.008f;
                    float lateral = Mathf.Sin(elapsed * 19f) * 0.004f;
                    elevatorCabinMotionRoot.localPosition =
                        _cabinRestLocalPosition +
                        new Vector3(lateral, vertical, 0f);
                }

                if (elevatorCabinLight != null)
                {
                    float flutter = 0.94f + Mathf.PerlinNoise(elapsed * 4f, 0.35f) * 0.08f;
                    elevatorCabinLight.intensity =
                        _cabinLightRestIntensity * flutter;
                }

                yield return null;
            }
        }

        private void CaptureInitialState(bool force)
        {
            if (_captured && !force)
            {
                return;
            }

            if (movableShelf != null)
            {
                _shelfClosedPosition = movableShelf.position;
                _shelfClosedRotation = movableShelf.rotation;
            }

            if (secondaryMovableShelf != null)
            {
                _secondaryShelfClosedPosition = secondaryMovableShelf.position;
                _secondaryShelfClosedRotation = secondaryMovableShelf.rotation;
            }

            if (floorHatch != null)
            {
                _hatchClosedPosition = floorHatch.position;
                _hatchClosedRotation = floorHatch.rotation;
            }

            if (secretWallPanel != null)
            {
                _wallClosedPosition = secretWallPanel.position;
                _wallClosedRotation = secretWallPanel.rotation;
            }

            if (elevatorCabinMotionRoot != null)
            {
                _cabinRestLocalPosition = elevatorCabinMotionRoot.localPosition;
            }

            if (elevatorCabinLight != null)
            {
                _cabinLightRestIntensity = elevatorCabinLight.intensity;
            }

            _captured = true;
        }

        private void SetWallColliderEnabled(bool enabled)
        {
            if (secretWallPanel == null)
            {
                return;
            }

            Collider[] colliders = secretWallPanel.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = enabled;
            }
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

        private AudioClip GetFallbackKnockClip()
        {
            if (_fallbackKnockClip == null)
            {
                _fallbackKnockClip = CreateProceduralClip(
                    "SecretDoor_Knock_Fallback",
                    0.13f,
                    115f,
                    0.55f,
                    true);
            }

            return _fallbackKnockClip;
        }

        private AudioClip GetFallbackMotorClip()
        {
            if (_fallbackMotorClip == null)
            {
                _fallbackMotorClip = CreateProceduralClip(
                    "SecretDoor_Motor_Fallback",
                    0.75f,
                    58f,
                    0.16f,
                    false);
            }

            return _fallbackMotorClip;
        }

        private AudioClip GetFallbackDescentClip()
        {
            if (_fallbackDescentClip == null)
            {
                _fallbackDescentClip = CreateProceduralClip(
                    "Elevator_Descent_Fallback",
                    0.9f,
                    46f,
                    0.12f,
                    false);
            }

            return _fallbackDescentClip;
        }

        private static AudioClip CreateProceduralClip(
            string clipName,
            float duration,
            float frequency,
            float amplitude,
            bool percussive)
        {
            const int sampleRate = 22050;
            int sampleCount = Mathf.Max(1, Mathf.RoundToInt(duration * sampleRate));
            var samples = new float[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float time = i / (float)sampleRate;
                float envelope = percussive
                    ? Mathf.Exp(-time * 28f)
                    : 0.72f + Mathf.Sin(time * 8f) * 0.08f;
                float fundamental = Mathf.Sin(time * frequency * Mathf.PI * 2f);
                float harmonic = Mathf.Sin(time * frequency * 2.03f * Mathf.PI * 2f) * 0.32f;
                float noise = (Mathf.PerlinNoise(i * 0.071f, 0.43f) - 0.5f) *
                              (percussive ? 0.8f : 0.2f);
                samples[i] = (fundamental + harmonic + noise) * amplitude * envelope;
            }

            AudioClip clip = AudioClip.Create(
                clipName,
                sampleCount,
                1,
                sampleRate,
                false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static IEnumerator MoveActorRoutine(
            Transform actor,
            Vector3 targetPosition,
            Quaternion targetRotation,
            float seconds)
        {
            Vector3 startPosition = actor.position;
            Quaternion startRotation = actor.rotation;
            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, seconds);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                actor.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, targetPosition, progress),
                    Quaternion.Slerp(startRotation, targetRotation, progress));
                yield return null;
            }

            actor.SetPositionAndRotation(targetPosition, targetRotation);
        }

        private static IEnumerator MoveTransformRoutine(
            Transform movingTransform,
            Vector3 targetPosition,
            Quaternion targetRotation,
            float seconds)
        {
            Vector3 startPosition = movingTransform.position;
            Quaternion startRotation = movingTransform.rotation;
            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, seconds);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                movingTransform.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, targetPosition, progress),
                    Quaternion.Slerp(startRotation, targetRotation, progress));
                yield return null;
            }

            movingTransform.SetPositionAndRotation(targetPosition, targetRotation);
        }

        private static IEnumerator MoveTransformPairRoutine(
            Transform first,
            Transform firstTarget,
            Transform second,
            Transform secondTarget,
            float seconds)
        {
            if (first == null || firstTarget == null ||
                second == null || secondTarget == null)
            {
                yield break;
            }

            Vector3 firstStartPosition = first.position;
            Quaternion firstStartRotation = first.rotation;
            Vector3 secondStartPosition = second.position;
            Quaternion secondStartRotation = second.rotation;
            float elapsed = 0f;
            float duration = Mathf.Max(0.01f, seconds);
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                first.SetPositionAndRotation(
                    Vector3.Lerp(firstStartPosition, firstTarget.position, progress),
                    Quaternion.Slerp(firstStartRotation, firstTarget.rotation, progress));
                second.SetPositionAndRotation(
                    Vector3.Lerp(secondStartPosition, secondTarget.position, progress),
                    Quaternion.Slerp(secondStartRotation, secondTarget.rotation, progress));
                yield return null;
            }

            first.SetPositionAndRotation(firstTarget.position, firstTarget.rotation);
            second.SetPositionAndRotation(secondTarget.position, secondTarget.rotation);
        }
    }
}
