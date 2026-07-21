using System;
using System.Collections.Generic;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    public sealed class FirstContactIntroGuideController : MonoBehaviour
    {
        [SerializeField] private Transform[] pathPoints = Array.Empty<Transform>();
        [SerializeField] private int[] manualHoldPointIndices = Array.Empty<int>();
        [SerializeField, Min(0.1f)] private float moveSpeed = 2.25f;
        [SerializeField, Min(0.5f)] private float playerLeashDistance = 5.5f;
        [SerializeField, Min(0f)] private float pointPauseSeconds = 0.25f;
        [SerializeField, Min(1f)] private float turnSpeed = 10f;

        private readonly HashSet<int> _manualHoldPoints = new();
        private Transform _player;
        private int _currentPointIndex;
        private float _pauseRemaining;
        private bool _moving;
        private bool _waitingForRelease;

        public event Action<int> ReachedManualHoldPoint;
        public event Action ReachedDestination;

        public bool IsMoving => _moving;
        public bool IsWaitingForRelease => _waitingForRelease;
        public int CurrentPointIndex => _currentPointIndex;

        public void Configure(
            Transform[] points,
            int[] holdPointIndices,
            float speed = 2.25f,
            float leashDistance = 5.5f)
        {
            pathPoints = points ?? Array.Empty<Transform>();
            manualHoldPointIndices = holdPointIndices ?? Array.Empty<int>();
            moveSpeed = speed;
            playerLeashDistance = leashDistance;
            RebuildHoldPointSet();
        }

        private void Awake()
        {
            RebuildHoldPointSet();
        }

        private void Update()
        {
            if (!_moving || _waitingForRelease || pathPoints == null || _currentPointIndex >= pathPoints.Length)
            {
                return;
            }

            if (_pauseRemaining > 0f)
            {
                _pauseRemaining -= Time.deltaTime;
                return;
            }

            Transform targetPoint = pathPoints[_currentPointIndex];
            if (targetPoint == null)
            {
                AdvancePoint();
                return;
            }

            if (_player != null && HorizontalDistance(transform.position, _player.position) > playerLeashDistance)
            {
                return;
            }

            Vector3 targetPosition = targetPoint.position;
            targetPosition.y = transform.position.y;
            Vector3 toTarget = targetPosition - transform.position;
            if (toTarget.sqrMagnitude <= 0.04f)
            {
                transform.position = targetPosition;
                AdvancePoint();
                return;
            }

            Vector3 direction = toTarget.normalized;
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetPosition,
                moveSpeed * Time.deltaTime);
            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                turnSpeed * Time.deltaTime);
        }

        public void Begin(Transform player, bool warpToStart = true)
        {
            _player = player;
            _currentPointIndex = 0;
            _pauseRemaining = 0f;
            _waitingForRelease = false;
            _moving = pathPoints != null && pathPoints.Length > 0;

            if (_moving && warpToStart && pathPoints[0] != null)
            {
                Vector3 start = pathPoints[0].position;
                start.y = transform.position.y;
                transform.position = start;
            }
        }

        public void Resume()
        {
            _waitingForRelease = false;
            _pauseRemaining = pointPauseSeconds;
        }

        public void Stop()
        {
            _moving = false;
            _waitingForRelease = false;
            _player = null;
        }

        private void AdvancePoint()
        {
            int reachedIndex = _currentPointIndex;
            _currentPointIndex++;
            if (_manualHoldPoints.Contains(reachedIndex))
            {
                _waitingForRelease = true;
                ReachedManualHoldPoint?.Invoke(reachedIndex);
                return;
            }

            if (_currentPointIndex >= pathPoints.Length)
            {
                _moving = false;
                ReachedDestination?.Invoke();
                return;
            }

            _pauseRemaining = pointPauseSeconds;
        }

        private void RebuildHoldPointSet()
        {
            _manualHoldPoints.Clear();
            if (manualHoldPointIndices == null)
            {
                return;
            }

            foreach (int index in manualHoldPointIndices)
            {
                if (index >= 0)
                {
                    _manualHoldPoints.Add(index);
                }
            }
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
