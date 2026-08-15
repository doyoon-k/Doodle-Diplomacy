using System;
using System.Collections;
using System.Collections.Generic;
using DoodleDiplomacy.Interaction;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    /// <summary>
    /// Owns the runtime continuity of the authored terminal and tablet while
    /// presentation sequences temporarily carry or reposition them.
    /// </summary>
    public sealed class FirstContactEquipmentContinuity
    {
        private sealed class InteractionSnapshot
        {
            public Collider[] Colliders = Array.Empty<Collider>();
            public bool[] ColliderEnabled = Array.Empty<bool>();
            public InteractableObject[] Interactables = Array.Empty<InteractableObject>();
            public bool[] InteractableActive = Array.Empty<bool>();
        }

        private readonly IReadOnlyList<Transform> _equipment;
        private readonly IReadOnlyList<Transform> _briefingPoses;
        private readonly IReadOnlyList<Transform> _meetingPoses;
        private readonly IReadOnlyList<Transform> _carrySockets;
        private readonly bool[] _carryActive;
        private readonly InteractionSnapshot[] _interactionSnapshots;

        public FirstContactEquipmentContinuity(
            IReadOnlyList<Transform> equipment,
            IReadOnlyList<Transform> briefingPoses,
            IReadOnlyList<Transform> meetingPoses,
            IReadOnlyList<Transform> carrySockets)
        {
            _equipment = equipment ?? Array.Empty<Transform>();
            _briefingPoses = briefingPoses ?? Array.Empty<Transform>();
            _meetingPoses = meetingPoses ?? Array.Empty<Transform>();
            _carrySockets = carrySockets ?? Array.Empty<Transform>();
            _carryActive = new bool[_equipment.Count];
            _interactionSnapshots = new InteractionSnapshot[_equipment.Count];
        }

        public bool HasCompleteConfiguration
        {
            get
            {
                if (_equipment.Count == 0 ||
                    _briefingPoses.Count != _equipment.Count ||
                    _meetingPoses.Count != _equipment.Count ||
                    _carrySockets.Count != _equipment.Count)
                {
                    return false;
                }

                for (int i = 0; i < _equipment.Count; i++)
                {
                    if (_equipment[i] == null ||
                        _briefingPoses[i] == null ||
                        _meetingPoses[i] == null ||
                        _carrySockets[i] == null)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        public void ResetToBriefingPlacement()
        {
            SetAllCarryActive(false);
            RestoreInteractionState();
            PlaceAt(_briefingPoses);
        }

        public void CommitMeetingPlacement()
        {
            SetAllCarryActive(false);
            RestoreInteractionState();
            PlaceAt(_meetingPoses);
        }

        public void PlaceAt(IReadOnlyList<Transform> poses)
        {
            if (poses == null)
            {
                return;
            }

            int count = Mathf.Min(_equipment.Count, poses.Count);
            for (int i = 0; i < count; i++)
            {
                Transform equipment = _equipment[i];
                Transform pose = poses[i];
                if (equipment != null && pose != null)
                {
                    equipment.SetPositionAndRotation(pose.position, pose.rotation);
                }
            }
        }

        public IEnumerator MoveToCarrySocketsRoutine(float seconds)
        {
            if (!HasCompleteConfiguration)
            {
                yield break;
            }

            CaptureAndDisableInteractionState();
            int count = _equipment.Count;
            var startPositions = new Vector3[count];
            var startRotations = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                startPositions[i] = _equipment[i].position;
                startRotations[i] = _equipment[i].rotation;
            }

            float duration = Mathf.Max(0f, seconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                for (int i = 0; i < count; i++)
                {
                    ApplyPoseProgress(
                        _equipment[i],
                        startPositions[i],
                        startRotations[i],
                        _carrySockets[i],
                        duration,
                        elapsed);
                }

                yield return null;
            }

            for (int i = 0; i < count; i++)
            {
                _carryActive[i] = true;
                _equipment[i].SetPositionAndRotation(
                    _carrySockets[i].position,
                    _carrySockets[i].rotation);
            }
        }

        public bool AttachToCarriersImmediate()
        {
            if (!HasCompleteConfiguration)
            {
                return false;
            }

            CaptureAndDisableInteractionState();
            for (int i = 0; i < _equipment.Count; i++)
            {
                _carryActive[i] = true;
                _equipment[i].SetPositionAndRotation(
                    _carrySockets[i].position,
                    _carrySockets[i].rotation);
            }

            return true;
        }

        public IEnumerator SetDownInMeetingRoutine(float seconds)
        {
            if (!IsAnyCarried())
            {
                yield break;
            }

            int count = Mathf.Min(_equipment.Count, _meetingPoses.Count);
            var startPositions = new Vector3[count];
            var startRotations = new Quaternion[count];
            for (int i = 0; i < count; i++)
            {
                startPositions[i] = _equipment[i].position;
                startRotations[i] = _equipment[i].rotation;
                _carryActive[i] = false;
            }

            float duration = Mathf.Max(0f, seconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                for (int i = 0; i < count; i++)
                {
                    ApplyPoseProgress(
                        _equipment[i],
                        startPositions[i],
                        startRotations[i],
                        _meetingPoses[i],
                        duration,
                        elapsed);
                }

                yield return null;
            }

            CommitMeetingPlacement();
        }

        public void FollowCarriers()
        {
            int count = Mathf.Min(_carryActive.Length, _carrySockets.Count);
            for (int i = 0; i < count; i++)
            {
                if (_carryActive[i] &&
                    _equipment[i] != null &&
                    _carrySockets[i] != null)
                {
                    _equipment[i].SetPositionAndRotation(
                        _carrySockets[i].position,
                        _carrySockets[i].rotation);
                }
            }
        }

        private bool IsAnyCarried()
        {
            for (int i = 0; i < _carryActive.Length; i++)
            {
                if (_carryActive[i])
                {
                    return true;
                }
            }

            return false;
        }

        private void SetAllCarryActive(bool active)
        {
            for (int i = 0; i < _carryActive.Length; i++)
            {
                _carryActive[i] = active;
            }
        }

        private void CaptureAndDisableInteractionState()
        {
            for (int i = 0; i < _equipment.Count; i++)
            {
                Transform equipment = _equipment[i];
                if (equipment == null)
                {
                    continue;
                }

                InteractionSnapshot snapshot = _interactionSnapshots[i];
                if (snapshot == null)
                {
                    snapshot = new InteractionSnapshot
                    {
                        Colliders = equipment.GetComponentsInChildren<Collider>(true),
                        Interactables = equipment.GetComponentsInChildren<InteractableObject>(true)
                    };
                    snapshot.ColliderEnabled = new bool[snapshot.Colliders.Length];
                    for (int colliderIndex = 0;
                         colliderIndex < snapshot.Colliders.Length;
                         colliderIndex++)
                    {
                        snapshot.ColliderEnabled[colliderIndex] =
                            snapshot.Colliders[colliderIndex] != null &&
                            snapshot.Colliders[colliderIndex].enabled;
                    }

                    snapshot.InteractableActive = new bool[snapshot.Interactables.Length];
                    for (int interactableIndex = 0;
                         interactableIndex < snapshot.Interactables.Length;
                         interactableIndex++)
                    {
                        snapshot.InteractableActive[interactableIndex] =
                            snapshot.Interactables[interactableIndex] != null &&
                            snapshot.Interactables[interactableIndex].isActive;
                    }

                    _interactionSnapshots[i] = snapshot;
                }

                for (int colliderIndex = 0;
                     colliderIndex < snapshot.Colliders.Length;
                     colliderIndex++)
                {
                    if (snapshot.Colliders[colliderIndex] != null)
                    {
                        snapshot.Colliders[colliderIndex].enabled = false;
                    }
                }

                for (int interactableIndex = 0;
                     interactableIndex < snapshot.Interactables.Length;
                     interactableIndex++)
                {
                    snapshot.Interactables[interactableIndex]?.SetInteractable(false);
                }
            }
        }

        private void RestoreInteractionState()
        {
            for (int i = 0; i < _interactionSnapshots.Length; i++)
            {
                InteractionSnapshot snapshot = _interactionSnapshots[i];
                if (snapshot == null)
                {
                    continue;
                }

                for (int colliderIndex = 0;
                     colliderIndex < snapshot.Colliders.Length;
                     colliderIndex++)
                {
                    if (snapshot.Colliders[colliderIndex] != null)
                    {
                        snapshot.Colliders[colliderIndex].enabled =
                            snapshot.ColliderEnabled[colliderIndex];
                    }
                }

                for (int interactableIndex = 0;
                     interactableIndex < snapshot.Interactables.Length;
                     interactableIndex++)
                {
                    if (snapshot.Interactables[interactableIndex] != null)
                    {
                        snapshot.Interactables[interactableIndex].SetInteractable(
                            snapshot.InteractableActive[interactableIndex]);
                    }
                }

                _interactionSnapshots[i] = null;
            }
        }

        private static void ApplyPoseProgress(
            Transform subject,
            Vector3 startPosition,
            Quaternion startRotation,
            Transform destination,
            float seconds,
            float elapsed)
        {
            float progress = seconds <= 0.0001f
                ? 1f
                : Mathf.Clamp01(elapsed / seconds);
            float eased = progress * progress * (3f - 2f * progress);
            subject.SetPositionAndRotation(
                Vector3.Lerp(startPosition, destination.position, eased),
                Quaternion.Slerp(startRotation, destination.rotation, eased));
        }
    }
}
