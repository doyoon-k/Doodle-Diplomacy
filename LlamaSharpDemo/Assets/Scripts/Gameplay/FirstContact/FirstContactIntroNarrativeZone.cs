using System;
using UnityEngine;

namespace DoodleDiplomacy.Gameplay.FirstContact
{
    public enum FirstContactIntroNarrativeStage
    {
        PizzaApproach,
        CitizenEncounter,
        PrivateExchange,
        SecretDoorReveal,
        ElevatorBoard
    }

    [Flags]
    public enum FirstContactIntroZoneActors
    {
        None = 0,
        Player = 1 << 0,
        Director = 1 << 1,
        PlayerAndDirector = Player | Director
    }

    /// <summary>
    /// A scene-authored volume used as the actual spatial condition for an intro beat.
    /// It polls authored actor transforms so it also works for the guide, which does not
    /// need a Rigidbody or a gameplay collider.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class FirstContactIntroNarrativeZone : MonoBehaviour
    {
        [SerializeField] private string displayName = "Narrative Zone";
        [SerializeField] private FirstContactIntroNarrativeStage stage;
        [SerializeField, Min(0)] private int sequenceOrder;
        [SerializeField] private FirstContactIntroZoneActors requiredActors =
            FirstContactIntroZoneActors.Player;
        [SerializeField, Min(0f)] private float enterDelaySeconds;
        [SerializeField] private string dialogueEvent = string.Empty;
        [SerializeField] private string followupDialogueEvent = string.Empty;
        [SerializeField] private FirstContactIntroGuidePoint guideHoldPoint;
        [SerializeField] private bool oneShot = true;
        [SerializeField] private Color gizmoColor = new(1f, 0.65f, 0.1f, 0.28f);

        private BoxCollider _box;
        private Transform _player;
        private Transform _director;
        private bool _armed;
        private bool _triggered;
        private bool _conditionWasMet;
        private float _conditionMetAt;
        private bool _rememberActorEntries;
        private bool _playerEntered;
        private bool _directorEntered;

        public event Action<FirstContactIntroNarrativeZone> Activated;

        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? gameObject.name
            : displayName;
        public FirstContactIntroNarrativeStage Stage => stage;
        public int SequenceOrder => sequenceOrder;
        public FirstContactIntroZoneActors RequiredActors => requiredActors;
        public string DialogueEvent => dialogueEvent;
        public string FollowupDialogueEvent => followupDialogueEvent;
        public FirstContactIntroGuidePoint GuideHoldPoint => guideHoldPoint;
        public Color GizmoColor => gizmoColor;
        public bool IsArmed => _armed;
        public bool HasTriggered => _triggered;
        public bool IsConditionMet => EvaluateActorCondition();

        private BoxCollider ZoneBox
        {
            get
            {
                if (_box == null)
                {
                    _box = GetComponent<BoxCollider>();
                }

                return _box;
            }
        }

        private void Reset()
        {
            BoxCollider box = ZoneBox;
            box.isTrigger = true;
            box.size = new Vector3(3f, 2.5f, 2f);
        }

        private void OnValidate()
        {
            BoxCollider box = ZoneBox;
            if (box != null)
            {
                box.isTrigger = true;
            }

            sequenceOrder = Mathf.Max(0, sequenceOrder);
            enterDelaySeconds = Mathf.Max(0f, enterDelaySeconds);
        }

        private void Update()
        {
            if (!_armed || (_triggered && oneShot))
            {
                return;
            }

            bool conditionMet = EvaluateActivationCondition();
            if (!conditionMet)
            {
                _conditionWasMet = false;
                _conditionMetAt = 0f;
                return;
            }

            if (!_conditionWasMet)
            {
                _conditionWasMet = true;
                _conditionMetAt = Time.unscaledTime;
            }

            if (Time.unscaledTime - _conditionMetAt >= enterDelaySeconds)
            {
                Activate();
            }
        }

        public void BindActors(Transform player, Transform director)
        {
            _player = player;
            _director = director;
        }

        public void Arm(
            bool resetTriggered = false,
            bool rememberActorEntries = false)
        {
            if (resetTriggered)
            {
                _triggered = false;
            }

            _armed = true;
            _rememberActorEntries = rememberActorEntries;
            _playerEntered = false;
            _directorEntered = false;
            _conditionWasMet = false;
            _conditionMetAt = 0f;
        }

        public void Disarm()
        {
            _armed = false;
            _conditionWasMet = false;
            _conditionMetAt = 0f;
        }

        public void ResetRuntimeState()
        {
            Disarm();
            _triggered = false;
            _rememberActorEntries = false;
            _playerEntered = false;
            _directorEntered = false;
            _player = null;
            _director = null;
        }

        public bool Contains(Vector3 worldPosition)
        {
            BoxCollider box = ZoneBox;
            if (box == null || !box.enabled || !gameObject.activeInHierarchy)
            {
                return false;
            }

            Vector3 local = transform.InverseTransformPoint(worldPosition) - box.center;
            Vector3 halfSize = box.size * 0.5f;
            const float tolerance = 0.0001f;
            return Mathf.Abs(local.x) <= halfSize.x + tolerance &&
                   Mathf.Abs(local.y) <= halfSize.y + tolerance &&
                   Mathf.Abs(local.z) <= halfSize.z + tolerance;
        }

        public void Configure(
            string zoneDisplayName,
            FirstContactIntroNarrativeStage zoneStage,
            int order,
            FirstContactIntroZoneActors actors,
            string primaryDialogueEvent,
            string secondaryDialogueEvent,
            FirstContactIntroGuidePoint holdPoint,
            Color color)
        {
            displayName = zoneDisplayName;
            stage = zoneStage;
            sequenceOrder = Mathf.Max(0, order);
            requiredActors = actors;
            dialogueEvent = primaryDialogueEvent ?? string.Empty;
            followupDialogueEvent = secondaryDialogueEvent ?? string.Empty;
            guideHoldPoint = holdPoint;
            gizmoColor = color;
            ZoneBox.isTrigger = true;
        }

        private bool EvaluateActorCondition()
        {
            if (requiredActors == FirstContactIntroZoneActors.None)
            {
                return false;
            }

            if ((requiredActors & FirstContactIntroZoneActors.Player) != 0 &&
                (_player == null || !Contains(_player.position)))
            {
                return false;
            }

            if ((requiredActors & FirstContactIntroZoneActors.Director) != 0 &&
                (_director == null || !Contains(_director.position)))
            {
                return false;
            }

            return true;
        }

        private bool EvaluateActivationCondition()
        {
            if (!_rememberActorEntries)
            {
                return EvaluateActorCondition();
            }

            if (requiredActors == FirstContactIntroZoneActors.None)
            {
                return false;
            }

            if ((requiredActors & FirstContactIntroZoneActors.Player) != 0 &&
                _player != null &&
                Contains(_player.position))
            {
                _playerEntered = true;
            }

            if ((requiredActors & FirstContactIntroZoneActors.Director) != 0 &&
                _director != null &&
                Contains(_director.position))
            {
                _directorEntered = true;
            }

            bool playerReady =
                (requiredActors & FirstContactIntroZoneActors.Player) == 0 ||
                _playerEntered;
            bool directorReady =
                (requiredActors & FirstContactIntroZoneActors.Director) == 0 ||
                _directorEntered;
            return playerReady && directorReady;
        }

        private void Activate()
        {
            if (!_armed || (_triggered && oneShot))
            {
                return;
            }

            _triggered = true;
            if (oneShot)
            {
                _armed = false;
            }

            Activated?.Invoke(this);
        }

        private void OnDrawGizmos()
        {
            BoxCollider box = ZoneBox;
            if (box == null)
            {
                return;
            }

            Color color = gizmoColor;
            if (Application.isPlaying)
            {
                if (_triggered)
                {
                    color = new Color(0.2f, 0.95f, 0.35f, 0.32f);
                }
                else if (_armed)
                {
                    color = new Color(1f, 0.8f, 0.15f, 0.36f);
                }
                else
                {
                    color.a *= 0.45f;
                }
            }

            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousColor = Gizmos.color;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = color;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(color.r, color.g, color.b, 0.95f);
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousColor;
        }
    }
}
