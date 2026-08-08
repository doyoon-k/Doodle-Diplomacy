using System;
using System.Collections;
using DoodleDiplomacy.Narrative;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactBriefingPresentation : MonoBehaviour
    {
        [SerializeField] private FirstContactBriefingProjector projector;
        [SerializeField] private Transform seatedViewPreview;
        [SerializeField] private Transform projectorLookTarget;
        [SerializeField] private Transform projectorCloseupCameraAnchor;
        [SerializeField] private Transform hwangPresentationLookTarget;
        [SerializeField] private Transform hwangQaLookTarget;
        [SerializeField] private Transform directorLookTarget;
        [SerializeField, Min(0f)] private float lookBlendSeconds = 0.55f;

        private Transform _activeLookTarget;
        private bool _usingProjectorCloseup;

        public Transform SeatedViewPreview => seatedViewPreview;
        public Transform DirectorLookTarget => directorLookTarget;

        public void SetDirectorLookTarget(Transform target)
        {
            if (target == null || directorLookTarget == target)
            {
                return;
            }

            if (_activeLookTarget == directorLookTarget)
            {
                _activeLookTarget = null;
            }

            directorLookTarget = target;
        }

        public void BeginPresentation()
        {
            _activeLookTarget = null;
            _usingProjectorCloseup = false;
            projector?.PowerOff();
        }

        public void EndPresentation()
        {
            _activeLookTarget = null;
            _usingProjectorCloseup = false;
            projector?.PowerOff();
        }

        public bool HandlesCue(string runtimeCue)
        {
            return FirstContactBriefingProjector.TryResolveSlide(runtimeCue, out _) ||
                   string.Equals(runtimeCue, "BriefingWide", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(runtimeCue, "BriefingProjectorSimple", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(runtimeCue, "BriefingLookDirector", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(runtimeCue, "BriefingLookProjector", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(runtimeCue, "BriefingLookHwangPresentation", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(runtimeCue, "BriefingLookHwangQA", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(runtimeCue, "BriefingQAndAStart", StringComparison.OrdinalIgnoreCase);
        }

        public bool HandlesAuthoredLookTarget(BriefingLookTarget authoredLookTarget)
        {
            return authoredLookTarget == BriefingLookTarget.KeepCurrent ||
                   TryResolveAuthoredLookTarget(authoredLookTarget, out _);
        }

        public IEnumerator PrepareCue(
            string runtimeCue,
            FirstContactIntroPlayerController player,
            BriefingLookTarget authoredLookTarget = BriefingLookTarget.UseRuntimeCue)
        {
            bool keepCurrentLookTarget =
                authoredLookTarget == BriefingLookTarget.KeepCurrent;
            bool hasAuthoredLookTarget = TryResolveAuthoredLookTarget(
                authoredLookTarget,
                out Transform authoredTarget);
            bool useProjectorCloseup =
                authoredLookTarget == BriefingLookTarget.Projector;

            if (FirstContactBriefingProjector.TryResolveSlide(
                    runtimeCue,
                    out FirstContactBriefingSlideId slideId))
            {
                projector?.ShowSlide(slideId);
                if (keepCurrentLookTarget)
                {
                    yield break;
                }

                if (useProjectorCloseup && projectorCloseupCameraAnchor != null)
                {
                    yield return BlendToProjectorCloseup(player);
                    yield break;
                }

                yield return RestoreFromProjectorCloseup(player);
                yield return BlendToTarget(
                    player,
                    hasAuthoredLookTarget
                        ? authoredTarget
                        : projectorLookTarget);
                yield break;
            }

            if (keepCurrentLookTarget)
            {
                yield break;
            }

            if (useProjectorCloseup && projectorCloseupCameraAnchor != null)
            {
                yield return BlendToProjectorCloseup(player);
                yield break;
            }

            yield return RestoreFromProjectorCloseup(player);

            Transform target = hasAuthoredLookTarget ? authoredTarget : null;
            if (!hasAuthoredLookTarget &&
                (string.Equals(runtimeCue, "BriefingWide", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(runtimeCue, "BriefingLookHwangPresentation", StringComparison.OrdinalIgnoreCase)))
            {
                target = hwangPresentationLookTarget;
            }
            else if (!hasAuthoredLookTarget &&
                     (string.Equals(runtimeCue, "BriefingProjectorSimple", StringComparison.OrdinalIgnoreCase) ||
                      string.Equals(runtimeCue, "BriefingLookProjector", StringComparison.OrdinalIgnoreCase)))
            {
                target = projectorLookTarget;
            }
            else if (!hasAuthoredLookTarget &&
                     string.Equals(runtimeCue, "BriefingLookDirector", StringComparison.OrdinalIgnoreCase))
            {
                target = directorLookTarget;
            }
            else if (!hasAuthoredLookTarget &&
                     string.Equals(runtimeCue, "BriefingLookHwangQA", StringComparison.OrdinalIgnoreCase))
            {
                target = hwangQaLookTarget;
            }
            else if (string.Equals(runtimeCue, "BriefingQAndAStart", StringComparison.OrdinalIgnoreCase))
            {
                projector?.PowerOff();
                if (!hasAuthoredLookTarget)
                {
                    target = hwangQaLookTarget;
                }
            }

            yield return BlendToTarget(player, target);
        }

        private IEnumerator BlendToProjectorCloseup(
            FirstContactIntroPlayerController player)
        {
            if (player == null || projectorCloseupCameraAnchor == null ||
                _usingProjectorCloseup)
            {
                yield break;
            }

            yield return player.BlendViewToAnchor(
                projectorCloseupCameraAnchor,
                lookBlendSeconds);
            _activeLookTarget = projectorLookTarget;
            _usingProjectorCloseup = true;
        }

        private IEnumerator RestoreFromProjectorCloseup(
            FirstContactIntroPlayerController player)
        {
            if (!_usingProjectorCloseup)
            {
                yield break;
            }

            if (player != null)
            {
                yield return player.BlendToRestoredView(lookBlendSeconds);
            }

            _activeLookTarget = null;
            _usingProjectorCloseup = false;
        }

        private bool TryResolveAuthoredLookTarget(
            BriefingLookTarget authoredLookTarget,
            out Transform target)
        {
            target = null;
            switch (authoredLookTarget)
            {
                case BriefingLookTarget.Director:
                    target = directorLookTarget;
                    return target != null;
                case BriefingLookTarget.HwangPresentation:
                    target = hwangPresentationLookTarget;
                    return target != null;
                case BriefingLookTarget.HwangQa:
                    target = hwangQaLookTarget;
                    return target != null;
                case BriefingLookTarget.Projector:
                    target = projectorLookTarget;
                    return target != null;
                default:
                    return false;
            }
        }

        private IEnumerator BlendToTarget(
            FirstContactIntroPlayerController player,
            Transform target)
        {
            if (player == null || target == null || target == _activeLookTarget)
            {
                yield break;
            }

            yield return player.BlendViewToLookAt(target, lookBlendSeconds);
            _activeLookTarget = target;
        }
    }
}
