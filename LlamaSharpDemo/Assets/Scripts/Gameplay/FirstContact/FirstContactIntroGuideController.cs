using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class FirstContactIntroGuideController : MonoBehaviour
    {
        [SerializeField] private Transform[] pathPoints = Array.Empty<Transform>();
        [SerializeField, HideInInspector] private int[] manualHoldPointIndices = Array.Empty<int>();
        [SerializeField, Min(0.1f)] private float moveSpeed = 2.25f;
        [SerializeField, Min(0.5f)] private float playerLeashDistance = 5.5f;
        [SerializeField, Min(0f)] private float pointPauseSeconds;
        [Header("NavMesh Movement")]
        [SerializeField, Min(0.02f)] private float waypointArrivalDistance = 0.2f;
        [SerializeField, Min(0.1f)] private float navMeshSampleDistance = 2f;
        [SerializeField, Min(0.1f)] private float acceleration = 12f;
        [SerializeField, Min(1f)] private float angularSpeed = 720f;
        [Header("Visual Forward Correction")]
        [Tooltip("The imported suit model faces local -X, while route movement uses Unity local +Z as forward.")]
        [SerializeField] private Transform visualForwardRoot;
        [SerializeField] private float visualYawCorrectionDegrees = 90f;

        private readonly HashSet<int> _manualHoldPoints = new();
        private readonly HashSet<Transform> _sequenceHoldTransforms = new();
        private Transform _player;
        private Quaternion _visualOriginalLocalRotation;
        private int _currentPointIndex;
        private float _pauseRemaining;
        private bool _moving;
        private bool _waitingForRelease;
        private Transform _waitingAtPoint;
        private bool _visualRotationCaptured;
        private bool _visualCorrectionApplied;
        private NavMeshAgent _agent;
        private bool _destinationAssigned;
        private bool _navigationFailureReported;

        public event Action<int> ReachedManualHoldPoint;
        public event Action<FirstContactIntroGuidePoint> ReachedNamedHoldPoint;
        public event Action ReachedDestination;

        public bool IsMoving => _moving;
        public bool IsWaitingForRelease => _waitingForRelease;
        public int CurrentPointIndex => _currentPointIndex;
        public IReadOnlyList<Transform> PathPoints => pathPoints;

        public bool IsWaitingAt(FirstContactIntroGuidePoint point)
        {
            return _waitingForRelease &&
                   point != null &&
                   _waitingAtPoint == point.transform;
        }

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
            pointPauseSeconds = 0f;
            RebuildHoldPointSet();
            _sequenceHoldTransforms.Clear();
        }

        public void CopyConfigurationFrom(FirstContactIntroGuideController source)
        {
            if (source == null || source == this)
            {
                return;
            }

            pathPoints = source.pathPoints != null
                ? (Transform[])source.pathPoints.Clone()
                : Array.Empty<Transform>();
            manualHoldPointIndices = source.manualHoldPointIndices != null
                ? (int[])source.manualHoldPointIndices.Clone()
                : Array.Empty<int>();
            moveSpeed = source.moveSpeed;
            playerLeashDistance = source.playerLeashDistance;
            pointPauseSeconds = source.pointPauseSeconds;
            waypointArrivalDistance = source.waypointArrivalDistance;
            navMeshSampleDistance = source.navMeshSampleDistance;
            acceleration = source.acceleration;
            angularSpeed = source.angularSpeed;
            visualYawCorrectionDegrees = source.visualYawCorrectionDegrees;
            RebuildHoldPointSet();
            _sequenceHoldTransforms.Clear();
        }

        private void Awake()
        {
            RebuildHoldPointSet();
            EnsureAgent();
            DisableAgent();
        }

        private void OnDisable()
        {
            DisableAgent();
            RestoreVisualForwardRotation();
        }

        private void Update()
        {
            if (!_moving || _waitingForRelease || pathPoints == null || _currentPointIndex >= pathPoints.Length)
            {
                return;
            }

            if (!Application.isPlaying)
            {
                UpdateDirectlyForEditorTests();
                return;
            }

            if (!IsAgentReady())
            {
                ReportNavigationFailure(
                    "The guide is active but is not placed on a baked NavMesh.");
                _moving = false;
                return;
            }

            if (_pauseRemaining > 0f)
            {
                _pauseRemaining -= Time.deltaTime;
                SetAgentPaused(true);
                if (_pauseRemaining <= 0f)
                {
                    AssignCurrentDestination();
                }

                return;
            }

            Transform targetPoint = pathPoints[_currentPointIndex];
            if (targetPoint == null)
            {
                AdvancePoint();
                AssignCurrentDestination();
                return;
            }

            if (_player != null && HorizontalDistance(transform.position, _player.position) > playerLeashDistance)
            {
                SetAgentPaused(true);
                return;
            }

            if (!_destinationAssigned)
            {
                AssignCurrentDestination();
            }

            SetAgentPaused(false);
            if (_agent.pathPending)
            {
                return;
            }

            if (_agent.pathStatus != NavMeshPathStatus.PathComplete)
            {
                ReportNavigationFailure(
                    $"No complete NavMesh path exists to route point '{targetPoint.name}' " +
                    $"(status: {_agent.pathStatus}).");
                _moving = false;
                SetAgentPaused(true);
                return;
            }

            if (!_agent.hasPath || float.IsInfinity(_agent.remainingDistance))
            {
                // A route can begin by warping the guide onto its first point.
                // NavMeshAgent reports no path when the destination is already
                // under the agent, so treat that state as an arrival instead of
                // waiting forever for a path that will never be created.
                if (Vector3.Distance(transform.position, targetPoint.position) <=
                    waypointArrivalDistance)
                {
                    AdvancePoint();
                    AssignCurrentDestination();
                }

                return;
            }

            if (_agent.remainingDistance <= waypointArrivalDistance)
            {
                AdvancePoint();
                AssignCurrentDestination();
            }
        }

        public void Begin(Transform player, bool warpToStart = true)
        {
            ApplyVisualForwardCorrection();
            _player = player;
            _currentPointIndex = 0;
            _pauseRemaining = 0f;
            _waitingForRelease = false;
            _waitingAtPoint = null;
            _moving = pathPoints != null && pathPoints.Length > 0;
            _destinationAssigned = false;
            _navigationFailureReported = false;

            if (!_moving)
            {
                DisableAgent();
                return;
            }

            if (!Application.isPlaying)
            {
                if (warpToStart && pathPoints[0] != null)
                {
                    transform.position = pathPoints[0].position;
                }

                return;
            }

            Vector3 requestedStart = warpToStart && pathPoints[0] != null
                ? pathPoints[0].position
                : transform.position;
            if (!ActivateAgent(requestedStart))
            {
                _moving = false;
                return;
            }

            AssignCurrentDestination();
        }

        public void Resume()
        {
            _waitingForRelease = false;
            _waitingAtPoint = null;
            _pauseRemaining = pointPauseSeconds;
            AssignCurrentDestination();
        }

        public void AddManualHoldPoint(int pointIndex)
        {
            if (pointIndex >= 0)
            {
                _manualHoldPoints.Add(pointIndex);
            }
        }

        /// <summary>
        /// Adds a temporary narrative catch-up gate. Unlike Pause On Arrival,
        /// the owning sequence decides exactly when to release this hold.
        /// </summary>
        public void AddSequenceHoldPoint(FirstContactIntroGuidePoint point)
        {
            if (point != null)
            {
                _sequenceHoldTransforms.Add(point.transform);
            }
        }

        public void Stop()
        {
            _moving = false;
            _waitingForRelease = false;
            _waitingAtPoint = null;
            _player = null;
            _destinationAssigned = false;
            DisableAgent();
            RestoreVisualForwardRotation();
        }

        public void ApplyVisualForwardCorrection()
        {
            if (Mathf.Approximately(visualYawCorrectionDegrees, 0f))
            {
                return;
            }

            if (visualForwardRoot == null)
            {
                Transform[] descendants = GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < descendants.Length; i++)
                {
                    if (descendants[i] != transform &&
                        string.Equals(
                            descendants[i].name,
                            "AdjutantVisual",
                            StringComparison.Ordinal))
                    {
                        visualForwardRoot = descendants[i];
                        break;
                    }
                }
            }

            if (visualForwardRoot == null)
            {
                return;
            }

            if (!_visualRotationCaptured)
            {
                _visualOriginalLocalRotation = visualForwardRoot.localRotation;
                _visualRotationCaptured = true;
            }

            visualForwardRoot.localRotation =
                _visualOriginalLocalRotation *
                Quaternion.Euler(0f, visualYawCorrectionDegrees, 0f);
            _visualCorrectionApplied = true;
        }

        private void RestoreVisualForwardRotation()
        {
            if (!_visualCorrectionApplied ||
                !_visualRotationCaptured ||
                visualForwardRoot == null)
            {
                return;
            }

            visualForwardRoot.localRotation = _visualOriginalLocalRotation;
            _visualCorrectionApplied = false;
        }

        private void AdvancePoint()
        {
            int reachedIndex = _currentPointIndex;
            Transform reachedTransform = pathPoints != null &&
                                         reachedIndex >= 0 &&
                                         reachedIndex < pathPoints.Length
                ? pathPoints[reachedIndex]
                : null;
            FirstContactIntroGuidePoint reachedPoint = reachedTransform != null
                ? reachedTransform.GetComponent<FirstContactIntroGuidePoint>()
                : null;
            _currentPointIndex++;
            bool shouldHold = _manualHoldPoints.Contains(reachedIndex) ||
                              (reachedTransform != null &&
                               _sequenceHoldTransforms.Contains(reachedTransform)) ||
                              (reachedPoint != null && reachedPoint.PauseOnArrival);
            if (shouldHold)
            {
                _waitingForRelease = true;
                _waitingAtPoint = reachedTransform;
                _destinationAssigned = false;
                SetAgentPaused(true);
                ReachedManualHoldPoint?.Invoke(reachedIndex);
                if (reachedPoint != null)
                {
                    ReachedNamedHoldPoint?.Invoke(reachedPoint);
                }
                return;
            }

            if (_currentPointIndex >= pathPoints.Length)
            {
                _moving = false;
                _destinationAssigned = false;
                SetAgentPaused(true);
                ReachedDestination?.Invoke();
                return;
            }

            _pauseRemaining = pointPauseSeconds;
            _destinationAssigned = false;
        }

        private void EnsureAgent()
        {
            _agent = _agent != null ? _agent : GetComponent<NavMeshAgent>();
        }

        private bool ActivateAgent(Vector3 requestedPosition)
        {
            EnsureAgent();
            if (_agent == null)
            {
                ReportNavigationFailure("The guide is missing its required NavMeshAgent.");
                return false;
            }

            int areaMask = _agent.areaMask;
            if (!NavMesh.SamplePosition(
                    requestedPosition,
                    out NavMeshHit hit,
                    navMeshSampleDistance,
                    areaMask))
            {
                ReportNavigationFailure(
                    $"No baked NavMesh was found within {navMeshSampleDistance:0.##}m of the guide start.");
                return false;
            }

            if (_agent.enabled)
            {
                _agent.enabled = false;
            }

            transform.position = hit.position;
            _agent.speed = moveSpeed;
            _agent.acceleration = acceleration;
            _agent.angularSpeed = angularSpeed;
            _agent.stoppingDistance = 0f;
            _agent.autoBraking = false;
            _agent.updatePosition = true;
            _agent.updateRotation = true;
            _agent.enabled = true;
            return _agent.isOnNavMesh;
        }

        private bool IsAgentReady()
        {
            EnsureAgent();
            return _agent != null && _agent.enabled && _agent.isOnNavMesh;
        }

        private void AssignCurrentDestination()
        {
            if (!_moving || _waitingForRelease || _pauseRemaining > 0f ||
                pathPoints == null || _currentPointIndex >= pathPoints.Length ||
                !Application.isPlaying)
            {
                return;
            }

            Transform targetPoint = pathPoints[_currentPointIndex];
            if (targetPoint == null)
            {
                return;
            }

            if (!IsAgentReady())
            {
                return;
            }

            if (!NavMesh.SamplePosition(
                    targetPoint.position,
                    out NavMeshHit hit,
                    navMeshSampleDistance,
                    _agent.areaMask))
            {
                ReportNavigationFailure(
                    $"Route point '{targetPoint.name}' is not within {navMeshSampleDistance:0.##}m of the baked NavMesh.");
                _moving = false;
                SetAgentPaused(true);
                return;
            }

            _agent.autoBraking = IsHoldPoint(_currentPointIndex) ||
                                 _currentPointIndex == pathPoints.Length - 1;
            _agent.isStopped = false;
            _destinationAssigned = _agent.SetDestination(hit.position);
            if (!_destinationAssigned)
            {
                ReportNavigationFailure(
                    $"NavMeshAgent rejected route point '{targetPoint.name}'.");
                _moving = false;
            }
        }

        private bool IsHoldPoint(int pointIndex)
        {
            if (pathPoints == null || pointIndex < 0 || pointIndex >= pathPoints.Length)
            {
                return false;
            }

            Transform pointTransform = pathPoints[pointIndex];
            FirstContactIntroGuidePoint point = pointTransform != null
                ? pointTransform.GetComponent<FirstContactIntroGuidePoint>()
                : null;
            return _manualHoldPoints.Contains(pointIndex) ||
                   (pointTransform != null && _sequenceHoldTransforms.Contains(pointTransform)) ||
                   (point != null && point.PauseOnArrival);
        }

        private void SetAgentPaused(bool paused)
        {
            if (IsAgentReady())
            {
                _agent.isStopped = paused;
            }
        }

        private void DisableAgent()
        {
            EnsureAgent();
            if (_agent == null || !_agent.enabled)
            {
                return;
            }

            if (_agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }

            _agent.enabled = false;
            _destinationAssigned = false;
        }

        private void ReportNavigationFailure(string message)
        {
            if (_navigationFailureReported)
            {
                return;
            }

            _navigationFailureReported = true;
            Debug.LogError($"[FirstContactIntroGuide] {message}", this);
        }

        private void UpdateDirectlyForEditorTests()
        {
            Transform targetPoint = pathPoints[_currentPointIndex];
            if (targetPoint == null)
            {
                AdvancePoint();
                return;
            }

            Vector3 targetPosition = targetPoint.position;
            if ((targetPosition - transform.position).sqrMagnitude <= 0.04f)
            {
                transform.position = targetPosition;
                AdvancePoint();
            }
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
