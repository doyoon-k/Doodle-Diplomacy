using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Camera;
using DoodleDiplomacy.Data;
using DoodleDiplomacy.Devices;
using DoodleDiplomacy.Narrative;
using Unity.Cinemachine;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactMeetingArrivalController : MonoBehaviour
    {
        private const string MeetingSectionId = "meeting_room_arrival";
        private const string AfterIntroEntryId = "first-contact-translation-after-intro";

        [Header("Camera")]
        [Tooltip("Existing gameplay camera owner. Its Default camera remains the authoritative president seat pose.")]
        [SerializeField] private CameraController cameraController;
        [Tooltip("Legacy authored arrival view kept only for scene compatibility. The Facility walk-in flow does not activate it.")]
        [SerializeField] private CinemachineCamera arrivalCamera;
        [Tooltip("The existing CM_Default camera. The sequence never replaces or offsets this seated pose.")]
        [SerializeField] private CinemachineCamera seatedCamera;
        [SerializeField, Min(0f)] private float lookBlendSeconds = 0.48f;
        [SerializeField, Min(0f)] private float restoreBlendSeconds = 0.55f;

        [Header("Meeting Staging")]
        [SerializeField] private Transform doctorHwangActor;
        [SerializeField] private Transform directorActor;
        [SerializeField] private Transform obamaActor;
        [SerializeField] private Transform obamaStart;
        [SerializeField] private Transform obamaCoffeeStop;
        [SerializeField] private Transform coffeeProp;
        [SerializeField] private Transform coffeeDropPoint;
        [SerializeField, Min(0.1f)] private float obamaEntranceSeconds = 1.65f;
        [SerializeField, Min(0f)] private float coffeeSettleSeconds = 0.35f;

        [Header("Look Targets")]
        [SerializeField] private FirstContactMeetingLookTarget[] lookTargets =
            Array.Empty<FirstContactMeetingLookTarget>();

        private readonly List<NarrativeBeat> _meetingBeats = new();
        private GameplayModeContext _context;
        private Vector3 _seatedPosition;
        private Quaternion _seatedRotation;
        private bool _hasCapturedSeatedPose;
        private bool _isPlaying;
        private bool _cancelRequested;

        public Transform SeatedViewPreview => seatedCamera != null
            ? seatedCamera.transform
            : null;
        public CinemachineCamera ArrivalCamera => arrivalCamera;
        public CinemachineCamera SeatedCamera => seatedCamera;
        public Transform DoctorHwangActor => doctorHwangActor;
        public Transform DirectorActor => directorActor;
        public bool IsPlaying => _isPlaying;

        public bool ShouldPlay(GameplayModeContext context, bool isFirstPlay)
        {
            if (!isFirstPlay || context == null || !isActiveAndEnabled)
            {
                return false;
            }

            return context.Services.TryGet(out FlowEntryDefinition entry) &&
                   entry != null &&
                   string.Equals(
                       entry.entryId,
                       AfterIntroEntryId,
                       StringComparison.Ordinal);
        }

        public IEnumerator PlayRoutine(
            GameplayModeContext context,
            NarrativeScenarioAsset scenario)
        {
            if (_isPlaying)
            {
                yield break;
            }

            ResolveReferences();
            if (!ValidateConfiguration(logErrors: true))
            {
                yield break;
            }

            _isPlaying = true;
            _cancelRequested = false;
            _context = context;
            CaptureSeatedPose();
            PrepareAuthoredStage();

            try
            {
                // The president now walks into the authored meeting room and
                // takes the seat in the Facility scene. The embedded gameplay
                // systems activate on that exact pose, so no second sit prompt or
                // temporary arrival camera is needed here.
                RestoreSeatedViewImmediate();
                yield return null;

                yield return PlayObamaEntranceRoutine();
                if (_cancelRequested)
                {
                    yield break;
                }

                CollectMeetingBeats(scenario);
                for (int i = 0; i < _meetingBeats.Count && !_cancelRequested; i++)
                {
                    NarrativeBeat beat = _meetingBeats[i];
                    yield return PlayBeatRoutine(
                        scenario != null ? scenario.ScenarioId : "first_contact_day1",
                        beat);
                }

                if (!_cancelRequested)
                {
                    yield return RestoreSeatedViewRoutine(restoreBlendSeconds);
                }
            }
            finally
            {
                _context?.Subtitles?.SetAdvancePromptVisible(false);
                _context?.Subtitles?.Hide();
                RestoreSeatedViewImmediate();
                _context = null;
                _isPlaying = false;
                _cancelRequested = false;
            }
        }

        public void StopPresentation()
        {
            if (!_isPlaying)
            {
                return;
            }

            _cancelRequested = true;
            _context?.Subtitles?.SetAdvancePromptVisible(false);
            _context?.Subtitles?.Hide();
            RestoreSeatedViewImmediate();
        }

        public void RebindDirector(
            Transform actor,
            FirstContactMeetingLookTarget lookTarget)
        {
            RebindMeetingActor(
                ref directorActor,
                actor,
                MeetingLookTarget.Director,
                lookTarget);
        }

        public void RebindDoctorHwang(
            Transform actor,
            FirstContactMeetingLookTarget lookTarget = null)
        {
            RebindMeetingActor(
                ref doctorHwangActor,
                actor,
                MeetingLookTarget.Hwang,
                lookTarget);
        }

        private void RebindMeetingActor(
            ref Transform currentActor,
            Transform actor,
            MeetingLookTarget targetType,
            FirstContactMeetingLookTarget lookTarget)
        {
            if (actor == null)
            {
                return;
            }

            Transform previousActor = currentActor;
            lookTarget = lookTarget != null && lookTarget.Target == targetType
                ? lookTarget
                : ResolveLookTargetComponent(targetType);
            if (lookTarget != null && !lookTarget.transform.IsChildOf(actor))
            {
                // Preserve the authored local head/face offset while moving the
                // target from a retired meeting placeholder to the carried actor.
                lookTarget.transform.SetParent(actor, false);
            }

            currentActor = actor;
            ReplaceLookTarget(targetType, lookTarget);

            if (previousActor != null && previousActor != actor)
            {
                previousActor.gameObject.SetActive(false);
            }
        }

        private void ReplaceLookTarget(
            MeetingLookTarget targetType,
            FirstContactMeetingLookTarget lookTarget)
        {
            if (lookTarget == null)
            {
                return;
            }

            lookTargets ??= Array.Empty<FirstContactMeetingLookTarget>();
            for (int i = 0; i < lookTargets.Length; i++)
            {
                FirstContactMeetingLookTarget candidate = lookTargets[i];
                if (candidate != null && candidate.Target == targetType)
                {
                    lookTargets[i] = lookTarget;
                    return;
                }
            }

            int index = lookTargets.Length;
            Array.Resize(ref lookTargets, index + 1);
            lookTargets[index] = lookTarget;
        }

        private FirstContactMeetingLookTarget ResolveLookTargetComponent(
            MeetingLookTarget targetType)
        {
            if (lookTargets == null)
            {
                return null;
            }

            for (int i = 0; i < lookTargets.Length; i++)
            {
                FirstContactMeetingLookTarget candidate = lookTargets[i];
                if (candidate != null && candidate.Target == targetType)
                {
                    return candidate;
                }
            }

            return null;
        }

        public bool ValidateConfiguration(bool logErrors)
        {
            bool valid = true;
            valid &= Require(cameraController, nameof(cameraController), logErrors);
            valid &= Require(seatedCamera, nameof(seatedCamera), logErrors);
            valid &= Require(obamaActor, nameof(obamaActor), logErrors);
            valid &= Require(obamaStart, nameof(obamaStart), logErrors);
            valid &= Require(obamaCoffeeStop, nameof(obamaCoffeeStop), logErrors);
            valid &= Require(coffeeProp, nameof(coffeeProp), logErrors);
            valid &= Require(coffeeDropPoint, nameof(coffeeDropPoint), logErrors);
            return valid;
        }

        public void Configure(
            CameraController cameraOwner,
            CinemachineCamera arrival,
            CinemachineCamera seated,
            Transform hwang,
            Transform director,
            Transform obama,
            Transform obamaEntrance,
            Transform obamaCoffee,
            Transform coffee,
            Transform coffeeDrop,
            FirstContactMeetingLookTarget[] targets)
        {
            cameraController = cameraOwner;
            arrivalCamera = arrival;
            seatedCamera = seated;
            doctorHwangActor = hwang;
            directorActor = director;
            obamaActor = obama;
            obamaStart = obamaEntrance;
            obamaCoffeeStop = obamaCoffee;
            coffeeProp = coffee;
            coffeeDropPoint = coffeeDrop;
            lookTargets = targets ?? Array.Empty<FirstContactMeetingLookTarget>();
            ResolveReferences();
        }

        private void OnDisable()
        {
            StopPresentation();
        }

        private void ResolveReferences()
        {
            cameraController = cameraController != null
                ? cameraController
                : FindFirstObjectByType<CameraController>(FindObjectsInactive.Include);
            seatedCamera = seatedCamera != null
                ? seatedCamera
                : cameraController?.DefaultViewCamera;
        }

        private void CaptureSeatedPose()
        {
            if (seatedCamera == null)
            {
                return;
            }

            _seatedPosition = seatedCamera.transform.position;
            _seatedRotation = seatedCamera.transform.rotation;
            _hasCapturedSeatedPose = true;
        }

        private void PrepareAuthoredStage()
        {
            if (doctorHwangActor != null)
            {
                doctorHwangActor.gameObject.SetActive(true);
            }

            if (directorActor != null)
            {
                directorActor.gameObject.SetActive(true);
            }

            if (obamaActor != null && obamaStart != null)
            {
                obamaActor.SetPositionAndRotation(
                    obamaStart.position,
                    obamaStart.rotation);
                obamaActor.gameObject.SetActive(false);
            }

            if (coffeeProp != null)
            {
                coffeeProp.gameObject.SetActive(false);
            }
        }

        private IEnumerator PlayObamaEntranceRoutine()
        {
            Transform doorTarget = ResolveLookTarget(MeetingLookTarget.Door);
            if (doorTarget != null)
            {
                yield return BlendSeatedLookRoutine(doorTarget, lookBlendSeconds);
            }

            if (_cancelRequested || obamaActor == null ||
                obamaStart == null || obamaCoffeeStop == null)
            {
                yield break;
            }

            obamaActor.SetPositionAndRotation(obamaStart.position, obamaStart.rotation);
            obamaActor.gameObject.SetActive(true);
            yield return MoveActorRoutine(
                obamaActor,
                obamaCoffeeStop,
                obamaEntranceSeconds);

            if (_cancelRequested)
            {
                yield break;
            }

            if (coffeeProp != null && coffeeDropPoint != null)
            {
                coffeeProp.SetPositionAndRotation(
                    coffeeDropPoint.position,
                    coffeeDropPoint.rotation);
                coffeeProp.gameObject.SetActive(true);
            }

            float elapsed = 0f;
            while (!_cancelRequested && elapsed < coffeeSettleSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private IEnumerator MoveActorRoutine(
            Transform actor,
            Transform destination,
            float seconds)
        {
            Vector3 startPosition = actor.position;
            Quaternion startRotation = actor.rotation;
            float duration = Mathf.Max(0.1f, seconds);
            float elapsed = 0f;
            while (!_cancelRequested && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / duration);
                float eased = progress * progress * (3f - 2f * progress);
                actor.SetPositionAndRotation(
                    Vector3.Lerp(startPosition, destination.position, eased),
                    Quaternion.Slerp(startRotation, destination.rotation, eased));
                yield return null;
            }

            if (!_cancelRequested)
            {
                actor.SetPositionAndRotation(destination.position, destination.rotation);
            }
        }

        private void CollectMeetingBeats(NarrativeScenarioAsset scenario)
        {
            _meetingBeats.Clear();
            if (scenario?.Beats == null)
            {
                return;
            }

            for (int i = 0; i < scenario.Beats.Count; i++)
            {
                NarrativeBeat beat = scenario.Beats[i];
                if (beat == null || !beat.enabled ||
                    !string.Equals(beat.sectionId, MeetingSectionId, StringComparison.Ordinal) ||
                    !string.Equals(beat.type, "dialogue", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                _meetingBeats.Add(beat);
            }

            _meetingBeats.Sort((left, right) => left.order.CompareTo(right.order));
        }

        private IEnumerator PlayBeatRoutine(string scenarioId, NarrativeBeat beat)
        {
            if (beat == null)
            {
                yield break;
            }

            MeetingLookTarget authoredTarget = beat.meetingLookTarget;
            if (authoredTarget == MeetingLookTarget.UseSpeakerDefault)
            {
                authoredTarget = ResolveSpeakerDefault(beat.speakerId);
            }

            Transform target = ResolveLookTarget(authoredTarget);
            if (target != null && authoredTarget != MeetingLookTarget.KeepCurrent)
            {
                yield return BlendSeatedLookRoutine(target, lookBlendSeconds);
            }

            if (_cancelRequested)
            {
                yield break;
            }

            NarrativeTrace.Emit(scenarioId, beat.id, "enter");
            _context?.Subtitles?.Show(beat.ResolveSpeaker(), beat.ResolveText());
            yield return WaitForLineRoutine(beat.minimumSeconds, beat.WaitForAdvance);
            _context?.Subtitles?.Hide();
            NarrativeTrace.Emit(scenarioId, beat.id, "exit");
        }

        private IEnumerator WaitForLineRoutine(float minimumSeconds, bool waitForAdvance)
        {
            yield return null;
            float elapsed = 0f;
            while (!_cancelRequested && elapsed < Mathf.Max(0f, minimumSeconds))
            {
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (_cancelRequested || !waitForAdvance)
            {
                yield break;
            }

            _context?.Subtitles?.SetAdvancePromptVisible(true);
            while (!_cancelRequested &&
                   !TerminalKeyboardInput.WasPressed(KeyCode.Space) &&
                   _context?.Subtitles?.ConsumeAdvanceRequest() != true)
            {
                yield return null;
            }

            _context?.Subtitles?.SetAdvancePromptVisible(false);
        }

        private IEnumerator BlendSeatedLookRoutine(Transform target, float seconds)
        {
            if (!_hasCapturedSeatedPose || seatedCamera == null || target == null)
            {
                yield break;
            }

            Vector3 direction = target.position - _seatedPosition;
            if (direction.sqrMagnitude < 0.0001f)
            {
                yield break;
            }

            Quaternion startRotation = seatedCamera.transform.rotation;
            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            float duration = Mathf.Max(0f, seconds);
            float elapsed = 0f;
            while (!_cancelRequested && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = duration <= 0.0001f
                    ? 1f
                    : Mathf.Clamp01(elapsed / duration);
                float eased = progress * progress * (3f - 2f * progress);
                seatedCamera.transform.SetPositionAndRotation(
                    _seatedPosition,
                    Quaternion.Slerp(startRotation, targetRotation, eased));
                yield return null;
            }

            if (!_cancelRequested)
            {
                seatedCamera.transform.SetPositionAndRotation(
                    _seatedPosition,
                    targetRotation);
            }
        }

        private IEnumerator RestoreSeatedViewRoutine(float seconds)
        {
            if (!_hasCapturedSeatedPose || seatedCamera == null)
            {
                yield break;
            }

            Quaternion startRotation = seatedCamera.transform.rotation;
            float duration = Mathf.Max(0f, seconds);
            float elapsed = 0f;
            while (!_cancelRequested && elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = duration <= 0.0001f
                    ? 1f
                    : Mathf.Clamp01(elapsed / duration);
                float eased = progress * progress * (3f - 2f * progress);
                seatedCamera.transform.SetPositionAndRotation(
                    _seatedPosition,
                    Quaternion.Slerp(startRotation, _seatedRotation, eased));
                yield return null;
            }

            RestoreSeatedViewImmediate();
        }

        private void RestoreSeatedViewImmediate()
        {
            if (arrivalCamera != null)
            {
                arrivalCamera.enabled = false;
            }

            if (!_hasCapturedSeatedPose || seatedCamera == null)
            {
                return;
            }

            seatedCamera.transform.SetPositionAndRotation(
                _seatedPosition,
                _seatedRotation);
            cameraController?.SetMode(CameraMode.Default);
        }

        private Transform ResolveLookTarget(MeetingLookTarget target)
        {
            if (target == MeetingLookTarget.KeepCurrent || lookTargets == null)
            {
                return null;
            }

            for (int i = 0; i < lookTargets.Length; i++)
            {
                FirstContactMeetingLookTarget candidate = lookTargets[i];
                if (candidate != null && candidate.Target == target)
                {
                    return candidate.transform;
                }
            }

            return null;
        }

        private static MeetingLookTarget ResolveSpeakerDefault(string speakerId)
        {
            if (string.Equals(speakerId, "obama", StringComparison.OrdinalIgnoreCase))
            {
                return MeetingLookTarget.Obama;
            }

            if (string.Equals(speakerId, "director", StringComparison.OrdinalIgnoreCase))
            {
                return MeetingLookTarget.Director;
            }

            if (string.Equals(speakerId, "doctor_hwang", StringComparison.OrdinalIgnoreCase))
            {
                return MeetingLookTarget.Hwang;
            }

            return MeetingLookTarget.KeepCurrent;
        }

        private bool Require(UnityEngine.Object reference, string fieldName, bool logErrors)
        {
            if (reference != null)
            {
                return true;
            }

            if (logErrors)
            {
                Debug.LogError(
                    $"[MeetingArrival] Missing required scene reference: {fieldName}.",
                    this);
            }

            return false;
        }
    }
}
