using System.Collections;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public enum FirstContactVehicleDriveState
    {
        Inactive,
        Cruising,
        LeadingToTurn,
        TurningRight,
        Approaching,
        WaitingToBrake,
        Braking,
        Stopped
    }

    /// <summary>
    /// Moves the authored car and its occupants along scene-authored route anchors.
    /// The road, trees, lights and parking area are normal saved scene objects;
    /// this component never creates environment geometry at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FirstContactVehicleRouteController : MonoBehaviour
    {
        [Header("Authored Scene Route")]
        [SerializeField] private Transform routeEnvironment;
        [SerializeField] private Transform vehicleMotionRoot;
        [SerializeField] private Transform cruiseStartAnchor;
        [SerializeField] private Transform turnEntryAnchor;
        [SerializeField] private Transform turnExitAnchor;
        [SerializeField] private Transform parkingStopAnchor;
        [SerializeField] private Transform signLookTarget;

        [Header("Route Timing")]
        [SerializeField, Min(0.1f)] private float cruiseSpeed = 4.5f;
        [SerializeField, Min(0.1f)] private float turnSeconds = 4.2f;
        [SerializeField, Min(0.1f)] private float approachSpeed = 2.5f;
        [SerializeField, Min(0.5f)] private float brakingDistance = 3f;
        [SerializeField, Min(0.1f)] private float brakingSeconds = 1.65f;
        [SerializeField, Min(0.1f)] private float turnLeadDistance = 2f;

        [Header("Motion Feedback")]
        [SerializeField, Min(0f)] private float vehicleBobHeight = 0.018f;
        [SerializeField, Min(0f)] private float vehicleBobFrequency = 6.2f;
        [SerializeField, Min(0f)] private float vehicleRollDegrees = 0.22f;

        private Transform _playerReference;
        private Transform _vehicleRoot;
        private Transform _playerAuthoredParent;
        private int _playerAuthoredSiblingIndex;
        private Vector3 _playerAuthoredLocalPosition;
        private Quaternion _playerAuthoredLocalRotation;
        private Vector3 _authoredVehicleStartPosition;
        private Quaternion _authoredVehicleStartRotation;
        private bool _authoredPoseCaptured;
        private Vector3 _routeLocalPosition;
        private Vector3 _brakeStartLocalPosition;
        private float _routeYaw;
        private float _plannedCruiseSpeed;
        private float _stateElapsed;
        private bool _signVisible;
        private bool _brakingRequested;
        private bool _cruisePaused;
        private FirstContactVehicleDriveState _state = FirstContactVehicleDriveState.Inactive;

        public FirstContactVehicleDriveState State => _state;
        public Transform SignLookTarget => signLookTarget;
        public bool IsArrivalStarted =>
            _state == FirstContactVehicleDriveState.LeadingToTurn ||
            _state == FirstContactVehicleDriveState.TurningRight ||
            _state == FirstContactVehicleDriveState.Approaching ||
            _state == FirstContactVehicleDriveState.WaitingToBrake ||
            _state == FirstContactVehicleDriveState.Braking ||
            _state == FirstContactVehicleDriveState.Stopped;
        public bool IsSignVisible => _signVisible;
        public bool IsStopped => _state == FirstContactVehicleDriveState.Stopped;

        public void Configure(Transform playerReference)
        {
            _playerReference = playerReference;
        }

        public void ConfigureSceneRoute(
            Transform environment,
            Transform motionRoot,
            Transform cruiseStart,
            Transform turnEntry,
            Transform turnExit,
            Transform parkingStop,
            Transform signTarget)
        {
            routeEnvironment = environment;
            vehicleMotionRoot = motionRoot;
            cruiseStartAnchor = cruiseStart;
            turnEntryAnchor = turnEntry;
            turnExitAnchor = turnExit;
            parkingStopAnchor = parkingStop;
            signLookTarget = signTarget;
        }

        public void PlanCruiseDuration(float secondsUntilTurn)
        {
            float seconds = Mathf.Max(0f, secondsUntilTurn);
            float availableDistance = GetAuthoredStraightDistance();
            float requiredSpeed = seconds > 0.01f
                ? availableDistance / seconds
                : cruiseSpeed;
            _plannedCruiseSpeed = Mathf.Min(cruiseSpeed, requiredSpeed);
            if (requiredSpeed > cruiseSpeed + 0.1f)
            {
                Debug.LogWarning(
                    "[FirstContactVehicleRoute] The authored CruiseStart is too far away for the current " +
                    "dialogue timing at the configured cruise speed. Move VehicleMotionRoot and CruiseStart " +
                    "closer together in the scene; the vehicle will not fast-forward at runtime.",
                    this);
            }
        }

        public void SetCruisePaused(bool paused)
        {
            _cruisePaused = paused;
        }

        private void Update()
        {
            if (_state == FirstContactVehicleDriveState.Inactive || _vehicleRoot == null)
            {
                return;
            }

            float deltaTime = Time.unscaledDeltaTime;
            if (_state == FirstContactVehicleDriveState.Cruising && _cruisePaused)
            {
                ApplyVehiclePose(true);
                return;
            }

            switch (_state)
            {
                case FirstContactVehicleDriveState.Cruising:
                    UpdateCruise(deltaTime);
                    break;

                case FirstContactVehicleDriveState.LeadingToTurn:
                    UpdateTurnLead(deltaTime);
                    break;

                case FirstContactVehicleDriveState.TurningRight:
                    UpdateTurn(deltaTime);
                    break;

                case FirstContactVehicleDriveState.Approaching:
                    UpdateApproach(deltaTime);
                    break;

                case FirstContactVehicleDriveState.Braking:
                    UpdateBraking(deltaTime);
                    break;
            }

            ApplyVehiclePose(_state != FirstContactVehicleDriveState.Stopped);
        }

        private void OnDisable()
        {
            StopAndRestore();
        }

        public void BeginCruise()
        {
            if (_playerReference == null)
            {
                Debug.LogWarning(
                    "[FirstContactVehicleRoute] Player reference is missing.",
                    this);
                return;
            }

            if (!EnsureVehicleRig())
            {
                return;
            }

            _routeLocalPosition = GetRouteLocalPosition(vehicleMotionRoot);
            Vector3 authoredForward = routeEnvironment.InverseTransformDirection(
                vehicleMotionRoot.forward);
            _routeYaw = GetYaw(authoredForward);
            _cruisePaused = false;
            _stateElapsed = 0f;
            _signVisible = false;
            _brakingRequested = false;
            _state = FirstContactVehicleDriveState.Cruising;
            ApplyVehiclePose(true);
        }

        public void BeginArrival()
        {
            if (_state == FirstContactVehicleDriveState.Inactive)
            {
                BeginCruise();
            }

            if (_vehicleRoot == null || IsArrivalStarted)
            {
                return;
            }

            _cruisePaused = false;
            _state = FirstContactVehicleDriveState.LeadingToTurn;
        }

        public void BeginBraking()
        {
            if (_state == FirstContactVehicleDriveState.Inactive ||
                _state == FirstContactVehicleDriveState.Braking ||
                _state == FirstContactVehicleDriveState.Stopped)
            {
                return;
            }

            _brakingRequested = true;
            if (_state == FirstContactVehicleDriveState.WaitingToBrake)
            {
                StartBraking();
            }
        }

        public IEnumerator WaitForSignRevealRoutine()
        {
            while (_state != FirstContactVehicleDriveState.Inactive && !_signVisible)
            {
                yield return null;
            }
        }

        public IEnumerator WaitUntilStoppedRoutine()
        {
            while (_state != FirstContactVehicleDriveState.Inactive &&
                   _state != FirstContactVehicleDriveState.Stopped)
            {
                yield return null;
            }
        }

        public void DetachPlayer(Transform playerTransform)
        {
            if (playerTransform == null)
            {
                return;
            }

            CapturePlayerAuthoredPose(playerTransform);
            if (playerTransform.parent != null)
            {
                playerTransform.SetParent(null, true);
            }
        }

        public void StopAndRestore()
        {
            _state = FirstContactVehicleDriveState.Inactive;
            _signVisible = false;
            _brakingRequested = false;
            _cruisePaused = false;

            if (_vehicleRoot != null)
            {
                _vehicleRoot.SetPositionAndRotation(
                    _authoredVehicleStartPosition,
                    _authoredVehicleStartRotation);
                _vehicleRoot = null;
            }

            RestorePlayerAuthoredPose();
        }

        private void UpdateCruise(float deltaTime)
        {
            Vector3 entry = GetRouteLocalPosition(turnEntryAnchor);
            Vector3 holdPosition = entry - GetStraightTravelDirection() * turnLeadDistance;
            _routeLocalPosition = Vector3.MoveTowards(
                _routeLocalPosition,
                holdPosition,
                EffectiveCruiseSpeed * deltaTime);
            _routeYaw = GetYaw(GetStraightTravelDirection());
        }

        private void UpdateTurnLead(float deltaTime)
        {
            Vector3 entry = GetRouteLocalPosition(turnEntryAnchor);
            _routeLocalPosition = Vector3.MoveTowards(
                _routeLocalPosition,
                entry,
                EffectiveCruiseSpeed * deltaTime);
            _routeYaw = GetYaw(GetStraightTravelDirection());

            if (Vector3.SqrMagnitude(_routeLocalPosition - entry) < 0.0001f)
            {
                _routeLocalPosition = entry;
                _stateElapsed = 0f;
                _state = FirstContactVehicleDriveState.TurningRight;
            }
        }

        private void UpdateTurn(float deltaTime)
        {
            _stateElapsed += deltaTime;
            float progress = Mathf.Clamp01(_stateElapsed / turnSeconds);
            _routeLocalPosition = EvaluateTurnPosition(progress);
            _routeYaw = GetYaw(EvaluateTurnTangent(progress));

            if (progress >= 1f)
            {
                _routeLocalPosition = GetRouteLocalPosition(turnExitAnchor);
                _routeYaw = GetYaw(GetApproachDirection());
                _stateElapsed = 0f;
                _signVisible = true;
                _state = FirstContactVehicleDriveState.Approaching;
            }
        }

        private void UpdateApproach(float deltaTime)
        {
            Vector3 stop = GetRouteLocalPosition(parkingStopAnchor);
            Vector3 exit = GetRouteLocalPosition(turnExitAnchor);
            Vector3 brakingPoint = Vector3.MoveTowards(stop, exit, brakingDistance);
            _routeLocalPosition = Vector3.MoveTowards(
                _routeLocalPosition,
                brakingPoint,
                approachSpeed * deltaTime);
            _routeYaw = GetYaw(GetApproachDirection());

            if (Vector3.SqrMagnitude(_routeLocalPosition - brakingPoint) < 0.0001f)
            {
                _routeLocalPosition = brakingPoint;
                _state = FirstContactVehicleDriveState.WaitingToBrake;
                if (_brakingRequested)
                {
                    StartBraking();
                }
            }
        }

        private void StartBraking()
        {
            _brakeStartLocalPosition = _routeLocalPosition;
            _stateElapsed = 0f;
            _state = FirstContactVehicleDriveState.Braking;
        }

        private void UpdateBraking(float deltaTime)
        {
            _stateElapsed += deltaTime;
            float progress = Mathf.Clamp01(_stateElapsed / brakingSeconds);
            float easedProgress = 1f - (1f - progress) * (1f - progress);
            Vector3 stop = GetRouteLocalPosition(parkingStopAnchor);
            _routeLocalPosition = Vector3.Lerp(
                _brakeStartLocalPosition,
                stop,
                easedProgress);
            _routeYaw = Mathf.Lerp(
                _routeYaw,
                GetYaw(GetApproachDirection()),
                easedProgress);

            if (progress >= 1f)
            {
                _routeLocalPosition = stop;
                _routeYaw = GetYaw(GetApproachDirection());
                _state = FirstContactVehicleDriveState.Stopped;
                ApplyVehiclePose(false);
            }
        }

        private void ApplyVehiclePose(bool includeRoadMotion)
        {
            if (_vehicleRoot == null || routeEnvironment == null)
            {
                return;
            }

            Quaternion routeRotation =
                routeEnvironment.rotation * Quaternion.Euler(0f, _routeYaw, 0f);
            Vector3 routePosition = routeEnvironment.TransformPoint(_routeLocalPosition);
            float wave = Mathf.Sin(Time.unscaledTime * vehicleBobFrequency);
            float strength = includeRoadMotion ? 1f : 0f;
            routePosition += routeRotation *
                             (Vector3.up * (wave * vehicleBobHeight * strength));
            routeRotation *= Quaternion.Euler(
                0f,
                0f,
                wave * vehicleRollDegrees * strength);
            _vehicleRoot.SetPositionAndRotation(routePosition, routeRotation);
        }

        private bool EnsureVehicleRig()
        {
            if (_vehicleRoot != null)
            {
                return true;
            }

            if (!HasAuthoredRoute())
            {
                Debug.LogError(
                    "[FirstContactVehicleRoute] The scene-authored SurfaceDriveRoute_Graybox " +
                    "and its route anchors are required. Runtime environment generation is disabled.",
                    this);
                return false;
            }

            _vehicleRoot = vehicleMotionRoot;
            CaptureAuthoredPose();
            CapturePlayerAuthoredPose(_playerReference);
            return true;
        }

        private bool HasAuthoredRoute()
        {
            return routeEnvironment != null &&
                   vehicleMotionRoot != null &&
                   cruiseStartAnchor != null &&
                   turnEntryAnchor != null &&
                   turnExitAnchor != null &&
                   parkingStopAnchor != null;
        }

        private void CaptureAuthoredPose()
        {
            if (_authoredPoseCaptured || vehicleMotionRoot == null)
            {
                return;
            }

            _authoredVehicleStartPosition = vehicleMotionRoot.position;
            _authoredVehicleStartRotation = vehicleMotionRoot.rotation;
            _authoredPoseCaptured = true;
        }

        private void CapturePlayerAuthoredPose(Transform playerTransform)
        {
            if (_playerAuthoredParent != null || playerTransform == null)
            {
                return;
            }

            _playerAuthoredParent = playerTransform.parent;
            _playerAuthoredSiblingIndex = playerTransform.GetSiblingIndex();
            _playerAuthoredLocalPosition = playerTransform.localPosition;
            _playerAuthoredLocalRotation = playerTransform.localRotation;
        }

        private void RestorePlayerAuthoredPose()
        {
            if (!Application.isPlaying ||
                _playerReference == null ||
                _playerAuthoredParent == null ||
                !_playerAuthoredParent.gameObject.activeInHierarchy)
            {
                return;
            }

            _playerReference.SetParent(_playerAuthoredParent, false);
            _playerReference.localPosition = _playerAuthoredLocalPosition;
            _playerReference.localRotation = _playerAuthoredLocalRotation;
            _playerReference.SetSiblingIndex(_playerAuthoredSiblingIndex);
        }

        private float GetAuthoredStraightDistance()
        {
            if (routeEnvironment == null ||
                cruiseStartAnchor == null ||
                turnEntryAnchor == null)
            {
                return 0f;
            }

            return Vector3.Distance(
                GetRouteLocalPosition(cruiseStartAnchor),
                GetRouteLocalPosition(turnEntryAnchor));
        }

        private float EffectiveCruiseSpeed =>
            _plannedCruiseSpeed > 0.01f ? _plannedCruiseSpeed : cruiseSpeed;

        private Vector3 GetStraightTravelDirection()
        {
            Vector3 direction =
                GetRouteLocalPosition(turnEntryAnchor) -
                GetRouteLocalPosition(cruiseStartAnchor);
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.left;
        }

        private Vector3 GetApproachDirection()
        {
            Vector3 direction =
                GetRouteLocalPosition(parkingStopAnchor) -
                GetRouteLocalPosition(turnExitAnchor);
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.forward;
        }

        private Vector3 EvaluateTurnPosition(float progress)
        {
            GetTurnControlPoints(
                out Vector3 p0,
                out Vector3 p1,
                out Vector3 p2,
                out Vector3 p3,
                out Vector3 p4,
                out Vector3 p5);
            float t = Mathf.Clamp01(progress);
            float oneMinusT = 1f - t;
            float oneMinusT2 = oneMinusT * oneMinusT;
            float oneMinusT3 = oneMinusT2 * oneMinusT;
            float oneMinusT4 = oneMinusT3 * oneMinusT;
            float oneMinusT5 = oneMinusT4 * oneMinusT;
            float t2 = t * t;
            float t3 = t2 * t;
            float t4 = t3 * t;
            float t5 = t4 * t;
            return p0 * oneMinusT5 +
                   p1 * (5f * oneMinusT4 * t) +
                   p2 * (10f * oneMinusT3 * t2) +
                   p3 * (10f * oneMinusT2 * t3) +
                   p4 * (5f * oneMinusT * t4) +
                   p5 * t5;
        }

        private Vector3 EvaluateTurnTangent(float progress)
        {
            GetTurnControlPoints(
                out Vector3 p0,
                out Vector3 p1,
                out Vector3 p2,
                out Vector3 p3,
                out Vector3 p4,
                out Vector3 p5);
            float t = Mathf.Clamp01(progress);
            float oneMinusT = 1f - t;
            float oneMinusT2 = oneMinusT * oneMinusT;
            float oneMinusT3 = oneMinusT2 * oneMinusT;
            float oneMinusT4 = oneMinusT3 * oneMinusT;
            float t2 = t * t;
            float t3 = t2 * t;
            float t4 = t3 * t;
            Vector3 tangent = 5f * (
                (p1 - p0) * oneMinusT4 +
                (p2 - p1) * (4f * oneMinusT3 * t) +
                (p3 - p2) * (6f * oneMinusT2 * t2) +
                (p4 - p3) * (4f * oneMinusT * t3) +
                (p5 - p4) * t4);
            return tangent.sqrMagnitude > 0.0001f
                ? tangent.normalized
                : GetApproachDirection();
        }

        private void GetTurnControlPoints(
            out Vector3 p0,
            out Vector3 p1,
            out Vector3 p2,
            out Vector3 p3,
            out Vector3 p4,
            out Vector3 p5)
        {
            p0 = GetRouteLocalPosition(turnEntryAnchor);
            p5 = GetRouteLocalPosition(turnExitAnchor);
            float chordDistance = Vector3.Distance(p0, p5);
            float controlDistance = Mathf.Clamp(
                cruiseSpeed * turnSeconds / 5f,
                chordDistance * 0.14f,
                chordDistance * 0.32f);
            Vector3 entryDirection = GetStraightTravelDirection();
            Vector3 exitDirection = GetApproachDirection();
            p1 = p0 + entryDirection * controlDistance;
            p2 = p0 + entryDirection * (controlDistance * 2f);
            p4 = p5 - exitDirection * controlDistance;
            p3 = p5 - exitDirection * (controlDistance * 2f);
        }

        private Vector3 GetRouteLocalPosition(Transform anchor)
        {
            return routeEnvironment != null && anchor != null
                ? routeEnvironment.InverseTransformPoint(anchor.position)
                : Vector3.zero;
        }

        private static float GetYaw(Vector3 localDirection)
        {
            localDirection.y = 0f;
            return localDirection.sqrMagnitude > 0.0001f
                ? Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg
                : 0f;
        }

    }
}
