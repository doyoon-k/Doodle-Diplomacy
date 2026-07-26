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
        [SerializeField] private Transform secretWallPanel;
        [SerializeField] private Transform wallOpenAnchor;
        [SerializeField] private Transform directorStandAnchor;
        [SerializeField] private Transform knockTarget;
        [SerializeField] private BoxCollider elevatorBoardingVolume;

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

            if (secretWallPanel != null)
            {
                secretWallPanel.SetPositionAndRotation(
                    _wallClosedPosition,
                    _wallClosedRotation);
                SetWallColliderEnabled(true);
            }

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
            if (movableShelf != null && shelfOpenAnchor != null)
            {
                yield return MoveTransformRoutine(
                    movableShelf,
                    shelfOpenAnchor.position,
                    shelfOpenAnchor.rotation,
                    shelfMoveSeconds);
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
    }
}
